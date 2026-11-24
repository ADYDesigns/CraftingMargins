// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia.Media;
using CraftingMargins.Core;

namespace CraftingMargins.App.ViewModels;

/// <summary>One line of the donut's legend: "Etsy fees  $1,234  21%".</summary>
public sealed record SplitRow(string Label, string Amount, string Share, IBrush Color);

/// <summary>
/// Where the money that came in went: Etsy and other selling fees, shipping, making costs,
/// and what was left.  Feeds the donut charts on Home and the Monthly P&amp;L.
/// </summary>
public sealed class MoneySplit
{
    // Fixed colours that read in light and dark mode and don't depend on the accent.
    public const string FeesColor = "#C0705F";
    public const string ShippingColor = "#D9A441";
    public const string CostsColor = "#7C8C9A";
    public const string KeptColor = "#3E9A5A";

    public IReadOnlyList<ChartSlice> Slices { get; init; } = [];
    public IReadOnlyList<SplitRow> Rows { get; init; } = [];
    public string Center { get; init; } = "";
    public string CenterSub { get; init; } = "";
    public bool IsLoss { get; init; }
    public string Summary { get; init; } = "";

    public static readonly MoneySplit Empty = new() { Center = "—", Summary = "Nothing to show yet." };

    /// <param name="months">The months to add up (one month, or a year).</param>
    public static MoneySplit From(IEnumerable<MonthlyPnL> months)
    {
        var ms = months.ToList();
        if (ms.Count == 0) return Empty;
        decimal S(Func<MonthlyPnL, decimal> f) => ms.Sum(f);
        var came = S(m => m.NetSales + m.Refunds + m.OtherSales);   // what buyers paid you, after tax and refunds
        var fees = -S(m => m.EtsyFees + m.OtherFees);
        var shipping = -S(m => m.Shipping);
        var costs = -S(m => m.OtherCosts);
        var kept = S(m => m.NetProfit);
        if (came <= 0) return Empty;

        var parts = new List<(string Label, decimal Value, string Color)>
        {
            ("Etsy and other fees", fees, FeesColor),
            ("Shipping", shipping, ShippingColor),
            ("Making costs", costs, CostsColor),
        };
        if (kept > 0) parts.Add(("You kept", kept, KeptColor));
        var basis = Math.Max(came, parts.Sum(p => p.Value));
        string Pct(decimal v) => $"{v / basis:0%}";

        return new MoneySplit
        {
            Slices = [.. parts.Where(p => p.Value > 0).Select(p => new ChartSlice(p.Label, p.Value, p.Color))],
            Rows = [.. parts.Select(p => new SplitRow(p.Label, Money.Format(Money.Round(p.Value)), Pct(p.Value), new SolidColorBrush(Color.Parse(p.Color))))],
            Center = kept >= 0 ? $"{kept / came:0%}" : Money.Format(Money.Round(kept)),
            CenterSub = kept >= 0 ? "kept" : "loss",
            IsLoss = kept < 0,
            Summary = kept >= 0
                ? $"Of every $10 buyers paid, you kept {Money.Format(Money.Round(kept / came * 10))}."
                : $"Costs were more than what came in: a loss of {Money.Format(Money.Round(-kept))}.",
        };
    }
}
