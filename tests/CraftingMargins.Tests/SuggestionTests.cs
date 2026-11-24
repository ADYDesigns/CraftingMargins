// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>The Home suggestions: each rule fires only when the numbers show it, and says something specific.</summary>
public sealed class SuggestionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 11, 19);

    public SuggestionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private (ShopData Data, List<OrderProfit> Orders, List<PriceRow> Pricing) Demo()
    {
        var db = new Database(Path.Combine(_dir, "demo.db"));
        var settings = new AppSettings();
        DemoShop.Build(db, settings, Today);
        var data = db.LoadShopData(settings, []);
        var orders = OrderReport.Build(data);
        var pricing = PricingCheck.Build(data, db.LoadListings(), orders, new PricingOptions { Floor = 5m, Today = Today });
        return (data, orders, pricing);
    }

    [Fact]
    public void Demo_shop_gets_all_five_and_no_more()
    {
        var (data, orders, pricing) = Demo();
        var all = Suggestions.Build(data, orders, pricing, 5m, Today);
        Assert.Equal(["below-goal", "free-shipping", "offsite-ads", "making-time", "discounts"], all.Select(s => s.Key));
        Assert.All(all, s => { Assert.NotEmpty(s.Title); Assert.NotEmpty(s.Detail); Assert.NotEmpty(s.Fingerprint); });
        // Same numbers, same fingerprints (so a hidden suggestion stays hidden).
        Assert.Equal(all.Select(s => s.Fingerprint), Suggestions.Build(data, orders, pricing, 5m, Today).Select(s => s.Fingerprint));
    }

    [Fact]
    public void Below_goal_names_the_price_that_fixes_it()
    {
        var (_, _, pricing) = Demo();
        var s = Suggestions.BelowGoal(pricing, 5m)!;
        var worst = pricing.Where(r => r.BelowFloor).OrderBy(r => r.WorstCase).First();
        Assert.Contains(Money.Format(worst.MinPrice!.Value), s.Detail);
        Assert.Null(Suggestions.BelowGoal(pricing, 0m - 100m));      // a goal everything clears: nothing to say
    }

    [Fact]
    public void Free_shipping_only_for_free_shipping_listings_with_measured_labels()
    {
        var (data, _, pricing) = Demo();
        var s = Suggestions.FreeShipping(data, pricing, Today)!;
        var ids = s.Fingerprint.Split('|');
        Assert.All(ids, id => Assert.True(pricing.Where(r => r.ListingId == id).All(r => r.Shipping == 0)));
        Assert.Contains("Cable Management Clips", s.Detail + s.Title);
        Assert.Null(Suggestions.FreeShipping(data, [.. pricing.Where(r => r.Shipping > 0)], Today));
    }

    [Fact]
    public void Offsite_ads_states_the_facts_and_makes_no_claim_about_opting_out()
    {
        var (data, orders, _) = Demo();
        var s = Suggestions.OffsiteAds(data, orders, Today)!;
        Assert.StartsWith("Offsite Ads cost you $", s.Title);
        // Whether a shop may opt out depends on Etsy's own sales record, which imported data can't show.
        Assert.DoesNotContain("$10,000", s.Detail);
        Assert.DoesNotContain("you can choose to opt out", s.Detail);
        Assert.Contains("organic orders", s.Detail);
        Assert.Null(Suggestions.OffsiteAds(data, [], Today));          // no ad orders: nothing to say
    }

    [Fact]
    public void Making_time_needs_three_timed_listings_and_mentions_missing_times()
    {
        var (_, _, pricing) = Demo();
        var s = Suggestions.MakingTime(pricing)!;
        Assert.Contains("per hour of making time", s.Detail);
        Assert.Contains("no making time", s.Detail);                    // the demo's pen cup
        Assert.Null(Suggestions.MakingTime([.. pricing.Where(r => r.OrganicPerHour is not null).GroupBy(r => r.ListingId).Take(2).SelectMany(g => g)]));
    }

    [Theory]
    [InlineData("Ceramic Plant Pot Sampler – Try Every Size Before You Buy", "Ceramic Plant Pot Sampler")]
    [InlineData("Dice Tray - Replacement Felt or Lid Insert for Large Wooden Box", "Dice Tray - Replacement Felt or Lid Insert…")]
    [InlineData("Pen Cup", "Pen Cup")]
    [InlineData("Headphone Stand for Desks - Fits Most Headsets, Walnut or Oak", "Headphone Stand for Desks")]
    public void Long_etsy_titles_are_shortened(string title, string expected) => Assert.Equal(expected, Suggestions.Short(title));

    [Fact]
    public void Discounts_compare_this_years_orders()
    {
        var (_, orders, _) = Demo();
        var s = Suggestions.Discounts(orders, 5m, Today)!;
        Assert.StartsWith("Discounted orders made $", s.Title);
        Assert.Contains("under your $5.00 goal", s.Detail);
        Assert.Null(Suggestions.Discounts(orders, 5m, new DateOnly(2030, 1, 1)));   // no orders that year
    }
}
