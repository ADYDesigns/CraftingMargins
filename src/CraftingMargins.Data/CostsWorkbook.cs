// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using CraftingMargins.Core;
using CsvHelper;
using CsvHelper.Configuration;

namespace CraftingMargins.Data;

/// <summary>
/// The cost files as one Excel workbook that is easy to fill in: a page of instructions, then
/// Materials, Parts and Cost rules with drop-downs, and the Cost rules sheet already listing
/// every listing with the option names and values Etsy uses.  It comes back in by dropping it
/// on Home, through exactly the same checks as the three CSV files (<see cref="CostsFile"/>).
/// </summary>
public static class CostsWorkbook
{
    public const string FileName = "Crafting Margins costs.xlsx";
    public const string MaterialsSheet = "Materials";
    public const string PartsSheet = "Parts";
    public const string RulesSheet = "Cost rules";
    public const string GuideSheet = "How to fill this in";
    public const string OptionsColumn = "Options on Etsy (for reference)";

    public static bool IsWorkbook(string path) => string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- writing

    /// <summary>Writes the workbook: what is saved now, plus a starter row for every listing that has no rules yet.</summary>
    public static void Write(Database db, string path)
    {
        var (m, ps, rs) = CostsFile.BuildSheets(db);
        var listings = db.LoadListings();
        var items = db.LoadItems();
        var options = OptionsByListing(listings, items);

        // Cost rules: the export's rows, an extra column showing each listing's options, and a
        // starter row for each listing that has none, so nobody has to look up a listing number.
        var rules = new Sheet(RulesSheet);
        rules.Add([.. CostsFile.RulesHeader, OptionsColumn]);
        var withRules = new HashSet<string>();
        foreach (var row in rs.Rows.Skip(1))
        {
            var id = row[0] as string ?? "";
            var first = withRules.Add(id);
            rules.Add([.. row, first ? options.GetValueOrDefault(id, "") : ""]);
        }
        var titles = items.GroupBy(i => i.ListingId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SaleDate).First().ItemName);
        foreach (var l in listings) titles[l.Key] = l.Title;      // the current title wins
        var forSale = listings.Select(l => l.Key).ToHashSet();
        foreach (var (id, title) in titles.Where(t => !withRules.Contains(t.Key))
                     .OrderBy(t => forSale.Contains(t.Key) ? 0 : 1).ThenBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
            rules.Add(id, title, "", "", null, "", null, "", options.GetValueOrDefault(id, forSale.Contains(id) ? "" : "Not for sale now (sold before)"));

        rules.ColumnWidths = [14, 48, 18, 40, 11, 34, 10, 14, 70];
        rules.Lists.Add(new ColumnList(5, $"{PartsSheet}!$A$2:$A$5000", IsRange: true, Strict: false,
            "Pick a part from the Parts sheet, or type a name and add it there."));
        ps.ColumnWidths = [40, 13, 16, 13, 26, 10, 50];
        ps.Lists.Add(new ColumnList(4, $"{MaterialsSheet}!$A$2:$A$1000", IsRange: true, Strict: false,
            "Pick a material from the Materials sheet, or type a name and add it there."));
        m.ColumnWidths = [32, 8, 14, 13];
        m.Lists.Add(new ColumnList(1, string.Join(",", Units.All), IsRange: false, Strict: true,
            "The unit the material is bought and used in. \"each\" is for things bought by the piece."));

        Export.WriteXlsx(path, [Guide(), m, ps, rules]);
        Log.Write($"Cost workbook written: {m.Rows.Count - 1} material price(s), {ps.Rows.Count - 1} part row(s), {rules.Rows.Count - 1} rule row(s).");
    }

    /// <summary>
    /// "Size: Small | Large;  Color: Red | Blue" for each listing: the options it has now, and
    /// values seen on past orders that it no longer offers (an old name still needs a rule for
    /// those orders).  Only option names the listing has now are shown, so personalization a
    /// buyer typed on an old order never appears.
    /// </summary>
    private static Dictionary<string, string> OptionsByListing(List<Listing> listings, List<OrderItem> items)
    {
        var sold = items.GroupBy(i => i.ListingId).ToDictionary(g => g.Key, g => g.SelectMany(i => Variations.Parse(i.Variations)).ToList());
        var result = new Dictionary<string, string>();
        foreach (var l in listings)
        {
            if (l.Variations.Count == 0) { result[l.Key] = "No options: one rule with no conditions."; continue; }
            var parts = new List<string>();
            foreach (var v in l.Variations)
            {
                var text = $"{v.Name}: {string.Join(" | ", v.Values)}";
                var old = sold.GetValueOrDefault(l.Key, [])
                    .Where(p => string.Equals(p.Key, v.Name, StringComparison.OrdinalIgnoreCase) && !v.Values.Contains(p.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                    .Select(p => p.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (old.Count > 0) text += $"  (past orders also: {string.Join(" | ", old)})";
                parts.Add(text);
            }
            result[l.Key] = string.Join(";   ", parts);
        }
        return result;
    }

    private static Sheet Guide()
    {
        var g = new Sheet(GuideSheet) { FreezeHeader = false, ColumnWidths = [120] };
        g.Add("How to fill in your costs");
        foreach (var line in new[]
        {
            "",
            "Fill in the three sheets in order: Materials, then Parts, then Cost rules. Save the file, then drop it on the Home tab of Crafting Margins.",
            "Bringing it back in only changes what the file mentions: a material is added or updated, a part's recipe is replaced, and a listing's rules are replaced. Anything the file leaves out is kept.",
            "",
            "MATERIALS: everything you buy and use up, one row per price.",
            "   Material: a name you'll recognize, such as \"PLA filament\", \"Soy wax\" or \"Magnets\".",
            "   Unit: pick from the list. Use \"each\" for things bought by the piece (magnets, wicks, jars, zippers).",
            "   Cost per unit: what one unit costs, such as 0.015 for a gram of filament at $15 per kilogram.",
            "   Price from: the date the price started. If the price changes, add another row with the new price and date; older sales keep the old price.",
            "",
            "PARTS: one thing you make, as a recipe. One row per material in it, with the same part name on each row.",
            "   Amounts from: the date this recipe started (use the same date on all of a part's rows).",
            "   Machine minutes: printer, laser or kiln time for one. Your minutes: your own hands-on time for one. Either can be 0.",
            "   Material: pick from the list (the names on the Materials sheet). Amount: how much of it goes into one, in that material's unit, including waste that's always lost (supports, trimmings).",
            "   Example: \"Small candle\" with three rows: Soy wax 7 (oz), Fragrance oil 0.6 (oz), Wick 1 (each).",
            "",
            "COST RULES: which parts each listing uses. Every listing is already listed; the last column shows its options exactly as Etsy names them.",
            "   A listing with no options: fill in one row, leave Conditions empty, and pick the Part and Quantity.",
            "   A listing with options: add a row for each option that changes the cost, with Conditions such as  Size:Large  or  Size:Large,Color Scheme:Wood  (copy the names and values from the last column).",
            "   Leave out options that don't change the cost, such as color: a rule only checks the options named in its Conditions.",
            "   Every rule that matches a sale adds up. A part used by several options can have its own rule, with Conditions naming just that option.",
            "   Two parts in one rule: add a second row with the same Listing ID, Rule and Conditions.",
            "   Multiply by: the name of an option holding a number, such as  Pack Size  for \"4 Pack\". The quantity is multiplied by that number.",
            "   Fixed cost: an amount per item sold on top of the parts, such as a bought-in item. Leave it empty if there is none.",
            "   Past orders also: option values an older version of the listing had. Add a rule for them too, or those past sales stay uncosted.",
            "   Rows left with no Part and no Fixed cost are skipped, so you can leave the starter rows of listings you're not ready to cost.",
            "",
            "Any mistake is reported with the sheet and row when you drop the file on Home, and nothing from that sheet on is brought in until it's fixed.",
        })
            g.Add(line);
        return g;
    }

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// Imports the Materials, Parts and Cost rules sheets, in that order (each needs the one before).
    /// Stops at the first sheet with a problem; the sheets before it stay imported.
    /// </summary>
    public static List<ImportResult> Import(Database db, string path)
    {
        var file = Path.GetFileName(path);
        Dictionary<string, List<string[]>> sheets;
        try { sheets = XlsxReader.Read(path); }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or IOException)
        {
            return [new(file, CsvKind.Unknown, 0, "Could not read the workbook: " + ex.Message + " (Is it open in Excel? Save it as an .xlsx workbook.)", false)];
        }

        var steps = new (string Sheet, CsvKind Kind, string[] Header, string[] DateColumns, Func<CsvReader, string, ImportResult> Run)[]
        {
            (MaterialsSheet, CsvKind.Materials, CostsFile.MaterialsHeader, ["Price from"], (csv, n) => CostsFile.ImportMaterials(db, csv, n)),
            (PartsSheet, CsvKind.Parts, CostsFile.PartsHeader, ["Amounts from"], (csv, n) => CostsFile.ImportParts(db, csv, n)),
            (RulesSheet, CsvKind.Rules, CostsFile.RulesHeader, [], (csv, n) => CostsFile.ImportRules(db, csv, n)),
        };
        if (!steps.Any(s => sheets.ContainsKey(s.Sheet)))
            return [new(file, CsvKind.Unknown, 0, $"This workbook has no {MaterialsSheet}, {PartsSheet} or {RulesSheet} sheet. Only the cost workbook from Products & Costs can be imported as .xlsx; Etsy files need to be CSV.", false)];

        var results = new List<ImportResult>();
        foreach (var step in steps)
        {
            if (!sheets.TryGetValue(step.Sheet, out var rows)) continue;
            var name = $"{file} ({step.Sheet})";
            try
            {
                var header = rows.Count > 0 ? rows[0].Select(h => h.Trim()).ToArray() : [];
                var missing = step.Header.Where(h => !header.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
                if (missing.Count > 0) throw new FormatException($"the first row needs the column(s) {string.Join(", ", missing.Select(c => $"\"{c}\""))}.");
                var body = rows.Skip(1).ToList();
                if (step.Kind == CsvKind.Rules) body = body.Select(r => SkipStarterRow(header, r) ? [] : r).ToList();
                using var csv = ToCsv(header, body, step.DateColumns);
                csv.Read();
                csv.ReadHeader();
                var result = step.Run(csv, name);
                db.LogImport(name, step.Kind.ToString(), result.Rows, result.Summary);
                Log.Write($"Imported {name}: {step.Kind}, {result.Rows} rows. {result.Summary}");
                results.Add(result);
            }
            catch (FormatException ex)
            {
                Log.Write($"Import failed for {name}: {ex.GetType().Name}");     // the message may quote a cost: not logged
                results.Add(new(name, step.Kind, 0, $"Sheet \"{step.Sheet}\": {ex.Message}" +
                    (step.Sheet != RulesSheet ? "  The sheets after it weren't brought in." : ""), false));
                break;
            }
        }
        return results;
    }

    /// <summary>A starter row nobody filled in: a listing with no part and no fixed cost.</summary>
    private static bool SkipStarterRow(string[] header, string[] row)
    {
        string Cell(string col) { var i = Array.FindIndex(header, h => h.Equals(col, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i < row.Length ? row[i].Trim() : ""; }
        var fixedCost = Cell("Fixed cost");
        return Cell("Part").Length == 0 && (fixedCost.Length == 0 || (decimal.TryParse(fixedCost, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f == 0));
    }

    /// <summary>Turns a sheet back into the CSV the cost import reads, with dates as yyyy-MM-dd.</summary>
    private static CsvReader ToCsv(string[] header, List<string[]> rows, string[] dateColumns)
    {
        var dates = dateColumns.Select(c => Array.FindIndex(header, h => h.Equals(c, StringComparison.OrdinalIgnoreCase))).Where(i => i >= 0).ToHashSet();
        var text = new StringWriter();
        using (var w = new CsvWriter(text, CultureInfo.InvariantCulture, leaveOpen: true))
        {
            foreach (var h in header) w.WriteField(h);
            w.NextRecord();
            foreach (var r in rows)
            {
                for (int c = 0; c < header.Length; c++)
                {
                    var cell = c < r.Length ? r[c] : "";
                    w.WriteField(dates.Contains(c) ? XlsxReader.DateText(cell) : cell);
                }
                w.NextRecord();
            }
        }
        return new CsvReader(new StringReader(text.ToString()), new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null, MissingFieldFound = null, TrimOptions = TrimOptions.Trim,
        });
    }
}
