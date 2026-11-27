// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace CraftingMargins.Data;

/// <summary>
/// Reads the cell text of each sheet in an .xlsx file: just enough for the app's own cost
/// workbook after a person has filled it in with Excel, LibreOffice or Numbers.  Like the
/// writer in <see cref="Export"/>, it is plain zip + XML, so no spreadsheet library is needed.
/// Formulas are read as the value they last showed.  Numbers come back as plain text
/// ("0.015", "4594565700"); a date comes back as Excel's day number unless the caller converts it.
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Sheet name → rows of cell text (a missing cell is "").  Sheet names compare without case.</summary>
    public static Dictionary<string, List<string[]>> Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Load(string name)
        {
            var e = zip.GetEntry(name) ?? throw new InvalidDataException($"The workbook is missing {name}.");
            using var s = e.Open();
            return XDocument.Load(s);
        }

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is not null)
            foreach (var si in Load("xl/sharedStrings.xml").Root!.Elements(Main + "si"))
                shared.Add(string.Concat(si.Descendants(Main + "t").Select(t => t.Value)));   // plain or rich text

        var rels = Load("xl/_rels/workbook.xml.rels").Root!.Elements(PkgRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!);
        var result = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Load("xl/workbook.xml").Root!.Element(Main + "sheets")!.Elements(Main + "sheet"))
        {
            var id = (string?)sheet.Attribute(Rel + "id");
            if (id is null || !rels.TryGetValue(id, out var target)) continue;
            var entry = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (zip.GetEntry(entry) is null) continue;
            result[(string)sheet.Attribute("name")!] = ReadSheet(Load(entry), shared);
        }
        return result;
    }

    private static List<string[]> ReadSheet(XDocument doc, List<string> shared)
    {
        var rows = new List<string[]>();
        var data = doc.Root!.Element(Main + "sheetData");
        if (data is null) return rows;
        foreach (var row in data.Elements(Main + "row"))
        {
            var number = (int?)row.Attribute("r") ?? rows.Count + 1;
            while (rows.Count < number - 1) rows.Add([]);       // empty rows are left out of the file
            var cells = new List<string>();
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = (string?)c.Attribute("r") is { } r ? ColumnIndex(r) : cells.Count;
                while (cells.Count < col) cells.Add("");
                cells.Add(CellText(c, shared));
            }
            rows.Add([.. cells]);
        }
        return rows;
    }

    private static string CellText(XElement c, List<string> shared)
    {
        var type = (string?)c.Attribute("t");
        var v = c.Element(Main + "v")?.Value;
        return type switch
        {
            "s" => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < shared.Count ? shared[i] : "",
            "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(t => t.Value)),
            "str" or "e" => v ?? "",
            "b" => v == "1" ? "TRUE" : "FALSE",
            _ => PlainNumber(v ?? ""),
        };
    }

    /// <summary>Excel keeps 0.015 as 1.4999999999999999E-2: read it back as the number a person typed.</summary>
    internal static string PlainNumber(string v)
    {
        if (v.Length == 0) return "";
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || double.IsNaN(d) || double.IsInfinity(d)) return v;
        if (Math.Abs(d) >= 1e15) return v;
        var rounded = Math.Round((decimal)d, 10);
        return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    /// <summary>"C12" → 2 (zero-based).</summary>
    internal static int ColumnIndex(string cellRef)
    {
        int n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(0, n - 1);
    }

    /// <summary>A date cell as yyyy-MM-dd: Excel's day number if it is one, else the text as typed.</summary>
    public static string DateText(string cell)
    {
        if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 2958466)
            return DateOnly.FromDayNumber(new DateOnly(1899, 12, 30).DayNumber + (int)Math.Floor(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return cell;
    }
}
