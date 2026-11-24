// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace CraftingMargins.Data;

/// <summary>A table to export: a header row, then rows of text, numbers (decimal) or dates.  Bold rows are totals.</summary>
public sealed class Sheet(string name)
{
    public string Name { get; } = name;
    public List<object?[]> Rows { get; } = [];
    public HashSet<int> BoldRows { get; } = [0];
    public double[] ColumnWidths { get; set; } = [];

    public Sheet Add(params object?[] cells) { Rows.Add(cells); return this; }
    public Sheet AddBold(params object?[] cells) { BoldRows.Add(Rows.Count); Rows.Add(cells); return this; }
}

/// <summary>
/// Writes .xlsx and .csv files.  The .xlsx is a minimal Office Open XML package written by
/// hand (zip + XML), so the app needs no spreadsheet library.  Numbers are real numbers
/// with a money format; dates are real dates.
/// </summary>
public static class Export
{
    public static void WriteCsv(string path, Sheet sheet)
    {
        var sb = new StringBuilder();
        foreach (var row in sheet.Rows)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM so Excel reads it as UTF-8
    }

    private static string CsvCell(object? v)
    {
        var s = v switch
        {
            null => "",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
        };
        // Quote when needed; a leading = + - @ is prefixed so a spreadsheet never runs it as a formula.
        if (v is string && s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static void WriteXlsx(string path, IReadOnlyList<Sheet> sheets)
    {
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            void Entry(string name, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            var names = UniqueNames(sheets.Select(s => s.Name));
            Entry("[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>"""
                + string.Concat(sheets.Select((_, i) => $"""<Override PartName="/xl/worksheets/sheet{i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>"""))
                + "</Types>");
            Entry("_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Entry("xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(names.Select((n, i) => $"""<sheet name="{X(n)}" sheetId="{i + 1}" r:id="rId{i + 1}"/>"""))
                + "</sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(sheets.Select((_, i) => $"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i + 1}.xml"/>"""))
                + $"""<Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            // Styles: 0 normal, 1 bold, 2 money, 3 bold money, 4 date, 5 bold date.
            Entry("xl/styles.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00;[Red]-#,##0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="6"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="164" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="14" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/></cellXfs></styleSheet>""");
            for (int i = 0; i < sheets.Count; i++)
                Entry($"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static string SheetXml(Sheet s)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        var cols = s.Rows.Count == 0 ? 0 : s.Rows.Max(r => r.Length);
        if (cols > 0)
        {
            sb.Append("<cols>");
            for (int c = 0; c < cols; c++)
            {
                var width = c < s.ColumnWidths.Length ? s.ColumnWidths[c]
                    : Math.Clamp(s.Rows.Max(r => c < r.Length ? Display(r[c]).Length : 0) + 2, 8, 60);
                sb.Append(CultureInfo.InvariantCulture, $"""<col min="{c + 1}" max="{c + 1}" width="{width}" customWidth="1"/>""");
            }
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < s.Rows.Count; r++)
        {
            var bold = s.BoldRows.Contains(r);
            sb.Append(CultureInfo.InvariantCulture, $"""<row r="{r + 1}">""");
            var row = s.Rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                var refName = ColumnName(c) + (r + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d:
                        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{refName}" s="{(bold ? 3 : 2)}"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>""");
                        break;
                    case int n:
                        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{refName}"{(bold ? " s=\"1\"" : "")}><v>{n}</v></c>""");
                        break;
                    case DateOnly d:
                        var serial = d.DayNumber - new DateOnly(1899, 12, 30).DayNumber;
                        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{refName}" s="{(bold ? 5 : 4)}"><v>{serial}</v></c>""");
                        break;
                    default:
                        sb.Append(CultureInfo.InvariantCulture, $"""<c r="{refName}" t="inlineStr"{(bold ? " s=\"1\"" : "")}><is><t xml:space="preserve">{X(Display(row[c]))}</t></is></c>""");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string Display(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("#,0.00", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    public static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\ and unique.</summary>
    private static List<string> UniqueNames(IEnumerable<string> names)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in names)
        {
            var n = new string(raw.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
            if (n.Length == 0) n = "Sheet";
            if (n.Length > 31) n = n[..31];
            var candidate = n;
            for (int i = 2; !used.Add(candidate); i++) candidate = (n.Length > 28 ? n[..28] : n) + $" {i}";
            result.Add(candidate);
        }
        return result;
    }

    /// <summary>XML-escapes text and drops characters XML can't hold.</summary>
    private static string X(string s) =>
        SecurityElement.Escape(new string(s.Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || ch >= ' ').ToArray())) ?? "";
}
