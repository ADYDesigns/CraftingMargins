// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CraftingMargins.App.ViewModels;

/// <summary>Colours come from the current theme (AppTheme), so they suit light and dark mode.</summary>
public static class Converters
{
    /// <summary>true → green, false → red.</summary>
    public static readonly IValueConverter GoodBad = new FuncValueConverter<bool, IBrush>(ok =>
        AppTheme.LiveBrush(ok ? "GoodBrush" : "BadBrush"));

    /// <summary>Pale pill background: true → pale green, false → pale red.</summary>
    public static readonly IValueConverter GoodBadPale = new FuncValueConverter<bool, IBrush>(ok =>
        AppTheme.LiveBrush(ok ? "GoodPaleBrush" : "BadPaleBrush"));

    /// <summary>Negative money → red, otherwise the normal text colour.</summary>
    public static readonly IValueConverter NegativeRed = new FuncValueConverter<decimal?, IBrush>(v =>
        AppTheme.LiveBrush(v < 0 ? "BadBrush" : "TextBrush"));

    /// <summary>true → red text (e.g. below the profit goal), false → the normal text colour.</summary>
    public static readonly IValueConverter RedIfTrue = new FuncValueConverter<bool, IBrush>(bad =>
        AppTheme.LiveBrush(bad ? "BadBrush" : "TextBrush"));

    public static readonly IValueConverter BoldIfTrue = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);
    public static readonly IValueConverter DimIfTrue = new FuncValueConverter<bool, double>(b => b ? 0.5 : 1.0);

    /// <summary>Money for busy grids: 1,234.56, or blank for zero so the numbers that matter stand out.</summary>
    public static readonly IValueConverter MoneyOrBlank = new FuncValueConverter<decimal, string>(v => v == 0 ? "" : v.ToString("N2"));

    public static readonly IValueConverter MachineTimeTip = new FuncValueConverter<bool, string>(missing =>
        missing ? "No machine time yet: enter the minutes it takes to make." : "OK");

    public static readonly IValueConverter ImportTip = new FuncValueConverter<bool, string>(demo =>
        demo ? "Leave the demo to import your own Etsy files." : "Statements, orders and listings exported from Etsy Shop Manager");

    /// <summary>
    /// A date kept as "2026-10-31" text ↔ the date a date picker shows.  Text that isn't a date
    /// shows as no date picked; clearing the picker gives "" (Save then asks for a date).
    /// </summary>
    public static readonly IValueConverter DateText = new DateTextConverter();

    private sealed class DateTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            Input.TryDate(value as string, out var d) ? d.ToDateTime(TimeOnly.MinValue) : null;

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is DateTime t ? Input.Date(DateOnly.FromDateTime(t)) : "";
    }

    /// <summary>0.25 → "25%".</summary>
    public static readonly IValueConverter Percent = new FuncValueConverter<decimal, string>(v => v.ToString("0%"));
}
