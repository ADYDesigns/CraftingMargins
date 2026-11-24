// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

/// <summary>
/// Etsy fee rates in effect from a date.  Every rate is a setting because fees change
/// and differ by country and shop size.  Defaults are US 2026.
/// </summary>
public sealed record FeeRates
{
    public DateOnly EffectiveFrom { get; init; } = new(2000, 1, 1);
    /// <summary>6.5% of item price + shipping (+ gift wrap).</summary>
    public decimal TransactionRate { get; init; } = 0.065m;
    /// <summary>3% of item + shipping + sales tax ...</summary>
    public decimal ProcessingRate { get; init; } = 0.03m;
    /// <summary>... plus $0.25 per order.</summary>
    public decimal ProcessingFixed { get; init; } = 0.25m;
    /// <summary>$0.20 per unit sold.</summary>
    public decimal ListingFee { get; init; } = 0.20m;
    /// <summary>15%, or 12% once the shop has had $10k in sales in 365 days.</summary>
    public decimal OffsiteAdsRate { get; init; } = 0.15m;
    /// <summary>Offsite Ads fee is capped per order.</summary>
    public decimal OffsiteAdsCap { get; init; } = 100m;
}

/// <summary>Picks the version of a dated setting in effect on a date.</summary>
public static class Effective
{
    /// <summary>Latest version whose EffectiveFrom is on or before <paramref name="date"/>; the earliest version if none.</summary>
    public static T At<T>(IEnumerable<T> versions, Func<T, DateOnly> from, DateOnly date)
    {
        var ordered = versions.OrderBy(from).ToList();
        if (ordered.Count == 0) throw new InvalidOperationException("No versions defined.");
        var pick = ordered[0];
        foreach (var v in ordered)
            if (from(v) <= date) pick = v;
        return pick;
    }

    /// <summary>
    /// The version in effect today.  A version dated in the future (a change entered ahead of
    /// time) doesn't count until its date arrives.
    /// </summary>
    public static T Latest<T>(IEnumerable<T> versions, Func<T, DateOnly> from, DateOnly? today = null) =>
        At(versions, from, today ?? DateOnly.FromDateTime(DateTime.Today));
}

/// <summary>The fee lines Etsy charges on one order, each rounded on its own.</summary>
public sealed record OrderFees(
    decimal TransactionItem,
    decimal TransactionShipping,
    decimal Processing,
    decimal OffsiteAds,
    decimal Listing)
{
    public decimal Total => TransactionItem + TransactionShipping + Processing + OffsiteAds + Listing;
}

public static class FeeCalculator
{
    /// <summary>
    /// Fees on one order.  <paramref name="itemTotal"/> is price × quantity after discounts.
    /// Each line is rounded to the cent separately, halves away from zero.
    /// </summary>
    public static OrderFees Compute(FeeRates r, decimal itemTotal, decimal shipping, decimal salesTax, int units, bool adAttributed)
    {
        var txItem = Money.RoundFee(r.TransactionRate * itemTotal);
        var txShip = Money.RoundFee(r.TransactionRate * shipping);
        var processing = Money.RoundFee(r.ProcessingRate * (itemTotal + shipping + salesTax) + r.ProcessingFixed);
        var ads = adAttributed ? Math.Min(Money.RoundFee(r.OffsiteAdsRate * (itemTotal + shipping)), r.OffsiteAdsCap) : 0m;
        var listing = Money.RoundFee(r.ListingFee * units);
        return new OrderFees(txItem, txShip, processing, ads, listing);
    }
}

/// <summary>
/// Quick pricing estimates for a 1-unit order (the worst case: the fixed $0.25 + $0.20
/// aren't shared across units).  The shipping label is assumed to equal shipping charged.
/// Leaves out the processing fee on sales tax, which depends on the buyer's location.
/// </summary>
public static class PricingEstimate
{
    // label: what the shipping label costs you.  Null = assume it equals the shipping charged.

    public static decimal Organic(FeeRates r, decimal price, decimal shipping, decimal cost, decimal? label = null) =>
        price + shipping - (label ?? shipping)
        - (r.TransactionRate + r.ProcessingRate) * (price + shipping) - r.ProcessingFixed - r.ListingFee - cost;

    public static decimal WorstCase(FeeRates r, decimal price, decimal shipping, decimal cost, decimal? label = null) =>
        Organic(r, price, shipping, cost, label) - Math.Min(r.OffsiteAdsRate * (price + shipping), r.OffsiteAdsCap);

    /// <summary>Lowest price whose worst-case profit reaches <paramref name="target"/> (ignores the ad cap, which only matters above ~$667).</summary>
    public static decimal MinPriceForTarget(FeeRates r, decimal target, decimal shipping, decimal cost, decimal? label = null)
    {
        var pct = r.TransactionRate + r.ProcessingRate + r.OffsiteAdsRate;
        // Fees of 100% or more: no price reaches the target.  Settings refuses such rates; this guards data saved before that check.
        if (pct >= 1m) return decimal.MaxValue / 1000;
        return (target + pct * shipping + (label ?? shipping) - shipping + r.ProcessingFixed + r.ListingFee + cost) / (1 - pct);
    }
}
