// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.IO.Compression;
using System.Text;
using CraftingMargins.Core;
using CraftingMargins.Data;
using Microsoft.Data.Sqlite;

namespace CraftingMargins.Tests;

/// <summary>The cost workbook (one .xlsx to fill in) and the cost files' numbers.  Made-up shop data only.</summary>
public sealed class CostWorkbookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private readonly Database _db;
    private static readonly DateOnly Day = new(2026, 1, 1);

    public CostWorkbookTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "test.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>A filament at 1.5¢ a gram, a printed part, and one listing costed by size; a second listing has no rules yet.</summary>
    private void SeedShop()
    {
        _db.SaveMaterials([new Material { Name = "PLA filament", Unit = "g", Prices = [new(Day, 0.015m)] }, new Material { Name = "Magnet", Unit = Units.Each, Prices = [new(Day, 0.085m)] }]);
        var mats = _db.LoadMaterials().ToDictionary(m => m.Name, m => m.Id);
        _db.SaveParts([new Part { Name = "Coaster", Versions = [new(Day, [new(mats["PLA filament"], 26.5m), new(mats["Magnet"], 4m)], 95, 0)] }]);
        var part = _db.LoadParts().Single().Id;
        _db.SaveRules([new MappingRule { ListingId = "700", Name = "Large", Conditions = new() { ["Size"] = "Large" }, Parts = [new(part, 1)] }]);
        _db.ReplaceApiListings(
        [
            new Listing { ListingId = "700", Title = "Coaster", State = "active", Variations = [new("Size", ["Small", "Large"])],
                Offerings = [new(new() { ["Size"] = "Small" }, 10m, 5, true), new(new() { ["Size"] = "Large" }, 12m, 5, true)] },
            new Listing { ListingId = "800", Title = "Pen cup", State = "active", Variations = [new("Size", ["Short", "Tall"])],
                Offerings = [new(new() { ["Size"] = "Short" }, 9m, 5, true), new(new() { ["Size"] = "Tall" }, 11m, 5, true)] },
        ]);
        // A past order used a size name the listing no longer has, and carried a buyer's personalization.
        _db.UpsertItems([new OrderItem { TransactionId = "t1", OrderId = "o1", ListingId = "800", SaleDate = Day.AddMonths(2), ItemName = "Pen cup", Quantity = 1, Price = 9m,
            Variations = Variations.Format([new("Size", "Mini"), new("Name on cup", "Dana")]) }]);
    }

    [Fact]
    public void Exported_cost_files_keep_prices_and_amounts_in_full()
    {
        SeedShop();
        var files = CostsFile.Export(_db, Path.Combine(_dir, "csv"));
        var materials = File.ReadAllText(files[0]);
        var parts = File.ReadAllText(files[1]);
        Assert.Contains("PLA filament,g,0.015,2026-01-01", materials);     // was written as 0.02 before 0.9.2
        Assert.Contains("0.085", materials);
        Assert.Contains("26.5", parts);
    }

    [Fact]
    public void Workbook_lists_every_listing_with_its_options_and_comes_back_with_the_same_costs()
    {
        SeedShop();
        var path = Path.Combine(_dir, CostsWorkbook.FileName);
        CostsWorkbook.Write(_db, path);

        var sheets = XlsxReader.Read(path);
        Assert.Equal([CostsWorkbook.GuideSheet, CostsWorkbook.MaterialsSheet, CostsWorkbook.PartsSheet, CostsWorkbook.RulesSheet], sheets.Keys);
        Assert.Equal("0.015", sheets[CostsWorkbook.MaterialsSheet].Single(r => r[0] == "PLA filament")[2]);
        var rules = sheets[CostsWorkbook.RulesSheet];
        Assert.Equal(CostsWorkbook.OptionsColumn, rules[0][^1]);
        var costed = rules.Single(r => r[0] == "700");
        Assert.Equal("Size: Small | Large", costed[^1]);
        var starter = rules.Single(r => r[0] == "800");                    // a listing with no rules gets a row to fill in
        Assert.Equal(("Pen cup", ""), (starter[1], starter[5]));
        Assert.Contains("Size: Short | Tall", starter[^1]);
        Assert.Contains("past orders also: Mini", starter[^1]);
        Assert.DoesNotContain("Dana", string.Join("|", rules.SelectMany(r => r)));     // a buyer's personalization never appears

        var xml = ReadEntry(path, "xl/worksheets/sheet2.xml");            // Materials: the unit is a strict drop-down
        Assert.Contains("type=\"list\"", xml);
        Assert.Contains("errorStyle=\"stop\"", xml);
        Assert.Contains("Materials!$A$2:$A$1000", ReadEntry(path, "xl/worksheets/sheet3.xml"));
        Assert.Contains("Parts!$A$2:$A$5000", ReadEntry(path, "xl/worksheets/sheet4.xml"));

        var fresh = new Database(Path.Combine(_dir, "fresh.db"));
        var results = new CsvImporter(fresh, new AppSettings()).ImportFiles([path]);
        Assert.All(results, r => Assert.True(r.Ok, r.Summary));
        Assert.Equal([CsvKind.Materials, CsvKind.Parts, CsvKind.Rules], results.Select(r => r.Kind));
        Assert.Equal(["700"], fresh.LoadRules().Select(r => r.ListingId));     // the untouched starter row was skipped
        var sold = Variations.Format([new("Size", "Large")]);
        var a = _db.LoadShopData(new AppSettings(), []).Costs.CostOf("700", sold, 1, Day.AddMonths(3));
        var b = fresh.LoadShopData(new AppSettings(), []).Costs.CostOf("700", sold, 1, Day.AddMonths(3));
        Assert.Equal(26.5m * 0.015m + 4 * 0.085m, a.Total);
        Assert.Equal(a, b);
    }

    [Fact]
    public void A_workbook_saved_by_excel_is_read_with_shared_text_dates_and_long_numbers()
    {
        // What Excel writes when it saves: text in a shared list (some as rich text), 0.015 as
        // 1.4999999999999999E-2, a typed date as a day number, empty cells left out.
        var path = Path.Combine(_dir, "from excel.xlsx");
        WriteExcelStyle(path, new()
        {
            ["Materials"] = """
                <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c></row>
                <row r="2"><c r="A2" t="s"><v>4</v></c><c r="B2" t="s"><v>5</v></c><c r="C2"><v>1.4999999999999999E-2</v></c><c r="D2" s="1"><v>46023</v></c></row>
                """,
            ["Parts"] = """
                <row r="1"><c r="A1" t="s"><v>6</v></c><c r="B1" t="s"><v>7</v></c><c r="C1" t="s"><v>8</v></c><c r="D1" t="s"><v>9</v></c><c r="E1" t="s"><v>0</v></c><c r="F1" t="s"><v>10</v></c><c r="G1" t="s"><v>11</v></c></row>
                <row r="3"><c r="A3" t="s"><v>12</v></c><c r="B3"><v>46023</v></c><c r="C3"><v>95</v></c><c r="E3" t="s"><v>4</v></c><c r="F3"><v>26.5</v></c></row>
                """,
            ["Cost rules"] = """
                <row r="1"><c r="A1" t="s"><v>13</v></c><c r="B1" t="s"><v>14</v></c><c r="C1" t="s"><v>15</v></c><c r="D1" t="s"><v>16</v></c><c r="E1" t="s"><v>17</v></c><c r="F1" t="s"><v>6</v></c><c r="G1" t="s"><v>18</v></c><c r="H1" t="s"><v>19</v></c></row>
                <row r="2"><c r="A2"><v>4594565700</v></c><c r="C2" t="str"><v>Coaster</v></c><c r="F2" t="s"><v>12</v></c><c r="G2"><v>2</v></c></row>
                """,
        },
        ["Material", "Unit", "Cost per unit", "Price from", "PLA <b>filament</b>", "g", "Part", "Amounts from", "Machine minutes", "Your minutes",
         "Amount", "Notes", "Coaster", "Listing ID", "Listing name", "Rule", "Conditions", "Fixed cost", "Quantity", "Multiply by"]);

        var fresh = new Database(Path.Combine(_dir, "fresh.db"));
        var results = CostsWorkbook.Import(fresh, path);
        Assert.All(results, r => Assert.True(r.Ok, r.Summary));
        var pla = fresh.LoadMaterials().Single();
        Assert.Equal(("PLA filament", 0.015m, new DateOnly(2026, 1, 1)), (pla.Name, pla.Prices.Single().CostPerUnit, pla.Prices.Single().EffectiveFrom));
        var part = fresh.LoadParts().Single();
        Assert.Equal((26.5m, 95m), (part.Versions.Single().Lines.Single().Amount, part.Versions.Single().MachineMinutes));
        var rule = fresh.LoadRules().Single();
        Assert.Equal(("4594565700", 2m), (rule.ListingId, rule.Parts.Single().Quantity));
    }

    [Fact]
    public void A_mistake_names_the_sheet_and_row_and_stops_the_sheets_after_it()
    {
        var src = new Database(Path.Combine(_dir, "src.db"));
        src.SaveParts([new Part { Name = "Coaster", Versions = [new(Day, [], 95, 0)] }]);
        var path = Path.Combine(_dir, "bad.xlsx");
        CostsWorkbook.Write(src, path);
        // A part that uses a material nobody added.
        var parts = new Sheet("Parts");
        parts.Add([.. CostsFile.PartsHeader]);
        parts.Add("Coaster", Day, new Amount(95), new Amount(0), "Walnut", new Amount(30), "");
        var rules = new Sheet("Cost rules");
        rules.Add([.. CostsFile.RulesHeader]);
        rules.Add("700", "Coaster", "Any", "", null, "Coaster", new Amount(1), "");
        Export.WriteXlsx(path, [parts, rules]);

        var results = CostsWorkbook.Import(_db, path);
        var failed = Assert.Single(results);
        Assert.False(failed.Ok);
        Assert.Contains("Sheet \"Parts\"", failed.Summary);
        Assert.Contains("Row 2", failed.Summary);
        Assert.Contains("Walnut", failed.Summary);
        Assert.Empty(_db.LoadRules());
    }

    [Fact]
    public void Any_other_workbook_is_refused_with_a_reason()
    {
        var path = Path.Combine(_dir, "orders.xlsx");
        Export.WriteXlsx(path, [new Sheet("Orders").Add("Order ID", "Total")]);
        var result = Assert.Single(new CsvImporter(_db, new AppSettings()).ImportFiles([path]));
        Assert.False(result.Ok);
        Assert.Contains("Etsy files need to be CSV", result.Summary);
    }

    [Fact]
    public void Shipping_insurance_bought_with_a_label_counts_as_shipping()
    {
        var c = new Categorizer();
        Assert.Equal(Category.ShippingLabels, c.Categorize("Shipping", "Shipsurance Package Insurance"));
        Assert.Equal(Category.ShippingLabels, c.Categorize("Shipping", "USPS shipping label"));
    }

    private static string ReadEntry(string zipPath, string entry)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var r = new StreamReader(zip.GetEntry(entry)!.Open());
        return r.ReadToEnd();
    }

    /// <summary>A minimal workbook in the shape Excel saves (shared strings, absolute sheet paths).</summary>
    private static void WriteExcelStyle(string path, Dictionary<string, string> sheetRows, string[] shared)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Entry(string name, string xml) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); w.Write(xml); }
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var names = sheetRows.Keys.ToList();
        Entry("xl/workbook.xml", $"""<workbook xmlns="{ns}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{string.Concat(names.Select((n, i) => $"""<sheet name="{n}" sheetId="{i + 1}" r:id="rId{i + 1}"/>"""))}</sheets></workbook>""");
        Entry("xl/_rels/workbook.xml.rels", $"""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{string.Concat(names.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="worksheet" Target="/xl/worksheets/sheet{i + 1}.xml"/>"""))}</Relationships>""");
        Entry("xl/sharedStrings.xml", $"""<sst xmlns="{ns}">{string.Concat(shared.Select(s => s.Contains("<b>")
            ? $"<si><r><t>{s[..s.IndexOf('<')]}</t></r><r><rPr><b/></rPr><t>{s[(s.IndexOf("<b>") + 3)..s.IndexOf("</b>")]}</t></r></si>"
            : $"<si><t>{s}</t></si>"))}</sst>""");
        for (int i = 0; i < names.Count; i++)
            Entry($"xl/worksheets/sheet{i + 1}.xml", $"""<worksheet xmlns="{ns}"><sheetData>{sheetRows[names[i]]}</sheetData></worksheet>""");
    }
}
