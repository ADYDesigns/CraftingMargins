// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CraftingMargins.Core;

/// <summary>
/// Money helpers.  All money in the app is <see cref="decimal"/>, never double.
/// </summary>
public static partial class Money
{
    [GeneratedRegex(@"^(\d{1,3}(,\d{3})+|\d+)?(\.\d*)?$")]
    private static partial Regex UsNumber();

    /// <summary>
    /// Rounds one fee line to the cent, halves away from zero (0.585 -> 0.59),
    /// which is how Etsy rounds.  .NET's default Math.Round rounds halves to even
    /// (0.585 -> 0.58), so every fee calculation must go through this method.
    /// </summary>
    public static decimal RoundFee(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Rounds an estimate or total to the cent for display (same rule as fees).</summary>
    public static decimal Round(decimal value) => RoundFee(value);

    /// <summary>
    /// Parses Etsy money text in US-dollar form: "-$1.04", "$-1.04", "($1.04)", "$25.19",
    /// "US$25.19", "25.19 USD", "1,234.50", "--" (zero), "" (zero).  The minus sign may come
    /// before or after the currency sign.  Other currencies and decimal commas are refused
    /// rather than misread.
    /// </summary>
    public static decimal Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var t = text.Trim();
        if (t == "--" || t == "-") return 0m;
        var negative = false;
        if (t.StartsWith('(') && t.EndsWith(')')) { negative = true; t = t[1..^1]; }
        t = t.Replace("USD", "", StringComparison.OrdinalIgnoreCase).Replace("US$", "", StringComparison.OrdinalIgnoreCase)
             .Replace("$", "").Replace(" ", "").Replace(" ", "").Replace('−', '-');
        if (t.StartsWith('-')) { negative = true; t = t[1..]; }
        else if (t.StartsWith('+')) t = t[1..];
        // A comma is only ever a thousands separator ("1,234.50").  "1,04" or "1.234,50" is a
        // decimal comma from another locale, and dropping it would turn 1.04 into 104.
        if (!UsNumber().IsMatch(t) ||
            !decimal.TryParse(t.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a US-dollar amount: \"{text}\" (this version reads Etsy exports in US dollars only)");
        return negative ? -v : v;
    }

    /// <summary>
    /// Formats for display: $1,234.56 / -$1.04.  The digits follow the computer's regional
    /// settings ($1.234,56 on a German PC), like every other number on screen; the "$" is fixed
    /// because the data is in US dollars.  Never use this for anything stored or exported.
    /// </summary>
    public static string Format(decimal value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("#,0.00", CultureInfo.CurrentCulture);
}

/// <summary>Text clean-up for values read from Etsy exports.</summary>
public static partial class EtsyText
{
    /// <summary>Decodes HTML entities (&amp;quot; &amp;amp; &amp;#39;) and trims.</summary>
    public static string Clean(string? text) =>
        WebUtility.HtmlDecode(text ?? "").Trim();

    /// <summary>First whole number in a text value ("2 Sets (0-9)" -> 2), or null.</summary>
    public static int? FirstInteger(string? text)
    {
        if (text is null) return null;
        var m = FirstIntRegex().Match(text);
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstIntRegex();
}
