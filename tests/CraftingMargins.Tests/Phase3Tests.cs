// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>Made-up October shop used by the Phase 3 report tests.</summary>
internal static class SampleShop
{
    public static readonly DateOnly Oct5 = new(2026, 10, 5), Oct6 = new(2026, 10, 6), Oct10 = new(2026, 10, 10), Oct20 = new(2026, 10, 20);

    private static StatementLine L(DateOnly d, string type, string title, string info, decimal net)
    {
        var l = new StatementLine { Date = d, Type = type, Title = title, Info = info, Amount = net, Net = net };
        StatementIds.Fill(l);
        return l;
    }

    /// <summary>
    /// Order 500: 1 × sign (L1, Large) $16 + $7.14 shipping + $1 tax, through an Offsite Ad.  Profit 6.86.
    /// Order 501: magnet (L2) $10 + sign (L1, Small) $16, $2 off with coupon FALL, $5 shipping, a $6 label.  Profit 15.59.
    /// Order 502: magnet, not on a statement yet.
    /// </summary>
    public static ShopData Data() => new()
    {
        Statement =
        [
            L(Oct5, "Sale", "Payment for Order #500", "", 24.14m),
            L(Oct5, "Tax", "Sales tax paid by buyer", "Order #500", -1.00m),
            L(Oct5, "Fee", "Transaction fee: Sign", "Order #500", -1.04m),
            L(Oct5, "Fee", "Transaction fee: Shipping", "Order #500", -0.46m),
            L(Oct5, "Fee", "Processing fee", "Order #500", -0.97m),
            L(Oct5, "Marketing", "Fee for sale made through Offsite Ads", "Order #500", -3.47m),
            L(Oct5, "Fee", "Listing fee", "Listing #1", -0.20m),
            L(Oct5, "Shipping", "USPS shipping label", "Label #1", -7.14m),
            L(Oct6, "Sale", "Payment for Order #501", "", 29.00m),
            L(Oct6, "Fee", "Transaction fee: Sign", "Order #501", -1.56m),
            L(Oct6, "Fee", "Transaction fee: Shipping", "Order #501", -0.33m),
            L(Oct6, "Fee", "Processing fee", "Order #501", -1.12m),
            L(Oct6, "Fee", "Listing fee", "Listing #1", -0.20m),
            L(Oct6, "Fee", "Listing fee", "Listing #2", -0.20m),
            L(Oct6, "Shipping", "USPS shipping label", "Label #2", -6.00m),
            // For a label bought before this statement, so it belongs to no order here (LabelTests covers the matched case).
            L(Oct10, "Shipping", "Adjustment for USPS shipping label", "Label #9", -0.50m),
        ],
        Orders =
        [
            new Order { OrderId = "500", SaleDate = Oct5, DateShipped = Oct5, Shipping = 7.14m, SalesTax = 1m, CardProcessingFees = 0.97m },
            new Order { OrderId = "501", SaleDate = Oct6, DateShipped = Oct6, Shipping = 5m, DiscountAmount = 2m, CardProcessingFees = 1.12m },
            new Order { OrderId = "502", SaleDate = Oct20 },
        ],
        Items =
        [
            new OrderItem { TransactionId = "t1", OrderId = "500", ListingId = "L1", ItemName = "Sign", SaleDate = Oct5, Quantity = 1, Price = 16m, Variations = "Size:Large" },
            new OrderItem { TransactionId = "t2", OrderId = "501", ListingId = "L2", ItemName = "Magnet", SaleDate = Oct6, Quantity = 1, Price = 10m, CouponCode = "FALL" },
            new OrderItem { TransactionId = "t3", OrderId = "501", ListingId = "L1", ItemName = "Sign", SaleDate = Oct6, Quantity = 1, Price = 16m, Variations = "Size:Small", CouponCode = "FALL" },
            new OrderItem { TransactionId = "t4", OrderId = "502", ListingId = "L2", ItemName = "Magnet", SaleDate = Oct20, Quantity = 1, Price = 10m },
        ],
        Costs = new CostModel
        {
            Rules = [new MappingRule { ListingId = "L1", FixedCost = 3m }, new MappingRule { ListingId = "L2", FixedCost = 1m }],
        },
    };
}

public class ReportTests
{
    private static readonly ShopData Data = SampleShop.Data();
    private static readonly List<OrderProfit> Orders = OrderReport.Build(Data);

    [Fact]
    public void Sample_orders_have_the_expected_profit()
    {
        Assert.Equal(6.86m, Orders.Single(o => o.OrderId == "500").Profit);
        Assert.Equal(15.59m, Orders.Single(o => o.OrderId == "501").Profit);
        Assert.Equal("FALL", Orders.Single(o => o.OrderId == "501").Coupon);
    }

    [Fact]
    public void Product_report_splits_a_shared_order_by_item_price()
    {
        var rows = ProductReport.Build(Data, Orders, Period.All);
        var sign = rows.Single(r => r.ListingId == "L1");
        var magnet = rows.Single(r => r.ListingId == "L2");
        Assert.Equal(6.86m + 9.59m, sign.Profit);          // 501 split 10 : 16 → 6.00 / 9.59
        Assert.Equal(6.00m, magnet.Profit);
        Assert.Equal(15.59m + 6.86m, sign.Profit + magnet.Profit);
        Assert.Equal(6.86m, sign.WorstOrder);
        Assert.Equal(2, magnet.Orders);                   // 502 counts as an order...
        Assert.Equal(1, magnet.OrdersWithProfit);         // ...but not toward profit
        Assert.Equal(0.5m, sign.AdShare);
        Assert.Equal(sign, rows[0]);                      // most profitable first
    }

    [Fact]
    public void Share_out_always_adds_back_up()
    {
        var parts = ProductReport.ShareOut(10m, [(1m, 1), (1m, 1), (1m, 1)]);
        Assert.Equal([3.33m, 3.33m, 3.34m], parts);
        Assert.Equal([2m, 2m], ProductReport.ShareOut(4m, [(0m, 1), (0m, 1)]));   // free items split by units
    }

    [Fact]
    public void Ad_report_counts_ad_and_organic_orders()
    {
        var rows = AdReport.Build(Data, Orders, Period.All);
        var total = rows[^1];
        Assert.True(total.IsTotal);
        Assert.Equal(1, total.AdOrders);
        Assert.Equal(1, total.OrganicOrders);           // 502 isn't on a statement, so it isn't counted
        Assert.Equal(6.86m, total.AdAverage);
        Assert.Equal(15.59m, total.OrganicAverage);
        Assert.Equal(-3.47m, total.OffsiteAdsPaid);
    }

    [Fact]
    public void Shipping_report_separates_adjustments_and_finds_losing_labels()
    {
        var t = ShippingReport.Build(Data, Period.All)[^1];
        Assert.Equal(12.14m, t.Charged);
        Assert.Equal(-13.14m, t.Labels);
        Assert.Equal(-0.50m, t.Adjustments);
        Assert.Equal(-1.50m, t.Net);
        var lost = Assert.Single(ShippingReport.LostOnShipping(Orders, Period.All));
        Assert.Equal("501", lost.OrderId);
    }

    [Fact]
    public void Discount_report_lists_discounted_orders_only()
    {
        Assert.Equal("501", Assert.Single(DiscountReport.Build(Orders, Period.All)).OrderId);
        Assert.Empty(DiscountReport.Build(Orders, Period.Year(2025)));
    }

    [Fact]
    public void Period_filters_by_sale_date()
    {
        var rows = ProductReport.Build(Data, Orders, new Period(SampleShop.Oct20, null));
        Assert.Equal("L2", Assert.Single(rows).ListingId);
    }
}

public class PricingTests
{
    private static readonly FeeRates R = new();

    [Fact]
    public void Label_defaults_to_shipping_and_a_dearer_label_costs_the_difference()
    {
        Assert.Equal(PricingEstimate.Organic(R, 20m, 5m, 3m), PricingEstimate.Organic(R, 20m, 5m, 3m, label: 5m));
        Assert.Equal(PricingEstimate.Organic(R, 20m, 5m, 3m) - 1.5m, PricingEstimate.Organic(R, 20m, 5m, 3m, label: 6.5m));
        // Free shipping: the label is all cost.
        Assert.Equal(PricingEstimate.Organic(R, 20m, 0m, 3m) - 4m, PricingEstimate.Organic(R, 20m, 0m, 3m, label: 4m));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(5, null)]
    [InlineData(0, 4.5)]
    [InlineData(6, 7.25)]
    public void Min_price_lands_exactly_on_the_goal(double ship, double? label)
    {
        decimal s = (decimal)ship; decimal? l = label is null ? null : (decimal)label;
        var p = PricingEstimate.MinPriceForTarget(R, 5m, s, 2.5m, l);
        Assert.Equal(5m, Math.Round(PricingEstimate.WorstCase(R, p, s, 2.5m, l), 10));
    }

    private static Listing ApiSign() => new()
    {
        Title = "Sign", ListingId = "L1", State = "active",
        Offerings =
        [
            new(new() { ["Size"] = "Large" }, 40m, 5, true),
            new(new() { ["Size"] = "Small" }, 16m, 5, true),
            new(new() { ["Size"] = "Standard" }, 30m, 5, true),    // retired name
            new(new() { ["Size"] = "Huge" }, 99m, 0, false),       // not for sale
        ],
    };

    [Fact]
    public void Api_offerings_are_each_checked_and_retired_or_disabled_ones_skipped()
    {
        var data = SampleShop.Data();
        data.Costs.Aliases.Add(new VariationAlias { ListingId = "L1", VariationName = "Size", OldValue = "Standard", NewValue = "Large" });
        var orders = OrderReport.Build(data);
        var rows = PricingCheck.Build(data, [ApiSign()], orders, new PricingOptions { Floor = 7m, Today = SampleShop.Oct20 });
        Assert.Equal(["Size:Large", "Size:Small"], rows.Select(r => r.Options));
        Assert.All(rows, r => Assert.Equal("Etsy", r.PriceSource));

        // L1's only single-item order (500) shipped for $7.14 with an exactly matched $7.14 label.
        var large = rows[0];
        Assert.Equal(7.14m, large.Shipping);
        Assert.Equal(7.14m, large.Label);
        Assert.Equal(3m, large.Cost);
        Assert.Equal(Money.Round(PricingEstimate.WorstCase(R, 40m, 7.14m, 3m, 7.14m)), large.WorstCase);
        Assert.False(large.BelowFloor);
        Assert.Equal(6.88m, rows[1].WorstCase);   // $16 − 24.5% of $23.14 − $0.45 − $3 cost
        Assert.True(rows[1].BelowFloor);          // goal is $7 in this test
    }

    [Fact]
    public void Options_without_a_cost_rule_get_no_profit_numbers()
    {
        var data = SampleShop.Data();
        data.Costs.Rules.RemoveAll(r => r.ListingId == "L1");
        var rows = PricingCheck.Build(data, [ApiSign()], OrderReport.Build(data), new PricingOptions());
        Assert.All(rows, r => { Assert.Null(r.WorstCase); Assert.Null(r.Cost); Assert.False(r.BelowFloor); Assert.NotEmpty(r.Status); });
    }

    [Fact]
    public void Without_api_data_options_come_from_sales_at_the_last_price()
    {
        var data = SampleShop.Data();
        data.Items.Add(new OrderItem { TransactionId = "t5", OrderId = "502", ListingId = "L1", ItemName = "Sign", SaleDate = SampleShop.Oct20, Quantity = 1, Price = 18m, Variations = "Size:Small" });
        var listings = new List<Listing> { new() { Title = "Sign" } };   // CSV download: no ID, no offerings; Magnet is no longer for sale
        var rows = PricingCheck.Build(data, listings, OrderReport.Build(data), new PricingOptions { Today = SampleShop.Oct20 });
        Assert.All(rows, r => Assert.Equal("L1", r.ListingId));
        var small = rows.Single(r => r.Options == "Size:Small");
        Assert.Equal(18m, small.Price);
        Assert.StartsWith("Last sold", small.PriceSource);
    }

    [Fact]
    public void Options_the_listings_download_no_longer_offers_are_skipped()
    {
        var data = SampleShop.Data();
        data.Items.Add(new OrderItem { TransactionId = "t5", OrderId = "502", ListingId = "L1", ItemName = "Sign", SaleDate = SampleShop.Oct20, Quantity = 1, Price = 12m, Variations = "Size:Standard,Name:Ann" });
        var listings = new List<Listing> { new() { Title = "Sign", Variations = [new("Size", ["Large", "Small"])] } };
        var rows = PricingCheck.Build(data, listings, OrderReport.Build(data), new PricingOptions());
        Assert.Equal(["Size:Large", "Size:Small"], rows.Select(r => r.Options).Order());
    }

    [Fact]
    public void Planned_sale_discounts_only_the_chosen_listings()
    {
        var data = SampleShop.Data();
        var orders = OrderReport.Build(data);
        var listings = new List<Listing> { ApiSign(), new() { Title = "Magnet", ListingId = "L2", State = "active", Offerings = [new([], 10m, 3, true)] } };
        var rows = PricingCheck.Build(data, listings, orders, new PricingOptions { DiscountPercent = 0.25m, DiscountedListings = new HashSet<string> { "L1" } });
        var large = rows.Single(r => r.Options == "Size:Large");
        Assert.Equal(40m, large.ListPrice);
        Assert.Equal(30m, large.Price);
        var magnet = rows.Single(r => r.ListingId == "L2");
        Assert.Equal(10m, magnet.Price);
    }

    [Fact]
    public void Profit_per_print_hour_uses_the_part_print_time()
    {
        var data = SampleShop.Data();
        data.Costs.Materials.Add(new Material { Id = 1, Name = "PLA", Prices = [new(new(2000, 1, 1), 0.02m)] });
        data.Costs.Parts.Add(new Part { Id = 1, Name = "Board", Versions = [PartVersion.Of(new(2000, 1, 1), 1, 100m, 120m)] });
        data.Costs.Rules.Single(r => r.ListingId == "L1").Parts.Add(new RulePart(1, 1));
        var row = PricingCheck.Build(data, [ApiSign()], OrderReport.Build(data), new PricingOptions()).First(r => r.Options == "Size:Large");
        Assert.Equal(2m, row.MakingHours);
        Assert.Equal(5m, row.Cost);   // $3 fixed + 100 g × $0.02
        Assert.Equal(Money.Round(row.Organic!.Value / 2), row.OrganicPerHour);
    }
}

public class TaxTests
{
    private static List<MonthlyPnL> Pnl() => PnLBuilder.Build(SampleShop.Data());

    private static readonly List<Expense> Expenses =
    [
        new() { Date = new(2026, 3, 2), Amount = 25m, Category = "Filament", Vendor = "Store" },
        new() { Date = new(2026, 10, 9), Amount = 100m, Category = "Equipment" },
        new() { Date = new(2025, 12, 30), Amount = 50m, Category = "Equipment" },   // other year
    ];

    [Fact]
    public void Used_basis_matches_the_pnl_and_leaves_filament_purchases_out()
    {
        var pnlProfit = Pnl().Sum(m => m.NetProfit);
        var t = TaxReport.Build(2026, Pnl(), Expenses, ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        Assert.Equal(pnlProfit - 100m, t.NetProfit);                 // equipment counts; filament bought doesn't
        var filament = t.Lines.Single(l => l.Name == "Filament");
        Assert.False(filament.InTotal);
        Assert.Equal(-25m, filament.Month(3));
        Assert.Equal(t.NetProfit, t.Lines.Single(l => l.IsTotal).Total);
    }

    [Fact]
    public void Bought_basis_swaps_per_sale_costs_for_purchases()
    {
        var p = Pnl();
        var t = TaxReport.Build(2026, p, Expenses, ScheduleC.DefaultCategories(), SuppliesBasis.Bought);
        var withoutPerSale = p.Sum(m => m.NetProfit - m.OtherCosts);
        Assert.Equal(withoutPerSale - 25m - 100m, t.NetProfit);
        Assert.All(t.Lines.Where(l => l.Section == TaxReport.UsedSection), l => Assert.False(l.InTotal));
    }

    [Fact]
    public void Bought_basis_says_which_costs_it_leaves_out_and_how_much()
    {
        MonthlyPnL[] p = [new() { Month = "2026-03", GrossSales = 100m, FixedCosts = -12m, MachineCost = -3.50m, Material = -8m }];
        var bought = TaxReport.Build(2026, p, [], ScheduleC.DefaultCategories(), SuppliesBasis.Bought);
        var note = Assert.Single(bought.Notes, n => n.Contains("leaves out"));
        Assert.Contains("$12.00", note);          // fixed item costs
        Assert.Contains("$3.50", note);           // electricity and printer wear
        Assert.DoesNotContain("$8.00", note);     // filament IS replaced by expense-log purchases, so it isn't part of this warning
        Assert.Contains("nothing in the expense log replaces it", bought.Lines.Single(l => l.Name == "Fixed item costs").Note);

        // On the "used" basis they count, so there is nothing to warn about.
        var used = TaxReport.Build(2026, p, [], ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        Assert.DoesNotContain(used.Notes, n => n.Contains("leaves out"));
        Assert.True(used.Lines.Single(l => l.Name == "Fixed item costs").InTotal);
    }

    [Fact]
    public void Schedule_c_view_adds_up_to_the_same_profit()
    {
        var t = TaxReport.Build(2026, Pnl(), Expenses, ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        var c = t.ByScheduleC();
        Assert.Equal(t.NetProfit, c.Single(l => l.IsTotal).Total);
        Assert.Equal(c.Where(l => !l.IsTotal).Sum(l => l.Total), t.NetProfit);
        Assert.Equal(ScheduleC.GrossReceipts, c[0].Name);
        var fees = c.Single(l => l.Name == ScheduleC.Commissions);
        Assert.Equal(-1.04m - 0.46m - 0.97m - 0.60m - 1.56m - 0.33m - 1.12m, fees.Total);   // three listing-fee lines
        Assert.Equal(-3.47m, c.Single(l => l.Name == ScheduleC.Advertising).Total);
    }

    [Fact]
    public void Months_without_statements_are_noted()
    {
        var t = TaxReport.Build(2026, Pnl(), [], ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        Assert.Contains(t.Notes, n => n.Contains("1 month(s)"));
        Assert.Contains(TaxReport.Build(2024, Pnl(), [], ScheduleC.DefaultCategories(), SuppliesBasis.Used).Notes, n => n.Contains("No statements"));
    }
}

public sealed class ExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    public ExportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Xlsx_is_a_valid_package_with_numbers_dates_and_text()
    {
        var path = Path.Combine(_dir, "Test.xlsx");
        var a = new Sheet("Tax summary 2026").Add("Name", "Amount", "Date").Add("Fees & <ads>", -12.34m, new DateOnly(2026, 10, 5)).AddBold("Total", 1234.5m, null);
        var b = new Sheet("Tax summary 2026");   // duplicate name, and too-long/invalid names below
        var c = new Sheet("Orders: a very long sheet name that Excel would refuse");
        Export.WriteXlsx(path, [a, b, c]);

        using var zip = ZipFile.OpenRead(path);
        string[] parts = ["[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml", "xl/worksheets/sheet3.xml"];
        foreach (var p in parts) Assert.NotNull(zip.GetEntry(p));
        XDocument Load(string p) { using var s = zip.GetEntry(p)!.Open(); return XDocument.Load(s); }   // throws if not well-formed

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var names = Load("xl/workbook.xml").Descendants(ns + "sheet").Select(e => (string)e.Attribute("name")!).ToList();
        Assert.Equal(3, names.Distinct().Count());
        Assert.All(names, n => { Assert.True(n.Length <= 31); Assert.DoesNotContain(':', n); });

        var cells = Load("xl/worksheets/sheet1.xml").Descendants(ns + "c").ToList();
        Assert.Equal("Fees & <ads>", cells.Single(e => (string)e.Attribute("r")! == "A2").Value);
        Assert.Equal("-12.34", cells.Single(e => (string)e.Attribute("r")! == "B2").Value);
        Assert.Equal((new DateOnly(2026, 10, 5).DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(), cells.Single(e => (string)e.Attribute("r")! == "C2").Value);
        Assert.Equal("3", (string)cells.Single(e => (string)e.Attribute("r")! == "B3").Attribute("s")!);   // bold money
    }

    [Fact]
    public void Csv_quotes_text_and_never_starts_a_formula()
    {
        var path = Path.Combine(_dir, "Test.csv");
        Export.WriteCsv(path, new Sheet("x").Add("Line", "Total").Add("Fees, \"all\"", -1.5m).Add("=HYPERLINK(1)", 2m));
        var lines = File.ReadAllLines(path);
        Assert.Equal("\"Fees, \"\"all\"\"\",-1.50", lines[1]);
        Assert.Equal("'=HYPERLINK(1),2.00", lines[2]);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Column_names(int i, string name) => Assert.Equal(name, Export.ColumnName(i));

    [Fact]
    public void Year_workbook_has_every_sheet()
    {
        var data = SampleShop.Data();
        var pnl = PnLBuilder.Build(data);
        var tax = TaxReport.Build(2026, pnl, [], ScheduleC.DefaultCategories(), SuppliesBasis.Used);
        var sheets = ReportSheets.YearWorkbook(tax, pnl, OrderReport.Build(data), []);
        Assert.Equal(["Tax summary 2026", "Schedule C 2026", "Monthly P&L", "Orders 2026", "Expenses 2026"], sheets.Select(s => s.Name));
        Assert.Equal(4, sheets[3].Rows.Count);    // header + 3 orders
        var path = Path.Combine(_dir, "Year.xlsx");
        Export.WriteXlsx(path, sheets);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [Fact]
    public void Expenses_and_categories_are_stored()
    {
        var db = new Database(Path.Combine(_dir, "test.db"));
        Assert.Equal(ScheduleC.DefaultCategories(), db.LoadExpenseCategories());
        db.SaveExpenses([new Expense { Date = new(2026, 2, 1), Amount = 19.99m, Category = "Filament", Vendor = "Shop", Note = "PLA" }]);
        var e = Assert.Single(db.LoadExpenses());
        Assert.Equal(19.99m, e.Amount);
        Assert.Equal(1, e.Id);
        db.SaveExpenseCategories([new ExpenseCategory("Filament", ScheduleC.Supplies, true)]);
        Assert.Single(db.LoadExpenseCategories());
        db.DeleteAllData();
        Assert.Empty(db.LoadExpenses());
    }
}

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.9", true)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("2.0.0-beta", "1.9.0", true)]
    [InlineData("latest", "1.0.0", false)]
    public void Version_compare(string tag, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(tag, current));

    [Fact]
    public async Task Reports_a_newer_release_and_ignores_drafts_and_prereleases()
    {
        var json = """{"tag_name":"v1.3.0","html_url":"https://github.com/owner/app/releases/tag/v1.3.0","draft":false,"prerelease":false}""";
        var fake = new FakeEtsy((_, _) => FakeEtsy.Json(json));
        var r = await UpdateCheck.NewerReleaseAsync(new HttpClient(fake), "owner/app", "1.2.0");
        Assert.Equal(new ReleaseInfo("1.3.0", "https://github.com/owner/app/releases/tag/v1.3.0"), r);
        Assert.Equal("https://api.github.com/repos/owner/app/releases/latest", fake.Calls[0].Request.RequestUri!.ToString());

        var pre = new FakeEtsy((_, _) => FakeEtsy.Json(json.Replace("\"prerelease\":false", "\"prerelease\":true")));
        Assert.Null(await UpdateCheck.NewerReleaseAsync(new HttpClient(pre), "owner/app", "1.2.0"));
        var none = new FakeEtsy((_, _) => FakeEtsy.Json("{}", HttpStatusCode.NotFound));
        Assert.Null(await UpdateCheck.NewerReleaseAsync(new HttpClient(none), "owner/app", "1.2.0"));
    }

    [Fact]
    public async Task No_repo_means_no_request()
    {
        var fake = new FakeEtsy((_, _) => throw new InvalidOperationException("should not be called"));
        Assert.Null(await UpdateCheck.NewerReleaseAsync(new HttpClient(fake), "", "1.0.0"));
        Assert.Empty(fake.Calls);
    }
}
