// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text;
using System.Text.RegularExpressions;

namespace CraftingMargins.Core;

/// <summary>
/// Etsy writes variations as "Size:Large (10&quot; x 7.5&quot;),Mounting Method:Desk Stand".
/// A comma only separates two pairs when it is followed by "Name:", so values that
/// contain commas ("1/4&quot;, 1/2&quot;") stay whole.
///
/// That leaves one case the text alone can't settle: a value that itself contains a comma
/// and then a colon ("Engraving:Est. 2020, time: 5pm" looks like a third option called
/// "time").  Two things deal with it:
/// <list type="bullet">
/// <item>When the listing's real option names are known (<see cref="Parse(string?, IReadOnlyCollection{string})"/>),
/// a piece whose name isn't one of them is put back onto the value before it.</item>
/// <item>The text the app stores (<see cref="Format"/>) writes a colon inside such a value as
/// "\:", so reading it back never splits there, whatever is known later.</item>
/// </list>
/// </summary>
public static partial class Variations
{
    public static List<KeyValuePair<string, string>> Parse(string? text)
    {
        var result = new List<KeyValuePair<string, string>>();
        var clean = EtsyText.Clean(text);
        if (clean.Length == 0) return result;
        foreach (var piece in PairSplit().Split(clean))
        {
            var colon = FirstPlainColon(piece);
            if (colon <= 0)
            {
                // No "Name:" part: attach to the previous value (it was a stray comma).
                if (result.Count > 0)
                {
                    var last = result[^1];
                    result[^1] = new(last.Key, last.Value + "," + Unescape(piece));
                }
                continue;
            }
            result.Add(new(Unescape(piece[..colon]).Trim(), Unescape(piece[(colon + 1)..]).Trim()));
        }
        return result;
    }

    /// <summary>
    /// Parses raw text from an Etsy file when the option names that really exist are known
    /// (the listing's own options, plus personalization names from Settings).  A piece that
    /// looks like "name: value" but whose name isn't a real option is part of the value before
    /// it.  With no names known it is the same as <see cref="Parse(string?)"/>.
    /// The first pair is always kept: the text has to start with an option.
    /// </summary>
    public static List<KeyValuePair<string, string>> Parse(string? text, IReadOnlyCollection<string> knownNames)
    {
        var pairs = Parse(text);
        if (knownNames.Count == 0 || pairs.Count < 2) return pairs;
        var known = knownNames.Select(n => EtsyText.Clean(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Only trust the list when it explains the text at all: if not even the first option is in it, the list is for some other listing or out of date.
        if (!known.Contains(pairs[0].Key)) return pairs;

        var result = new List<KeyValuePair<string, string>> { pairs[0] };
        foreach (var p in pairs.Skip(1))
        {
            if (known.Contains(p.Key)) result.Add(p);
            else
            {
                var last = result[^1];
                result[^1] = new(last.Key, $"{last.Value}, {p.Key}: {p.Value}");
            }
        }
        return result;
    }

    /// <summary>
    /// Joins pairs back into "Name:Value,Name:Value", the form the app stores.  A colon inside a
    /// value that also has a comma is written "\:" so <see cref="Parse(string?)"/> gives the
    /// same pairs back.
    /// </summary>
    public static string Format(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join(",", pairs.Select(p => $"{p.Key}:{(p.Value.Contains(',') ? p.Value.Replace(":", "\\:") : p.Value)}"));

    /// <summary>"Size:Large,Color:Red" → "Size: Large, Color: Red", for showing to the user.</summary>
    public static string Readable(string? text)
    {
        var pairs = Parse(text);
        return pairs.Count == 0 ? EtsyText.Clean(text) : string.Join(", ", pairs.Select(p => $"{p.Key}: {p.Value}"));
    }

    /// <summary>Removes variations the user marked as free text (personalization) so they are never stored.</summary>
    public static string Strip(string? text, IReadOnlyCollection<string> ignoredNames) =>
        Format(Parse(text).Where(p => !ignoredNames.Contains(p.Key, StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// Variation names that look like free text typed by the buyer (personalization), which
    /// may hold names and shouldn't be stored.  A name is suspect when it isn't a variation of
    /// any listing in the listings download and every sale has a different value, or (with no
    /// listings loaded) when nearly every sale has a different value.
    /// </summary>
    public static List<string> SuspectFreeText(IEnumerable<OrderItem> items, IEnumerable<Listing> listings, IEnumerable<string> keep)
    {
        var listingNames = listings.SelectMany(l => l.Variations.Select(v => v.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keepSet = keep.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return items
            .SelectMany(i => Parse(i.Variations))
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => !keepSet.Contains(g.Key))
            .Where(g =>
            {
                int n = g.Count(), distinct = g.Select(p => p.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                return listingNames.Count > 0
                    ? !listingNames.Contains(g.Key) && n >= 2 && distinct == n
                    : n >= 3 && distinct >= 0.8 * n;
            })
            .Select(g => g.Key)
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>Index of the first colon that isn't written "\:", or -1.</summary>
    private static int FirstPlainColon(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length && s[i + 1] == ':') { i++; continue; }
            if (s[i] == ':') return i;
        }
        return -1;
    }

    private static string Unescape(string s) => s.Contains('\\') ? s.Replace("\\:", ":") : s;

    // Split on a comma that is followed by "Some Name:" (no comma inside the name, and the colon not written "\:").
    [GeneratedRegex(@",(?=(?:[^,:\\]|\\:|\\(?!:))+:)")]
    private static partial Regex PairSplit();
}
