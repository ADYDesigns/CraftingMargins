// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>Features added after the author's first review: outside sales, year over year, 1099-K, calculator, packaging, rename.</summary>
public class OutsideSalesTests
{
    /// <summary>The sample October shop plus a craft-fair sale of 2 signs at $20, $1.50 card fee, $3 postage.</summary>
    private static ShopData WithFairSale(DateOnly? date = null)
    {
        var data = SampleShop.Data();
        var d = date ?? new DateOnly(2026, 10, 12);
        data.Orders.Add(new Order { OrderId = "M-1", Source = Order.OtherSource, OrderType = "Craft fair", SaleDate = d, OrderValue = 40m, CardProcessingFees = 1.50m, ExtraShippingCost = 3m });
        data.Items.Add(new OrderItem { TransactionId = "M-1-1", OrderId = "M-1", ListingId = "L1", ItemName = "Sign", SaleDate = d, Quantity = 2, Price = 20m, Variations = "Size:Large" });
        return data;
    }

    [Fact]
    public void Outside_sale_adds_to_profit_but_not_to_the_etsy_self_test()
    {
        var before = Assert.Single(PnLBuilder.Build(SampleShop.Data()));
        var m = Assert.Single(PnLBuilder.Build(WithFairSale()));
        Assert.True(m.SelfTestPassed, string.Join("\n", m.Issues));
        Assert.Equal(40m, m.OtherSales);
        Assert.Equal(-1.50m, m.OtherFees);
        // + $40 sale − $1.50 fee − $3 postage − 2 × $3 cost.
        Assert.Equal(before.NetProfit + 40m - 1.50m - 3m - 6m, m.NetProfit);
    }

    [Fact]
    public void Outside_sale_after_the_last_statement_gets_its_own_month()
    {
        var months = PnLBuilder.Build(WithFairSale(new DateOnly(2026, 12, 6)));
        var dec = months.Single(x => x.Month == "2026-12");
        Assert.Equal(40m - 1.50m - 3m - 6m, dec.NetProfit);
        Assert.True(dec.SelfTestPassed);
    }

    [Fact]
    public void Outside_sale_has_a_profit_and_stays_out_of_etsy_only_reports()
    {
        var data = WithFairSale();
        var orders = OrderReport.Build(data);
        var fair = orders.Single(o => o.OrderId == "M-1");
        Assert.True(fair.OnStatement);
        Assert.Equal(0m, fair.ListingFees);
        Assert.Equal(40m - 1.50m - 3m - 6m, fair.Profit);
        Assert.StartsWith("Sold outside Etsy: Craft fair", fair.Status);

        var ads = AdReport.Build(data, orders, Period.All)[^1];
        Assert.Equal(2, ads.Orders);                                           // the two Etsy orders on a statement only
        Assert.DoesNotContain(PricingCheck.TypicalShipping(data, orders), kv => kv.Value.Shipping == 0 && kv.Key == "L1");
        var tax = TaxReport.Build(2026, PnLBuilder.Build(data), [], ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        Assert.Equal(40m, tax.Lines.Single(l => l.Name == "Sales outside Etsy").Total);
        Assert.Equal(-1.50m, tax.Lines.Single(l => l.Name == "Fees outside Etsy").Total);
    }
}

public class YearAndFormTests
{
    private static MonthlyPnL M(string month, decimal sales, int orders = 1) => new() { Month = month, GrossSales = sales, Orders = orders };

    [Fact]
    public void Year_over_year_compares_only_months_both_years_have()
    {
        var rows = YearOverYear.Build([M("2025-01", 100), M("2025-02", 50), M("2025-11", 999), M("2026-01", 150), M("2026-02", 40)], 2026);
        var total = rows[^1];
        Assert.True(total.IsTotal);
        Assert.Equal(190m, total.Sales);
        Assert.Equal(150m, total.LastSales);          // November last year isn't compared against a month we don't have yet
        Assert.Equal(0.5m, rows.Single(r => r.Label == "January").SalesChange);
        Assert.Contains(rows, r => r.Label == "November" && r.Sales == 0 && r.LastSales == 999);
    }

    [Fact]
    public void Form_1099K_check_uses_payments_before_refunds()
    {
        var pnl = new List<MonthlyPnL> { M("2026-03", 300), M("2026-04", 200), M("2025-12", 999) };
        pnl[0].Refunds = -20m;
        var c = TaxReport.Check1099K(2026, pnl, 510m);
        Assert.Equal(500m, c.EtsyPayments);
        Assert.Equal(-20m, c.Refunds);
        Assert.Equal(10m, c.Difference);
        Assert.Null(TaxReport.Check1099K(2026, pnl, null).Difference);
    }
}

public class CalculatorAndPackagingTests
{
    [Fact]
    public void Calculator_breaks_down_the_fees_and_meets_the_goal_at_its_min_price()
    {
        var r = new FeeRates();
        var q = PriceCalculator.Quote(r, 20m, 5m, null, 4m, goal: 5m);
        Assert.Equal(-1.63m, q.TransactionFee);       // 6.5% of $25 = 1.625
        Assert.Equal(-1.00m, q.ProcessingFee);        // 3% of $25 + 0.25
        Assert.Equal(-0.20m, q.ListingFee);
        Assert.Equal(-3.75m, q.OffsiteAdsFee);
        Assert.Equal(20m - 4m - 1.63m - 1.00m - 0.20m, q.Organic);
        Assert.Equal(q.Organic - 3.75m, q.WorstCase);
        var atMin = PriceCalculator.Quote(r, q.MinPrice, 5m, null, 4m, goal: 5m);
        Assert.True(atMin.WorstCase >= 4.98m, $"worst case at the minimum price was {atMin.WorstCase}");   // per-line fee rounding can cost a cent or two
    }

    [Fact]
    public void Parts_cost_matches_the_rule_based_cost()
    {
        var model = new CostModel
        {
            Materials =
            [
                new Material { Id = 1, Name = "PLA", Prices = [new(new(2000, 1, 1), 0.02m)] },
                new Material { Id = 2, Name = "Magnet", Unit = Units.Each, Prices = [new(new(2000, 1, 1), 0.50m)] },
            ],
            Parts = [new Part { Id = 1, Name = "Board", Versions = [new(new(2000, 1, 1), [new PartLine(1, 100m), new PartLine(2, 1m)], 90m)] }],
            CostSettings = [new CostSettings { WastePercent = 0.10m, MachineHourRate = 0.20m }],
        };
        var c = model.CostOfParts([(1, 2m)], new DateOnly(2026, 1, 1));
        Assert.True(c.Mapped);
        Assert.Equal(4m, c.Material);
        Assert.Equal(0.4m, c.Waste);
        Assert.Equal(1m, c.Components);
        Assert.Equal(3m, c.MakingHours);
        Assert.Equal(4m + 0.4m + 1m + 0.6m, c.Total);
    }

    [Fact]
    public void Listing_packaging_overrides_the_shop_wide_per_unit_amount()
    {
        var data = SampleShop.Data();
        data.Costs.CostSettings.Clear();
        data.Costs.CostSettings.Add(new CostSettings { PackagingPerUnit = 0.30m, PackagingPerOrder = 0.10m });
        data.Costs.PackagingByListing["L2"] = 1.00m;
        var orders = OrderReport.Build(data);
        Assert.Equal(-(0.10m + 0.30m), orders.Single(o => o.OrderId == "500").Packaging);               // one sign
        Assert.Equal(-(0.10m + 1.00m + 0.30m), orders.Single(o => o.OrderId == "501").Packaging);       // magnet + sign
        var m = Assert.Single(PnLBuilder.Build(data));
        Assert.Equal(-(2 * 0.10m + 0.30m + 1.00m + 0.30m), m.Packaging);                                 // order 502 is past the last statement
    }
}

public sealed class DataFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    public DataFolderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Pointer_remembers_the_chosen_folder_and_null_means_default()
    {
        var loc = new DataLocation(Path.Combine(_dir, "AppData", "location.json"));
        Assert.Null(loc.Read());
        loc.Write(@"E:\Shop data");
        Assert.Equal(@"E:\Shop data", loc.Read());
        loc.Write(null);
        Assert.Null(loc.Read());
        Assert.False(File.Exists(loc.PointerFile));
    }

    [Fact]
    public void Copy_takes_everything_but_the_demo_and_leaves_the_source()
    {
        var from = Path.Combine(_dir, "From");
        var db = new Database(((IAppPaths)new FolderAppPaths(from)).DatabaseFile);
        Directory.CreateDirectory(Path.Combine(from, "Backups"));
        Directory.CreateDirectory(Path.Combine(from, "Demo"));
        File.WriteAllText(Path.Combine(from, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(from, "Backups", "Crafting Margins Backup 2026-11-19 210000.db"), "b");
        File.WriteAllText(Path.Combine(from, "Demo", "Crafting Margins.db"), "demo");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var to = Path.Combine(_dir, "To");
        Assert.Equal(3, DataLocation.Copy(from, to));
        Assert.True(DataLocation.HasData(to));
        Assert.True(File.Exists(Path.Combine(to, "Backups", "Crafting Margins Backup 2026-11-19 210000.db")));
        Assert.False(Directory.Exists(Path.Combine(to, "Demo")));
        Assert.True(DataLocation.HasData(from));                                  // nothing removed
        Assert.Throws<IOException>(() => DataLocation.Copy(from, Path.Combine(from, "Inside")));
        Assert.Throws<IOException>(() => DataLocation.Copy(from, from));
    }

    [Fact]
    public void Files_are_named_with_a_space_and_backups_say_so()
    {
        IAppPaths paths = new FolderAppPaths(_dir);
        Assert.Equal("Crafting Margins.db", Path.GetFileName(paths.DatabaseFile));
        var db = new Database(paths.DatabaseFile);
        var file = Backups.MakeBackup(db, paths.BackupFolder, 3, new DateTime(2026, 11, 19, 21, 0, 0))!;
        Assert.Equal("Crafting Margins Backup 2026-11-19 210000.db", Path.GetFileName(file));
        Assert.Equal("Backup from Nov 19, 2026, 9:00 PM", Backups.Describe(file));
    }
}
