// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using CraftingMargins.Data;

namespace CraftingMargins.App;

/// <summary>
/// The app's colours.  Every screen uses these named brushes (DynamicResource), so switching
/// light/dark or the accent colour repaints everything.  Neither accent looks like Etsy's orange.
/// </summary>
public static class AppTheme
{
    /// <summary>One full set of colours.</summary>
    private sealed record Palette(
        string Brand, string BrandHover, string BrandText, string BrandLight, string HeaderBar, string Chip, string ChipText,
        string Page, string Surface, string Line, string Text, string Muted,
        string GridLine, string Header, string AltRow, string Select, string Hover, string SelectHover,
        string Good, string Bad, string GoodPale, string BadPale);

    // Teal (the original look) and blue: same structure, different hue.
    private static readonly Palette TealLight = new(
        "#1F5F5B", "#174A47", "#1F5F5B", "#E6F0EF", "#1F5F5B", "#2E726D", "#E6F0EF",
        "#F3F6F6", "#FFFFFF", "#D9E2E1", "#1B2524", "#5F6B6A",
        "#EDF2F1", "#F6F9F9", "#FAFCFC", "#D3E7E5", "#EAF3F2", "#C7E0DE",
        "#2E7D32", "#C62828", "#E3F1E4", "#FBE4E4");

    private static readonly Palette BlueLight = new(
        "#1F4E79", "#173C5E", "#1F4E79", "#E6EEF6", "#1F4E79", "#2E6194", "#E6EEF6",
        "#F3F5F8", "#FFFFFF", "#D9E0E8", "#1B2230", "#5F6873",
        "#EDF1F6", "#F6F8FB", "#FAFBFD", "#D3E1EF", "#EAF0F7", "#C7D7EA",
        "#2E7D32", "#C62828", "#E3F1E4", "#FBE4E4");

    private static readonly Palette TealDark = new(
        "#2F7D76", "#3A9089", "#6CC3BA", "#1A2E2C", "#163533", "#24504C", "#D5ECE9",
        "#111617", "#1A2123", "#2D3739", "#E3EAE9", "#9AA8A6",
        "#252E30", "#20292B", "#1D2527", "#23423F", "#1F3331", "#2A4E4A",
        "#66BB6A", "#EF6B6B", "#1E3A22", "#45201F");

    private static readonly Palette BlueDark = new(
        "#2E6DA8", "#3A7DBB", "#7DB3E8", "#18263A", "#14273D", "#22466C", "#D6E5F5",
        "#111419", "#1A1F26", "#2D3540", "#E3E8EF", "#9AA4B2",
        "#252C35", "#20262F", "#1D232B", "#233A55", "#1F2C3D", "#2A4566",
        "#66BB6A", "#EF6B6B", "#1E3A22", "#45201F");

    public static void Apply(Application app, ThemeMode mode, AccentColor accent)
    {
        var (light, dark) = accent == AccentColor.Blue ? (BlueLight, BlueDark) : (TealLight, TealDark);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Build(light);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Build(dark);

        // Selections, check boxes and focus rings use the accent instead of Windows blue.
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = Color.Parse(light.Brand), ErrorText = Color.Parse(light.Bad) };
            fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = Color.Parse(dark.Brand), ErrorText = Color.Parse(dark.Bad) };
        }
        app.RequestedThemeVariant = mode switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,   // follow Windows
        };
        _colors[ThemeVariant.Light] = Colors(light);
        _colors[ThemeVariant.Dark] = Colors(dark);
        UpdateLiveBrushes(app);
    }

    // Brushes handed out to converters (good/bad, red for losses).  A binding doesn't re-run when
    // only the theme changes, so these objects stay the same and just change colour.
    private static readonly Dictionary<string, SolidColorBrush> Live = [];
    private static readonly Dictionary<ThemeVariant, Dictionary<string, Color>> _colors = [];

    /// <summary>Call when the theme showing changes (Windows switched light/dark, or the user picked one).</summary>
    /// <summary>
    /// Settings → Appearance: show or hide the outline around the table cell that has keyboard
    /// focus.  A style in App.axaml sets that outline's visibility from the app resource
    /// "TableCellFocusVisible"; changing the resource changes every table at once, open or not.
    /// </summary>
    public static void ShowTableFocusOutline(Application app, bool show) => app.Resources["TableCellFocusVisible"] = show;

    public static void UpdateLiveBrushes(Application app)
    {
        var map = ColorsFor(app.ActualThemeVariant);
        foreach (var (key, brush) in Live)
            if (map.TryGetValue(key, out var c)) brush.Color = c;
    }

    private static Dictionary<string, Color> ColorsFor(ThemeVariant v) =>
        _colors.TryGetValue(v == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light, out var m) ? m : Colors(TealLight);

    /// <summary>A brush that always shows the current theme's colour for <paramref name="key"/>.</summary>
    public static IBrush LiveBrush(string key)
    {
        if (!Live.TryGetValue(key, out var b))
        {
            var app = Application.Current;
            var map = ColorsFor(app?.ActualThemeVariant ?? ThemeVariant.Light);
            Live[key] = b = new SolidColorBrush(map.TryGetValue(key, out var c) ? c : Avalonia.Media.Colors.Gray);
        }
        return b;
    }

    private static Dictionary<string, Color> Colors(Palette p) =>
        Build(p).Where(kv => kv.Value is SolidColorBrush).ToDictionary(kv => (string)kv.Key, kv => ((SolidColorBrush)kv.Value!).Color);

    private static ResourceDictionary Build(Palette p)
    {
        var d = new ResourceDictionary();
        void B(string key, string color) => d[key] = new SolidColorBrush(Color.Parse(color));
        B("BrandBrush", p.Brand);
        B("BrandDarkBrush", p.BrandHover);
        B("BrandTextBrush", p.BrandText);
        B("BrandLightBrush", p.BrandLight);
        B("HeaderBarBrush", p.HeaderBar);
        B("ChipBrush", p.Chip);
        B("ChipTextBrush", p.ChipText);
        B("OnBrandBrush", "#FFFFFF");
        B("PageBrush", p.Page);
        B("SurfaceBrush", p.Surface);
        B("LineBrush", p.Line);
        B("TextBrush", p.Text);
        B("MutedBrush", p.Muted);
        B("GridLineBrush", p.GridLine);
        B("HeaderBrush", p.Header);
        B("AltRowBrush", p.AltRow);
        B("SelectBrush", p.Select);
        B("HoverBrush", p.Hover);
        B("SelectHoverBrush", p.SelectHover);
        B("GoodBrush", p.Good);
        B("BadBrush", p.Bad);
        B("GoodPaleBrush", p.GoodPale);
        B("BadPaleBrush", p.BadPale);
        // Fluent's own tab header keys.
        B("TabItemHeaderForegroundSelected", p.BrandText);
        B("TabItemHeaderForegroundSelectedPointerOver", p.BrandText);
        B("TabItemHeaderSelectedPipeFill", p.BrandText);
        return d;
    }

    /// <summary>A named brush for the theme currently showing (for code that draws or converts).</summary>
    public static IBrush Brush(string key, ThemeVariant? variant = null)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, variant ?? app.ActualThemeVariant, out var v) && v is IBrush b) return b;
        return Brushes.Gray;
    }
}
