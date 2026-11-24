// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

/// <summary>Where a suggestion's "Show me" goes.</summary>
public enum SuggestionPage { Pricing, ReportsAds, ReportsDiscounts }

/// <summary>
/// One thing worth knowing, worked out from the shop's own numbers.  Never advice and never a
/// guess: each rule only fires when the data clearly shows it.  <see cref="Fingerprint"/>
/// identifies what the suggestion is about, so a hidden one comes back only when that changes.
/// </summary>
public sealed record Suggestion(string Key, string Title, string Detail, SuggestionPage Page, string Fingerprint);

public static class Suggestions
{
    public const int MaxShown = 5;

    /// <param name="pricing">The pricing check rows (today's prices and costs).</param>
    public static List<Suggestion> Build(ShopData data, IReadOnlyList<OrderProfit> orders, IReadOnlyList<PriceRow> pricing, decimal goal, DateOnly today)
    {
        var list = new List<Suggestion?>
        {
            BelowGoal(pricing, goal),
            FreeShipping(data, pricing, today),
            OffsiteAds(data, orders, today),
            MakingTime(pricing),
            Discounts(orders, goal, today),
        };
        return [.. list.OfType<Suggestion>().Take(MaxShown)];
    }

    private static string Name(PriceRow r) => r.Options.Length == 0 ? Short(r.Title) : $"{Short(r.Title)} ({r.OptionsText})";

    /// <summary>
    /// Etsy titles are long and keyword-stuffed ("Ceramic Plant Pot Sampler – Try Every Size Before You Buy").
    /// Suggestions use the part before the first dash or bar, cut to about 45 characters.
    /// </summary>
    public static string Short(string title)
    {
        var t = title;
        foreach (var sep in new[] { " – ", " — ", " - ", " | ", ", " })
        {
            var i = t.IndexOf(sep, StringComparison.Ordinal);
            if (i >= 12) t = t[..i];
        }
        if (t.Length <= 45) return t;
        var cut = t[..44];
        var space = cut.LastIndexOf(' ');
        return (space >= 25 ? cut[..space] : cut).TrimEnd(' ', ',', '-', '–') + "…";
    }

    // 1. Options that miss the profit goal on an Offsite Ads sale, and the price that fixes each.
    public static Suggestion? BelowGoal(IReadOnlyList<PriceRow> pricing, decimal goal)
    {
        var below = pricing.Where(r => r.WorstCase < goal && r.MinPrice is not null).OrderBy(r => r.WorstCase).ToList();   // rows are priced against the same goal
        if (below.Count == 0) return null;
        var lines = below.Take(3).Select(r => $"{Name(r)}: {Money.Format(r.Price)} now, {Money.Format(r.MinPrice!.Value)} would reach it.");
        var more = below.Count > 3 ? $"  {below.Count - 3} more on the pricing check." : "";
        return new("below-goal",
            below.Count == 1 ? $"1 option earns less than your {Money.Format(goal)} goal on an Offsite Ads sale"
                             : $"{below.Count} options earn less than your {Money.Format(goal)} goal on an Offsite Ads sale",
            string.Join("  ", lines) + more,
            SuggestionPage.Pricing,
            string.Join("|", below.Select(r => FormattableString.Invariant($"{r.ListingId}/{r.Options}/{r.Price}")).Order()));
    }

    // 2. Free-shipping listings whose label (measured on real orders) eats the margin.
    public static Suggestion? FreeShipping(ShopData data, IReadOnlyList<PriceRow> pricing, DateOnly today)
    {
        var fees = data.FeesAt(today);
        var cut = fees.TransactionRate + fees.ProcessingRate + fees.OffsiteAdsRate;
        var hit = pricing
            .Where(r => r.Shipping == 0 && r.Label > 0 && r.LabelSource.StartsWith("Typical label"))
            .GroupBy(r => r.ListingId)
            .Select(g => (Title: Short(g.First().Title), Label: g.Max(r => r.Label), Worst: g.Min(r => r.WorstCase ?? decimal.MaxValue), Id: g.Key))
            .Where(x => x.Label >= 1m)
            .OrderBy(x => x.Worst)
            .ToList();
        if (hit.Count == 0) return null;
        var lines = hit.Take(3).Select(x =>
            $"{x.Title}: the label typically costs {Money.Format(x.Label)}. Charging that as shipping covers it; adding it to the price takes about {Money.Format(Math.Ceiling(x.Label / (1 - cut) * 100) / 100)} because Etsy's fees take a cut of the price.");
        return new("free-shipping",
            hit.Count == 1 ? $"Free shipping on {hit[0].Title} costs you {Money.Format(hit[0].Label)} a label"
                           : $"Free shipping costs you the label on {hit.Count} listings",
            string.Join("  ", lines),
            SuggestionPage.Pricing,
            string.Join("|", hit.Select(x => x.Id).Order()));
    }

    // 4. Is Offsite Ads paying off?  Facts for this year; the decision is the seller's.
    public static Suggestion? OffsiteAds(ShopData data, IReadOnlyList<OrderProfit> orders, DateOnly today)
    {
        var year = Period.Year(today.Year);
        var t = AdReport.Build(data, orders, year)[^1];
        if (t.AdOrders < 5 || t.OffsiteAdsPaid == 0) return null;
        // Whether the shop may opt out isn't said here: that depends on Etsy's own record of the
        // shop's sales (and is permanent once $10,000 in a year is reached), which the imported
        // data can't show reliably.
        return new("offsite-ads",
            $"Offsite Ads cost you {Money.Format(-t.OffsiteAdsPaid)} in {today.Year}",
            $"{t.AdOrders} of {t.Orders} orders ({t.AdShare:0%}) came through an ad. Ad orders averaged {Money.Format(t.AdAverage ?? 0)} profit, organic orders {Money.Format(t.OrganicAverage ?? 0)}. Whether your shop can opt out is shown in Etsy's Offsite Ads settings.",
            SuggestionPage.ReportsAds,
            $"{today.Year}");
    }

    // 5. Which listings earn the most per hour of printer time.
    public static Suggestion? MakingTime(IReadOnlyList<PriceRow> pricing)
    {
        var timed = pricing.Where(r => r.OrganicPerHour is not null)
            .GroupBy(r => r.ListingId)
            .Select(g => (Title: Short(g.First().Title), PerHour: Money.Round(g.Average(r => r.OrganicPerHour!.Value)), Id: g.Key))
            .OrderByDescending(x => x.PerHour)
            .ToList();
        if (timed.Count < 3) return null;
        var missing = pricing.Where(r => r.Cost is not null && r.MakingHours == 0).Select(r => r.ListingId).Distinct().Count();
        var top = timed.Take(2).Select(x => $"{x.Title} {Money.Format(x.PerHour)}");
        var bottom = timed.TakeLast(Math.Min(2, timed.Count - 2)).Select(x => $"{x.Title} {Money.Format(x.PerHour)}");
        return new("making-time",
            $"Your making time earns the most on {timed[0].Title}",
            $"Profit per hour of making time on a normal sale. Best: {string.Join(", ", top)}. Lowest: {string.Join(", ", bottom)}." +
            (missing > 0 ? $"  {missing} listing(s) aren't included because their parts have no making time yet." : ""),
            SuggestionPage.Pricing,
            $"{timed[0].Id}|{timed[^1].Id}");
    }

    // 8. Discounted orders against full-price ones, this year.
    public static Suggestion? Discounts(IReadOnlyList<OrderProfit> orders, decimal goal, DateOnly today)
    {
        var year = orders.Where(o => o.OnStatement && !o.IsOther && o.SaleDate.Year == today.Year).ToList();
        var disc = year.Where(DiscountReport.IsDiscounted).ToList();
        var full = year.Where(o => !DiscountReport.IsDiscounted(o)).ToList();
        if (disc.Count < 3 || full.Count < 3) return null;
        var d = Money.Round(disc.Average(o => o.Profit));
        var f = Money.Round(full.Average(o => o.Profit));
        var under = disc.Count(o => o.Profit < goal);
        return new("discounts",
            $"Discounted orders made {Money.Format(d)} on average, full-price orders {Money.Format(f)}",
            $"{disc.Count} discounted order(s) in {today.Year}, {Money.Format(disc.Sum(o => o.Discount + o.ShippingDiscount))} off in total; {under} of them came in under your {Money.Format(goal)} goal.",
            SuggestionPage.ReportsDiscounts,
            $"{today.Year}");
    }
}
