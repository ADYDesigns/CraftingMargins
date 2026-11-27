// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>The made-up demo shop must be internally consistent and show every kind of Needs attention item.</summary>
public sealed class DemoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today = new(2026, 11, 19);

    public DemoTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private (Database Db, ShopData Data, AppSettings Settings) Build(string name = "demo.db", DateOnly? today = null)
    {
        var db = new Database(Path.Combine(_dir, name));
        var settings = new AppSettings();
        DemoShop.Build(db, settings, today ?? Today);
        return (db, db.LoadShopData(settings, []), settings);
    }

    [Fact]
    public void Every_month_passes_the_self_test_and_every_line_has_a_category()
    {
        var (_, data, _) = Build();
        var months = PnLBuilder.Build(data);
        Assert.Equal(12 + Today.Month, months.Count);                  // January last year to now
        Assert.All(months, m => Assert.True(m.SelfTestPassed, m.Month + ": " + string.Join("; ", m.Issues.Where(i => i.IsError).Select(i => i.Message))));
        Assert.DoesNotContain(data.Statement, s => data.Categorizer.Categorize(s) == Category.Unknown);
        Assert.All(months, m => Assert.DoesNotContain(m.Issues, i => i.Message.Contains("processing fee")));
    }

    [Fact]
    public void Looks_like_a_small_shop()
    {
        var (_, data, _) = Build();
        var etsy = data.Orders.Where(o => !o.IsOther).ToList();
        Assert.InRange(etsy.Count, 400, 1100);
        var orders = OrderReport.Build(data).Where(o => o.OnStatement && !o.IsOther).ToList();
        Assert.InRange(orders.Count(o => o.AdAttributed) / (double)orders.Count, 0.38, 0.62);
        Assert.InRange(data.Items.Count(i => i.Quantity == 1) / (double)data.Items.Count, 0.7, 0.92);
        Assert.Contains(data.Statement, s => s.Type == "Refund");
        Assert.Contains(data.Statement, s => s.Title.StartsWith("Adjustment for USPS"));
        Assert.Contains(data.Items, i => i.CouponCode == "SAVE10");
        Assert.Contains(data.Orders, o => o.IsOther && o.OrderType == "Spring craft fair");
        Assert.Equal(DemoShop.ShopName, Build("b.db").Settings.ShopName);
    }

    [Fact]
    public void Sales_are_steady_with_a_small_christmas_bump()
    {
        var (_, data, _) = Build();
        var byMonth = data.Orders.Where(o => !o.IsOther).GroupBy(o => o.SaleDate.ToString("yyyy-MM")).ToDictionary(g => g.Key, g => g.Count());
        var dec = byMonth["2025-12"];
        var typical = byMonth.Where(kv => kv.Key is not "2025-11" and not "2025-12" and not "2026-11").Average(kv => kv.Value);
        Assert.InRange(dec / typical, 1.0, 2.0);                        // a bump, not a seasonal spike
        Assert.True(byMonth.Values.Min() >= typical * 0.4);             // no dead months
    }

    [Fact]
    public void Has_one_of_each_needs_attention_kind()
    {
        var (_, data, _) = Build();
        var costs = data.Items.Select(i => (i, c: data.Costs.CostOf(i.ListingId, i.Variations, i.Quantity, i.SaleDate))).ToList();
        Assert.Contains(costs, x => !x.c.Mapped && x.i.ItemName == "Dice Tower");
        Assert.Contains(costs, x => x.c.Mapped && x.c.Total == 0 && x.i.ItemName == "Custom Name Keychain");
        Assert.Contains(data.Costs.Parts, p => p.Name == "Pen cup" && p.Versions[0].MachineMinutes == 0);
        Assert.Contains("Name on keychain", Variations.SuspectFreeText(data.Items, Build("c.db").Db.LoadListings(), []));
        // Every other sale has a real cost.
        Assert.All(costs.Where(x => x.i.ItemName is not "Dice Tower" and not "Custom Name Keychain"), x => Assert.True(x.c.Mapped && x.c.Total > 0, x.i.ItemName));
    }

    [Fact]
    public void Same_day_same_shop_and_dates_follow_today()
    {
        var a = Build("a.db").Data;
        var b = Build("b.db").Data;
        Assert.Equal(a.Orders.Count, b.Orders.Count);
        Assert.Equal(a.Statement.Sum(s => s.Net), b.Statement.Sum(s => s.Net));
        var later = Build("later.db", new DateOnly(2027, 3, 2)).Data;
        Assert.InRange(later.Orders.Max(o => o.SaleDate), new DateOnly(2027, 2, 20), new DateOnly(2027, 3, 2));
        Assert.True(later.Statement.Max(s => s.Date) <= new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void Pricing_check_covers_every_option_from_listing_data()
    {
        var (db, data, _) = Build();
        var rows = PricingCheck.Build(data, db.LoadListings(), OrderReport.Build(data), new PricingOptions { Today = Today });
        Assert.Equal(21, rows.Sum(r => r.Combinations));                // 15 listings, 21 options...
        Assert.Equal(18, rows.Count);                                   // ...options that earn the same are one row
        Assert.All(rows, r => Assert.Equal("Etsy", r.PriceSource));
    }
}
