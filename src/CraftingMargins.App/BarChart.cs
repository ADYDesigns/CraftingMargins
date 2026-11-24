// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CraftingMargins.App;

// Small charts drawn directly (no chart library).  Colours come from the theme, so they follow
// light/dark and blue/teal; the money-split colours are fixed and chosen to read in both modes.

/// <summary>One bar: a label and a money value (negative bars go the other way, in red).</summary>
public sealed record ChartBar(string Label, decimal Value, string Tip = "");

/// <summary>One slice of the money donut.  Value is a positive amount; Color is a hex colour.</summary>
public sealed record ChartSlice(string Label, decimal Value, string Color)
{
    public IBrush Brush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
}

/// <summary>Shared drawing helpers.</summary>
internal static class ChartDraw
{
    public static readonly Typeface Face = new(new FontFamily("fonts:Inter#Inter, $Default"));

    public static double NiceStep(double raw)
    {
        if (raw <= 0) return 100;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag;
    }

    /// <summary>Whole dollars, or cents when under $10 (so -$0.40 doesn't show as -$0).</summary>
    public static string Label(decimal v) =>
        Math.Abs(v) < 10 ? Core.Money.Format(v) : Money((double)Math.Round(v, 0, MidpointRounding.AwayFromZero));

    public static string Money(double v) =>
        (v < 0 ? "-$" : "$") + Math.Abs(v).ToString(Math.Abs(v) >= 1000 ? "#,0" : "0", CultureInfo.CurrentCulture);

    public static FormattedText Text(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Face.FontFamily, FontStyle.Normal, weight), size, brush);

    public static void Draw(DrawingContext ctx, string text, double size, IBrush brush, Point at, bool center = false, bool alignRight = false, FontWeight weight = FontWeight.Normal)
    {
        var ft = Text(text, size, brush, weight);
        var x = center ? at.X - ft.Width / 2 : alignRight ? at.X - ft.Width : at.X;
        ctx.DrawText(ft, new Point(x, at.Y));
    }
}

/// <summary>
/// Vertical money bars: one bar per item, a zero line, gridlines with dollar labels and the
/// value above each bar.  With <see cref="Compare"/> set (same count), each slot shows the
/// comparison bar (lighter) beside the main one, e.g. last year next to this year.
/// </summary>
public sealed class BarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> BarsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Bars));
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> CompareProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<ChartBar>?>(nameof(Compare));
    public static readonly StyledProperty<string> BarsLabelProperty = AvaloniaProperty.Register<BarChart, string>(nameof(BarsLabel), "");
    public static readonly StyledProperty<string> CompareLabelProperty = AvaloniaProperty.Register<BarChart, string>(nameof(CompareLabel), "");

    public IReadOnlyList<ChartBar>? Bars { get => GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    public IReadOnlyList<ChartBar>? Compare { get => GetValue(CompareProperty); set => SetValue(CompareProperty, value); }
    public string BarsLabel { get => GetValue(BarsLabelProperty); set => SetValue(BarsLabelProperty, value); }
    public string CompareLabel { get => GetValue(CompareLabelProperty); set => SetValue(CompareLabelProperty, value); }

    static BarChart() => AffectsRender<BarChart>(BarsProperty, CompareProperty, BarsLabelProperty, CompareLabelProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        IBrush C(string key) => AppTheme.Brush(key, ActualThemeVariant);
        var positive = C("BrandBrush");
        var negative = C("BadBrush");
        var text = C("TextBrush");
        var muted = C("MutedBrush");
        var grid = new Pen(C("LineBrush"), 1);
        var axis = new Pen(muted, 1);
        var faded = new SolidColorBrush(((ISolidColorBrush)positive).Color, 0.35);

        var bars = Bars;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (bars is null || bars.Count == 0 || w < 80 || h < 60)
        {
            if (w > 0 && h > 0) ChartDraw.Draw(ctx, "No months to show yet", 13, muted, new Point(w / 2, h / 2), center: true);
            return;
        }
        var compare = Compare is { } cmp && cmp.Count == bars.Count ? cmp : null;

        const double left = 64, right = 8, bottom = 26;
        var top = compare is null ? 22.0 : 30.0;
        var plotW = w - left - right;
        var plotH = h - top - bottom;
        var all = compare is null ? bars : bars.Concat(compare).ToList();
        var max = (double)Math.Max(0, all.Max(b => b.Value));
        var min = (double)Math.Min(0, all.Min(b => b.Value));
        var step = ChartDraw.NiceStep((max - min) / 4);
        max = Math.Ceiling(max / step) * step;
        min = Math.Floor(min / step) * step;
        if (max - min < step) max = min + step;
        double Y(double v) => top + (max - v) / (max - min) * plotH;

        for (var v = min; v <= max + step / 2; v += step)
        {
            var y = Y(v);
            ctx.DrawLine(Math.Abs(v) < step / 2 ? axis : grid, new Point(left, y), new Point(w - right, y));
            ChartDraw.Draw(ctx, ChartDraw.Money(v), 11, muted, new Point(left - 6, y - 7), alignRight: true);
        }

        // Legend for a paired chart, top right.
        if (compare is not null)
        {
            var x = w - right;
            foreach (var (label, brush) in new[] { (BarsLabel, positive), (CompareLabel, (IBrush)faded) })
            {
                var ft = ChartDraw.Text(label, 11, muted);
                x -= ft.Width;
                ctx.DrawText(ft, new Point(x, 2));
                x -= 16;
                ctx.DrawRectangle(brush, null, new Rect(x, 4, 11, 11), 2, 2);
                x -= 14;
            }
        }

        var slot = plotW / bars.Count;
        var zero = Y(0);
        void Bar(double x, double width, decimal value, IBrush fill, bool label)
        {
            var y = Y((double)value);
            var rect = new Rect(x, Math.Min(y, zero), width, Math.Max(1, Math.Abs(zero - y)));
            ctx.DrawRectangle(value < 0 ? negative : fill, null, rect, 3, 3);
            if (label && slot >= 44) ChartDraw.Draw(ctx, ChartDraw.Label(value), 11, text, new Point(x + width / 2, value < 0 ? rect.Bottom + 2 : rect.Top - 16), center: true);
        }
        for (int i = 0; i < bars.Count; i++)
        {
            if (compare is null)
            {
                var barW = Math.Min(48, slot * 0.62);
                Bar(left + slot * i + (slot - barW) / 2, barW, bars[i].Value, positive, true);
            }
            else
            {
                var barW = Math.Min(26, slot * 0.36);
                var x0 = left + slot * i + (slot - barW * 2 - 3) / 2;
                Bar(x0, barW, compare[i].Value, faded, false);
                Bar(x0 + barW + 3, barW, bars[i].Value, positive, false);
            }
            ChartDraw.Draw(ctx, bars[i].Label, 11, muted, new Point(left + slot * i + slot / 2, h - bottom + 6), center: true);
        }
    }

    /// <summary>Hovering over a bar shows its exact amount (narrow bars have no room for the number above them).</summary>
    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        string? tip = null;
        if (Bars is { Count: > 0 } bars && Bounds.Width >= 80)
        {
            const double left = 64, right = 8;   // the same margins Render uses
            var slot = (Bounds.Width - left - right) / bars.Count;
            var i = slot > 0 ? (int)Math.Floor((e.GetPosition(this).X - left) / slot) : -1;
            if (i >= 0 && i < bars.Count)
            {
                var compare = Compare is { } cmp && cmp.Count == bars.Count ? cmp : null;
                tip = bars[i].Tip.Length > 0 ? bars[i].Tip
                    : compare is null ? $"{bars[i].Label}: {Core.Money.Format(bars[i].Value)}"
                    : $"{bars[i].Label}\n{BarsLabel}: {Core.Money.Format(bars[i].Value)}\n{CompareLabel}: {Core.Money.Format(compare[i].Value)}";
            }
        }
        if (!Equals(ToolTip.GetTip(this), tip)) ToolTip.SetTip(this, tip);
    }
}

/// <summary>Horizontal money bars, one row per item (e.g. profit by product), label on the left.</summary>
public sealed class HBarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartBar>?> BarsProperty =
        AvaloniaProperty.Register<HBarChart, IReadOnlyList<ChartBar>?>(nameof(Bars));

    public IReadOnlyList<ChartBar>? Bars { get => GetValue(BarsProperty); set => SetValue(BarsProperty, value); }

    static HBarChart() => AffectsRender<HBarChart>(BarsProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        IBrush C(string key) => AppTheme.Brush(key, ActualThemeVariant);
        var positive = C("BrandBrush");
        var negative = C("BadBrush");
        var text = C("TextBrush");
        var muted = C("MutedBrush");

        var bars = Bars;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (bars is null || bars.Count == 0 || w < 200)
        {
            if (w > 0 && h > 0) ChartDraw.Draw(ctx, "Nothing sold in this period", 13, muted, new Point(w / 2, h / 2 - 8), center: true);
            return;
        }

        var labelW = Math.Min(280, w * 0.34);
        const double valueW = 70;
        var plotX = labelW + 12;
        var plotW = w - plotX - valueW;
        var row = h / bars.Count;
        var barH = Math.Min(18, row * 0.66);
        var max = (double)Math.Max(0.01m, bars.Max(b => b.Value));
        var min = (double)Math.Min(0, bars.Min(b => b.Value));
        double X(double v) => plotX + (v - min) / (max - min) * plotW;
        var zero = X(0);

        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var cy = row * i + row / 2;
            // Label, shortened to fit.
            var name = b.Label;
            var ft = ChartDraw.Text(name, 12, text);
            while (ft.Width > labelW && name.Length > 4) { name = name[..^2]; ft = ChartDraw.Text(name.TrimEnd() + "…", 12, text); }
            ctx.DrawText(ft, new Point(labelW - ft.Width, cy - ft.Height / 2));
            var x = X((double)b.Value);
            var rect = new Rect(Math.Min(x, zero), cy - barH / 2, Math.Max(1, Math.Abs(x - zero)), barH);
            ctx.DrawRectangle(b.Value < 0 ? negative : positive, null, rect, 3, 3);
            var v = ChartDraw.Text(ChartDraw.Label(b.Value), 11, b.Value < 0 ? negative : muted, FontWeight.SemiBold);
            ctx.DrawText(v, new Point(Math.Max(rect.Right, zero) + 6, cy - v.Height / 2));
        }
    }
}

/// <summary>
/// A donut of where the money went (Etsy fees, shipping, making costs, what you kept), with a
/// big number in the middle.  Slices are positive amounts; the legend is laid out in XAML.
/// </summary>
public sealed class DonutChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<ChartSlice>?>(nameof(Slices));
    public static readonly StyledProperty<string> CenterTextProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterText), "");
    public static readonly StyledProperty<string> CenterSubTextProperty = AvaloniaProperty.Register<DonutChart, string>(nameof(CenterSubText), "");
    public static readonly StyledProperty<bool> CenterBadProperty = AvaloniaProperty.Register<DonutChart, bool>(nameof(CenterBad));

    public IReadOnlyList<ChartSlice>? Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    public string CenterText { get => GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }
    public string CenterSubText { get => GetValue(CenterSubTextProperty); set => SetValue(CenterSubTextProperty, value); }
    public bool CenterBad { get => GetValue(CenterBadProperty); set => SetValue(CenterBadProperty, value); }

    static DonutChart() => AffectsRender<DonutChart>(SlicesProperty, CenterTextProperty, CenterSubTextProperty, CenterBadProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        IBrush C(string key) => AppTheme.Brush(key, ActualThemeVariant);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 40) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outer = size / 2 - 2;
        var thickness = Math.Max(14, outer * 0.28);
        var radius = outer - thickness / 2;

        var slices = Slices?.Where(s => s.Value > 0).ToList() ?? [];
        var total = slices.Sum(s => s.Value);
        if (total <= 0)
        {
            ctx.DrawEllipse(null, new Pen(C("LineBrush"), thickness), center, radius, radius);
        }
        else
        {
            var angle = -Math.PI / 2;
            foreach (var s in slices)
            {
                var sweep = (double)(s.Value / total) * Math.PI * 2;
                var pen = new Pen(s.Brush, thickness, lineCap: PenLineCap.Flat);
                if (sweep >= Math.PI * 2 - 0.0001) ctx.DrawEllipse(null, pen, center, radius, radius);
                else
                {
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), false);
                        var end = angle + sweep;
                        g.ArcTo(new Point(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)),
                            new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise);
                        g.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, pen, geo);
                }
                angle += sweep;
            }
        }

        var big = ChartDraw.Text(CenterText, Math.Max(14, size * 0.13), CenterBad ? C("BadBrush") : C("TextBrush"), FontWeight.SemiBold);
        var small = ChartDraw.Text(CenterSubText, Math.Max(10, size * 0.06), C("MutedBrush"));
        var blockH = big.Height + small.Height;
        ctx.DrawText(big, new Point(center.X - big.Width / 2, center.Y - blockH / 2));
        ctx.DrawText(small, new Point(center.X - small.Width / 2, center.Y - blockH / 2 + big.Height));
    }
}
