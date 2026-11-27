// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using CraftingMargins.Core;
using CsvHelper;

namespace CraftingMargins.Data;

/// <summary>
/// The costs a person types in (materials, parts, cost rules) as three plain CSV files, so
/// that work is never trapped in the app: it can be read, kept, edited in a spreadsheet and
/// brought back in.  Names are used instead of internal numbers, so the files make sense on
/// their own and can be moved to another data folder.
///
/// Bringing a file back in changes only what the file mentions:
/// a material in the file is added or updated (its unit, and the price for each date given);
/// a part in the file has its recipe replaced by the file's; a listing in the file has its
/// rules replaced by the file's.  Anything the file doesn't mention is left alone.
/// </summary>
public static class CostsFile
{
    public const string MaterialsName = "Crafting Margins materials.csv";
    public const string PartsName = "Crafting Margins parts.csv";
    public const string RulesName = "Crafting Margins cost rules.csv";

    public static readonly string[] MaterialsHeader = ["Material", "Unit", "Cost per unit", "Price from"];
    public static readonly string[] PartsHeader = ["Part", "Amounts from", "Machine minutes", "Your minutes", "Material", "Amount", "Notes"];
    public static readonly string[] RulesHeader = ["Listing ID", "Listing name", "Rule", "Conditions", "Fixed cost", "Part", "Quantity", "Multiply by"];

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- export

    /// <summary>Writes the three files into a folder (replacing earlier ones) and returns their paths.</summary>
    public static List<string> Export(Database db, string folder)
    {
        Directory.CreateDirectory(folder);
        var (m, ps, rs) = BuildSheets(db);
        var files = new List<string>();
        foreach (var (sheet, name) in new[] { (m, MaterialsName), (ps, PartsName), (rs, RulesName) })
        {
            var path = Path.Combine(folder, name);
            Data.Export.WriteCsv(path, sheet);
            files.Add(path);
        }
        Log.Write($"Costs exported to {folder}: {m.Rows.Count - 1} material price(s), {ps.Rows.Count - 1} part row(s), {rs.Rows.Count - 1} rule row(s).");
        return files;
    }

    /// <summary>
    /// The saved materials, parts and rules as three tables with the file headers.  Amounts,
    /// prices per unit, minutes and quantities are written in full (0.015, not 0.02).
    /// </summary>
    internal static (Sheet Materials, Sheet Parts, Sheet Rules) BuildSheets(Database db)
    {
        var materials = db.LoadMaterials();
        var parts = db.LoadParts();
        var rules = db.LoadRules();
        var matName = materials.ToDictionary(m => m.Id, m => m.Name);
        var partName = parts.ToDictionary(p => p.Id, p => p.Name);
        // The newest title of each listing, so a row can be recognized by a person; the app only reads the ID.
        var titles = db.LoadItems().GroupBy(i => i.ListingId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SaleDate).First().ItemName);
        foreach (var l in db.LoadListings()) titles.TryAdd(l.Key, l.Title);

        var m = new Sheet("Materials");
        m.Add([.. MaterialsHeader]);
        foreach (var mat in materials.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var p in mat.Prices.OrderBy(p => p.EffectiveFrom))
                m.Add(mat.Name, mat.Unit, new Amount(p.CostPerUnit), p.EffectiveFrom);

        var ps = new Sheet("Parts");
        ps.Add([.. PartsHeader]);
        foreach (var part in parts.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var v in part.Versions.OrderBy(v => v.EffectiveFrom))
            {
                var lines = v.Lines.Where(l => matName.ContainsKey(l.MaterialId)).ToList();
                if (lines.Count == 0) ps.Add(part.Name, v.EffectiveFrom, new Amount(v.MachineMinutes), new Amount(v.OwnMinutes), "", new Amount(0), part.Notes);
                foreach (var l in lines) ps.Add(part.Name, v.EffectiveFrom, new Amount(v.MachineMinutes), new Amount(v.OwnMinutes), matName[l.MaterialId], new Amount(l.Amount), part.Notes);
            }

        var rs = new Sheet("Cost rules");
        rs.Add([.. RulesHeader]);
        foreach (var r in rules.OrderBy(r => r.ListingId, StringComparer.Ordinal).ThenBy(r => r.SortOrder))
        {
            var conditions = Variations.Format(r.Conditions.Select(c => new KeyValuePair<string, string>(c.Key, c.Value)));
            var title = titles.GetValueOrDefault(r.ListingId, "");
            var used = r.Parts.Where(p => partName.ContainsKey(p.PartId)).ToList();
            if (used.Count == 0) rs.Add(r.ListingId, title, r.Name, conditions, new Amount(r.FixedCost), "", new Amount(0), "");
            foreach (var p in used) rs.Add(r.ListingId, title, r.Name, conditions, new Amount(r.FixedCost), partName[p.PartId], new Amount(p.Quantity), p.MultiplyByVariation ?? "");
        }
        return (m, ps, rs);
    }

    // ---------------------------------------------------------------- import

    private static string Get(CsvReader csv, string col) => csv.TryGetField<string>(col, out var v) ? (v ?? "").Trim() : "";

    /// <summary>A number as the export writes it (1234.5); a spreadsheet may have added a currency sign or thousands commas.</summary>
    private static decimal Number(CsvReader csv, string col, int row)
    {
        var text = Get(csv, col);
        if (text.Length == 0) return 0;
        try { return Money.Parse(text); }
        catch (FormatException) { throw new FormatException($"Row {row}: \"{text}\" in the column \"{col}\" isn't a number."); }
    }

    private static DateOnly DateOf(CsvReader csv, string col, int row)
    {
        var text = Get(csv, col);
        if (text.Length == 0) return new DateOnly(2000, 1, 1);
        string[] formats = ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "M/d/yy"];     // a spreadsheet may have re-formatted the date
        return DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new FormatException($"Row {row}: \"{text}\" in the column \"{col}\" isn't a date (use 2026-10-31).");
    }

    public static ImportResult ImportMaterials(Database db, CsvReader csv, string file)
    {
        var materials = db.LoadMaterials();
        var byName = materials.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        int rows = 0, added = 0;
        while (csv.Read())
        {
            rows++;
            var name = Get(csv, "Material");
            if (name.Length == 0) continue;
            var unit = Get(csv, "Unit");
            if (unit.Length == 0) unit = Units.Default;
            var known = Units.All.FirstOrDefault(u => string.Equals(u, unit, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"Row {rows + 1}: \"{unit}\" isn't a unit the app knows. Use one of: {string.Join(", ", Units.All)}.");
            var cost = Number(csv, "Cost per unit", rows + 1);
            if (cost < 0) throw new FormatException($"Row {rows + 1}: the cost of {name} can't be negative.");
            var from = DateOf(csv, "Price from", rows + 1);
            if (!byName.TryGetValue(name, out var m)) { byName[name] = m = new Material { Name = name }; materials.Add(m); added++; }
            m.Unit = known;
            m.Prices.RemoveAll(p => p.EffectiveFrom == from);
            m.Prices.Add(new MaterialPrice(from, cost));
        }
        db.SaveMaterials(materials);
        return new(file, CsvKind.Materials, rows, $"Materials: {rows} price row(s) read, {added} new material(s) added; the rest updated", true);
    }

    public static ImportResult ImportParts(Database db, CsvReader csv, string file)
    {
        var materials = db.LoadMaterials().ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        var parts = db.LoadParts();
        var byName = parts.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        // part name -> (date -> version being built); a part in the file gets exactly the file's versions.
        var fromFile = new Dictionary<string, Dictionary<DateOnly, PartVersion>>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int rows = 0;
        while (csv.Read())
        {
            rows++;
            var name = Get(csv, "Part");
            if (name.Length == 0) continue;
            var from = DateOf(csv, "Amounts from", rows + 1);
            var machine = Number(csv, "Machine minutes", rows + 1);
            var own = Number(csv, "Your minutes", rows + 1);
            var amount = Number(csv, "Amount", rows + 1);
            if (machine < 0 || own < 0 || amount < 0) throw new FormatException($"Row {rows + 1}: amounts and minutes for {name} can't be negative.");
            if (!fromFile.TryGetValue(name, out var versions)) fromFile[name] = versions = [];
            if (!versions.TryGetValue(from, out var v)) versions[from] = v = new PartVersion(from, [], machine, own);
            notes[name] = Get(csv, "Notes");
            var material = Get(csv, "Material");
            if (material.Length == 0) continue;
            if (!materials.TryGetValue(material, out var mat))
                throw new FormatException($"Row {rows + 1}: the part {name} uses the material \"{material}\", which doesn't exist. Import the materials file first, or add it on the Materials tab.");
            if (v.Lines.Any(l => l.MaterialId == mat.Id)) throw new FormatException($"Row {rows + 1}: {name} lists \"{material}\" twice for {Date(from)}.");
            v.Lines.Add(new PartLine(mat.Id, amount));
        }
        int added = 0;
        foreach (var (name, versions) in fromFile)
        {
            if (!byName.TryGetValue(name, out var p)) { byName[name] = p = new Part { Name = name }; parts.Add(p); added++; }
            p.Versions = [.. versions.Values.OrderBy(v => v.EffectiveFrom)];
            p.Notes = notes.GetValueOrDefault(name, "");
        }
        db.SaveParts(parts);
        return new(file, CsvKind.Parts, rows, $"Parts: {fromFile.Count} part(s) read, {added} new; each one's recipe replaced by the file's", true);
    }

    public static ImportResult ImportRules(Database db, CsvReader csv, string file)
    {
        var parts = db.LoadParts().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var rules = db.LoadRules();
        // (listing, rule name, conditions) identifies a rule in the file; its rows are its parts.
        var fromFile = new List<MappingRule>();
        var index = new Dictionary<string, MappingRule>();
        int rows = 0;
        while (csv.Read())
        {
            rows++;
            var listing = Get(csv, "Listing ID");
            if (listing.Length == 0) continue;
            var name = Get(csv, "Rule");
            var conditions = Get(csv, "Conditions");
            var key = string.Join('\u001f', listing, name, conditions);
            if (!index.TryGetValue(key, out var rule))
            {
                var fixedCost = Number(csv, "Fixed cost", rows + 1);
                if (fixedCost < 0) throw new FormatException($"Row {rows + 1}: the fixed cost can't be negative.");
                index[key] = rule = new MappingRule
                {
                    ListingId = listing, Name = name.Length > 0 ? name : "Rule", FixedCost = fixedCost,
                    SortOrder = fromFile.Count(r => r.ListingId == listing),
                    Conditions = new(Variations.Parse(conditions).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
                };
                fromFile.Add(rule);
            }
            var part = Get(csv, "Part");
            if (part.Length == 0) continue;
            if (!parts.TryGetValue(part, out var p))
                throw new FormatException($"Row {rows + 1}: the rule \"{name}\" uses the part \"{part}\", which doesn't exist. Import the parts file first, or add it on the Parts tab.");
            var quantity = Number(csv, "Quantity", rows + 1);
            if (quantity < 0) throw new FormatException($"Row {rows + 1}: the quantity can't be negative.");
            var multiply = Get(csv, "Multiply by");
            rule.Parts.Add(new RulePart(p.Id, quantity, multiply.Length > 0 ? multiply : null));
        }
        var listings = fromFile.Select(r => r.ListingId).ToHashSet();
        rules.RemoveAll(r => listings.Contains(r.ListingId));
        rules.AddRange(fromFile);
        db.SaveRules(rules);
        return new(file, CsvKind.Rules, rows, $"Cost rules: {fromFile.Count} rule(s) for {listings.Count} listing(s); those listings' rules replaced by the file's", true);
    }
}
