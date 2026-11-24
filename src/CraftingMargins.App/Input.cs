// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;

namespace CraftingMargins.App;

/// <summary>
/// Reading what the user typed.  One place, so every box accepts the same things.
///
/// Numbers follow the computer's regional settings, the same as the numbers the app shows:
/// on a US PC 1,234.56, on a German one 1.234,56.  Money is always US dollars (Etsy's files
/// for a US shop are), so the "$" sign is fixed and may be typed or left out.
/// Dates are typed as 2026-10-31 everywhere (or picked from the calendar), which can't be
/// misread as day/month or month/day.
/// What is stored and exported never depends on the region.
/// </summary>
public static class Input
{
    /// <summary>
    /// Numbers follow the computer's region; month and day names stay in English, because every
    /// other word on screen is English and "Okt 5, 2026" in the middle of it reads as a mistake.
    /// It also keeps years on the ordinary calendar on PCs whose region counts them differently.
    /// Called once at start, before any window opens.
    /// </summary>
    public static void UseRegionForNumbersOnly()
    {
        var culture = NumbersFrom(CultureInfo.CurrentCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    /// <summary>A region's number format with English month and day names on the ordinary calendar.</summary>
    public static CultureInfo NumbersFrom(CultureInfo region)
    {
        var culture = (CultureInfo)region.Clone();
        culture.DateTimeFormat = (DateTimeFormatInfo)CultureInfo.GetCultureInfo("en-US").DateTimeFormat.Clone();
        return culture;
    }

    public static bool TryMoney(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace("US$", "", StringComparison.OrdinalIgnoreCase).Replace("USD", "", StringComparison.OrdinalIgnoreCase).Replace("$", "").Trim();
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    public static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
