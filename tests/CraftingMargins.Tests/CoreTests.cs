// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("-$1.04", -1.04)]
    [InlineData("$25.19", 25.19)]
    [InlineData("--", 0)]
    [InlineData("", 0)]
    [InlineData("$1,234.50", 1234.50)]
    [InlineData("12.90", 12.90)]
    [InlineData("-16.91", -16.91)]
    public void Parses_etsy_money(string text, double expected) =>
        Assert.Equal((decimal)expected, Money.Parse(text));

    [Fact]
    public void Rounds_halves_away_from_zero() // Case B
    {
        Assert.Equal(0.59m, Money.RoundFee(0.065m * 9.00m));
        Assert.Equal(-0.59m, Money.RoundFee(-0.585m));
    }

    [Fact]
    public void Cleans_html_entities() =>
        Assert.Equal("Custom \"Since Last Incident\" & I'm", EtsyText.Clean("Custom &quot;Since Last Incident&quot; &amp; I&#39;m "));
}

public class FeeTests
{
    [Fact]
    public void Case_A_matches_the_real_statement()
    {
        var f = FeeCalculator.Compute(new FeeRates(), itemTotal: 32.00m, shipping: 7.14m, salesTax: 4.11m, units: 2, adAttributed: true);
        Assert.Equal(2.08m, f.TransactionItem);
        Assert.Equal(0.46m, f.TransactionShipping);
        Assert.Equal(1.55m, f.Processing);
        Assert.Equal(5.87m, f.OffsiteAds);
        Assert.Equal(0.40m, f.Listing);
    }

    [Fact]
    public void Case_C_offsite_ads_capped_at_100()
    {
        var f = FeeCalculator.Compute(new FeeRates(), 800m, 0m, 0m, 1, adAttributed: true);
        Assert.Equal(100.00m, f.OffsiteAds);
    }

    [Fact]
    public void Organic_order_has_no_ad_fee() =>
        Assert.Equal(0m, FeeCalculator.Compute(new FeeRates(), 16m, 7m, 1m, 1, adAttributed: false).OffsiteAds);

    [Fact]
    public void Pricing_estimates_follow_the_spec_formulas()
    {
        var r = new FeeRates();
        // P=25, S=7, M=3: organic = 25 - 0.095*32 - 0.45 - 3 = 18.51 ; worst = 25 - 0.245*32 - 0.45 - 3 = 13.71
        Assert.Equal(18.51m, PricingEstimate.Organic(r, 25m, 7m, 3m));
        Assert.Equal(13.71m, PricingEstimate.WorstCase(r, 25m, 7m, 3m));
        var min = PricingEstimate.MinPriceForTarget(r, 5m, 7m, 3m);
        Assert.Equal(5m, Math.Round(PricingEstimate.WorstCase(r, min, 7m, 3m), 10));
    }

    [Fact]
    public void Effective_dated_rates_pick_the_right_version()
    {
        var versions = new List<FeeRates>
        {
            new() { EffectiveFrom = new(2020, 1, 1), OffsiteAdsRate = 0.15m },
            new() { EffectiveFrom = new(2026, 7, 3), OffsiteAdsRate = 0.12m },
        };
        Assert.Equal(0.15m, Effective.At(versions, v => v.EffectiveFrom, new(2026, 7, 2)).OffsiteAdsRate);
        Assert.Equal(0.12m, Effective.At(versions, v => v.EffectiveFrom, new(2026, 7, 3)).OffsiteAdsRate);
    }
}

public class VariationTests
{
    [Fact]
    public void Splits_pairs_but_keeps_commas_inside_values()
    {
        var v = Variations.Parse("Size:Large (10&quot; x 7.5&quot;),Mounting Method:Desk Stand,Gauges:1/4&quot;, 1/2&quot;,Sets of Digits:2 Sets (0-9)");
        Assert.Equal(4, v.Count);
        Assert.Equal("Large (10\" x 7.5\")", v[0].Value);
        Assert.Equal("1/4\", 1/2\"", v[2].Value);
        Assert.Equal("2 Sets (0-9)", v[3].Value);
    }

    [Fact]
    public void Strips_personalization()
    {
        var s = Variations.Strip("Color:White,Personalization:Bob Smith", ["Personalization"]);
        Assert.Equal("Color:White", s);
    }

    [Fact]
    public void Empty_variations() => Assert.Empty(Variations.Parse(""));

    [Fact]
    public void Flags_personalization_not_in_listings()
    {
        var items = new[]
        {
            new OrderItem { Variations = "Size:Large,Name for top:Pat" },
            new OrderItem { Variations = "Size:Large,Name for top:Sam" },
        };
        var listings = new[] { new Listing { Variations = [new("Size", ["Large", "Small"])] } };
        Assert.Equal(["Name for top"], Variations.SuspectFreeText(items, listings, []));
        Assert.Empty(Variations.SuspectFreeText(items, listings, ["Name for top"]));
    }

    [Fact]
    public void Label_matched_by_amount_when_several_ship_same_day()
    {
        var d = new DateOnly(2026, 10, 5);
        var data = new ShopData
        {
            Statement =
            [
                new StatementLine { Date = d, Type = "Shipping", Title = "USPS shipping label", LabelId = "1", Net = -5.00m },
                new StatementLine { Date = d, Type = "Shipping", Title = "USPS shipping label", LabelId = "2", Net = -6.01m },
            ],
            Orders = [new Order { OrderId = "1", DateShipped = d, Shipping = 6.01m }, new Order { OrderId = "2", DateShipped = d, Shipping = 5.00m }],
        };
        var a = OrderReport.AllocateLabels(data);
        Assert.Equal((-6.01m, false), a["1"]);
        Assert.Equal((-5.00m, false), a["2"]);
    }
}

public class CategoryTests
{
    private readonly Categorizer _c = new();

    [Theory]
    [InlineData("Sale", "Payment for Order #1001", Category.GrossSales)]
    [InlineData("Tax", "Sales tax paid by buyer", Category.SalesTax)]
    [InlineData("Tax", "Refund to buyer for sales tax", Category.SalesTax)]
    [InlineData("Buyer Fee", "Colorado Retail Delivery Fee (paid by buyer)", Category.SalesTax)]
    [InlineData("Refund", "Refund for Order #1001", Category.Refunds)]
    [InlineData("Fee", "Transaction fee: Shipping", Category.TransactionFees)]
    [InlineData("Fee", "Transaction fee: Widget Holder - Blue", Category.TransactionFees)]
    [InlineData("Fee", "Credit for transaction fee on shipping", Category.TransactionFees)]
    [InlineData("Fee", "Processing fee", Category.ProcessingFees)]
    [InlineData("Fee", "Credit for processing fee", Category.ProcessingFees)]
    [InlineData("Fee", "Listing fee", Category.ListingFees)]
    [InlineData("Fee", "Credit for listing fee", Category.ListingFees)]
    [InlineData("Marketing", "Fee for sale made through Offsite Ads", Category.OffsiteAds)]
    [InlineData("Shipping", "USPS shipping label", Category.ShippingLabels)]
    [InlineData("Shipping", "Adjustment for USPS shipping label", Category.ShippingLabels)]
    [InlineData("Shipping", "Credit for USPS shipping label adjustment", Category.ShippingLabels)]
    [InlineData("Deposit", "$100.00 sent to your bank account", Category.Excluded)]
    [InlineData("Payment", "Card Payment", Category.Excluded)]
    public void Known_lines(string type, string title, string expected) =>
        Assert.Equal(expected, _c.Categorize(type, title));

    [Fact]
    public void Unknown_line_is_never_guessed() =>
        Assert.Equal(Category.Unknown, _c.Categorize("Fee", "Regulatory operating fee"));

    [Fact]
    public void User_rule_wins()
    {
        var c = new Categorizer([new CategoryRule("Fee", "Regulatory operating fee", Category.TransactionFees)]);
        Assert.Equal(Category.TransactionFees, c.Categorize("Fee", "Regulatory operating fee"));
    }

    [Fact]
    public void Ids_come_from_info_or_title()
    {
        var sale = new StatementLine { Type = "Sale", Title = "Payment for Order #3456", Info = "" };
        StatementIds.Fill(sale);
        Assert.Equal("3456", sale.OrderId);
        var label = new StatementLine { Type = "Shipping", Title = "USPS shipping label", Info = "Label #777" };
        StatementIds.Fill(label);
        Assert.Equal("777", label.LabelId);
        Assert.Null(label.OrderId);
    }
}

public class MappingTests
{
    // Made-up shop modeled on the Days Since sign example in the spec.
    private static CostModel SignShop(bool failed = false, decimal hourRate = 0) => new()
    {
        Materials =
        [
            new Material { Id = 1, Name = "PLA", Prices = [new(new(2000, 1, 1), 0.015m)] },
            new Material { Id = 2, Name = "Magnets, set of 4", Unit = Units.Each, Prices = [new(new(2000, 1, 1), 0.68m)] },   // bought by the piece: a component
        ],
        Parts =
        [
            new Part { Id = 1, Name = "Large board", Versions = [PartVersion.Of(new(2000, 1, 1), 1, 151m, 285m)] },
            new Part { Id = 2, Name = "Large digit set", Versions = [PartVersion.Of(new(2000, 1, 1), 1, 20m, 60m)] },
            new Part { Id = 3, Name = "Large stand", Versions = [PartVersion.Of(new(2000, 1, 1), 1, 40m, 90m)] },
            new Part { Id = 4, Name = "Magnets x4", Versions = [PartVersion.Of(new(2000, 1, 1), 2, 1m)] },
        ],
        Rules =
        [
            new MappingRule { Id = 1, ListingId = "L1", Name = "Board", Conditions = new() { ["Size"] = "Large" }, Parts = [new(1, 1)] },
            new MappingRule { Id = 2, ListingId = "L1", Name = "Digits", Conditions = new() { ["Size"] = "Large" }, Parts = [new(2, 1, "Sets of Digits")] },
            new MappingRule { Id = 3, ListingId = "L1", Name = "Stand", Conditions = new() { ["Size"] = "Large", ["Mounting Method"] = "Desk Stand" }, Parts = [new(3, 1)] },
            new MappingRule { Id = 4, ListingId = "L1", Name = "Magnets", Conditions = new() { ["Mounting Method"] = "Magnets on Back" }, Parts = [new(4, 1)] },
            new MappingRule { Id = 5, ListingId = "L2", Name = "Simple product", FixedCost = 2.50m },
        ],
        Aliases = [new VariationAlias { ListingId = "L1", VariationName = "Size", OldValue = "Standard", NewValue = "Large" }],
        CostSettings = [new CostSettings { WastePercent = failed ? 0.10m : 0m, MachineHourRate = hourRate }],
    };

    private static readonly DateOnly Day = new(2026, 10, 1);

    [Fact]
    public void Desk_stand_with_two_digit_sets()
    {
        var c = SignShop().CostOf("L1", "Size:Large,Mounting Method:Desk Stand,Sets of Digits:2 Sets (0-9)", 1, Day);
        Assert.True(c.Mapped);
        Assert.Equal((151m + 2 * 20m + 40m) * 0.015m, c.Material);
        Assert.Equal(0m, c.Components);
    }

    [Fact]
    public void Magnets_add_hardware_not_stand()
    {
        var c = SignShop().CostOf("L1", "Size:Large,Mounting Method:Magnets on Back,Sets of Digits:1 Set", 2, Day);
        Assert.Equal(2 * (151m + 20m) * 0.015m, c.Material);
        Assert.Equal(2 * 0.68m, c.Components);
    }

    [Fact]
    public void Retired_value_alias_maps_to_current()
    {
        var model = SignShop();
        var c = model.CostOf("L1", "Size:Standard,Mounting Method:Wall,Sets of Digits:1 Set", 1, Day);
        Assert.True(c.Mapped);
        Assert.Equal((151m + 20m) * 0.015m, c.Material);
        Assert.True(model.IsRetired("L1", "Size", "Standard"));
    }

    [Fact]
    public void Unmapped_line_is_never_defaulted()
    {
        var c = SignShop().CostOf("L1", "Size:Small", 1, Day);
        Assert.False(c.Mapped);
        Assert.Equal(0m, c.Total);
        Assert.False(SignShop().CostOf("L9", "", 1, Day).Mapped);
    }

    [Fact]
    public void Simple_product_uses_fixed_cost_per_unit() =>
        Assert.Equal(7.50m, SignShop().CostOf("L2", "Color:Red", 3, Day).Total);

    [Fact]
    public void Failed_print_and_print_hour_rate()
    {
        var c = SignShop(failed: true, hourRate: 0.30m).CostOf("L1", "Size:Large,Mounting Method:Wall,Sets of Digits:1 Set", 1, Day);
        var material = (151m + 20m) * 0.015m;
        Assert.Equal(material * 0.10m, c.Waste);
        Assert.Equal((285m + 60m) / 60m, c.MakingHours);
        Assert.Equal((285m + 60m) / 60m * 0.30m, c.MachineCost);
    }

    [Fact]
    public void Weights_current_vs_at_sale_date()
    {
        var model = SignShop();
        model.Parts[0].Versions.Add(PartVersion.Of(new(2026, 11, 1), 1, 200m, 285m));
        var current = model.CostOf("L1", "Size:Large,Mounting Method:Wall,Sets of Digits:1 Set", 1, Day);
        Assert.Equal((200m + 20m) * 0.015m, current.Material);

        var dated = new CostModel
        {
            Materials = model.Materials, Parts = model.Parts, Rules = model.Rules, Aliases = model.Aliases,
            CostSettings = model.CostSettings, UseWeightsAtSaleDate = true,
        }.CostOf("L1", "Size:Large,Mounting Method:Wall,Sets of Digits:1 Set", 1, Day);
        Assert.Equal((151m + 20m) * 0.015m, dated.Material);
    }
}

public class PnLTests
{
    // One made-up month: an ad-attributed order (Case A numbers), a label, a deposit.
    private static ShopData Month(params StatementLine[] extra)
    {
        var d = new DateOnly(2026, 10, 5);
        StatementLine L(string type, string title, string info, decimal amount, decimal fees)
        {
            var l = new StatementLine { Date = d, Type = type, Title = title, Info = info, Amount = amount, FeesAndTaxes = fees, Net = amount + fees };
            StatementIds.Fill(l);
            return l;
        }
        var lines = new List<StatementLine>
        {
            L("Sale", "Payment for Order #500", "", 43.25m, 0),
            L("Tax", "Sales tax paid by buyer", "Order #500", 0, -4.11m),
            L("Fee", "Transaction fee: Sign", "Order #500", 0, -2.08m),
            L("Fee", "Transaction fee: Shipping", "Order #500", 0, -0.46m),
            L("Fee", "Processing fee", "Order #500", 0, -1.55m),
            L("Marketing", "Fee for sale made through Offsite Ads", "Order #500", 0, -5.87m),
            L("Fee", "Listing fee", "Listing #9", 0, -0.20m),
            L("Fee", "Listing fee", "Listing #9", 0, -0.20m),
            L("Shipping", "USPS shipping label", "Label #1", 0, -7.14m),
            L("Deposit", "$20.00 sent to your bank account", "", -20m, 0),
        };
        lines.AddRange(extra);
        return new ShopData
        {
            Statement = lines,
            Orders = [new Order { OrderId = "500", SaleDate = d, DateShipped = d, Shipping = 7.14m, SalesTax = 4.11m, CardProcessingFees = 1.55m }],
            Items = [new OrderItem { TransactionId = "t1", OrderId = "500", ListingId = "L2", SaleDate = d, Quantity = 2, Price = 16m }],
            Costs = new CostModel { Rules = [new MappingRule { ListingId = "L2", FixedCost = 3m }] },
        };
    }

    [Fact]
    public void Self_test_passes_and_profit_adds_up()
    {
        var m = Assert.Single(PnLBuilder.Build(Month()));
        Assert.True(m.SelfTestPassed, string.Join("\n", m.Issues));
        Assert.Equal(39.14m, m.NetSales);
        Assert.Equal(-10.36m, m.EtsyFees);
        Assert.Equal(-6m, m.OtherCosts);
        Assert.Equal(39.14m - 10.36m - 7.14m - 6m, m.NetProfit);
        // Statement total (excluding the deposit) = Net Profit − material cost.
        Assert.Equal(m.StatementNet, m.NetProfit - m.OtherCosts);
    }

    [Fact]
    public void Unknown_line_fails_the_self_test()
    {
        var odd = new StatementLine { Date = new(2026, 10, 9), Type = "Fee", Title = "Mystery fee", Info = "", FeesAndTaxes = -1m, Net = -1m };
        var m = Assert.Single(PnLBuilder.Build(Month(odd)));
        Assert.False(m.SelfTestPassed);
        Assert.Contains(m.Issues, i => i.Message.Contains("Mystery fee"));
    }

    [Fact]
    public void Cancelled_order_missing_from_export_is_not_flagged()
    {
        var d = new DateOnly(2026, 10, 7);
        StatementLine L(string type, string title, string info, decimal net)
        {
            var l = new StatementLine { Date = d, Type = type, Title = title, Info = info, Amount = net, Net = net };
            StatementIds.Fill(l);
            return l;
        }
        var data = Month(
            L("Sale", "Payment for Order #900", "", 16.91m),
            L("Refund", "Refund for Order #900", "", -16.91m));
        Assert.DoesNotContain(Assert.Single(PnLBuilder.Build(data)).Issues, i => i.Message.Contains("#900"));

        var notCancelled = Month(L("Sale", "Payment for Order #901", "", 10m));
        Assert.Contains(Assert.Single(PnLBuilder.Build(notCancelled)).Issues, i => i.Message.Contains("#901"));
    }

    [Fact]
    public void Order_profit_uses_statement_values_and_matches_label()
    {
        var p = Assert.Single(OrderReport.Build(Month()));
        Assert.True(p.AdAttributed);
        Assert.Equal(-7.14m, p.Label);
        Assert.False(p.LabelShared);
        Assert.Equal(-0.40m, p.ListingFees);
        Assert.Equal(43.25m - 4.11m - 2.08m - 0.46m - 1.55m - 5.87m - 0.40m - 7.14m - 6m, p.Profit);
    }

    [Fact]
    public void Labels_shared_evenly_when_two_orders_ship_the_same_day()
    {
        var d = new DateOnly(2026, 10, 5);
        var data = new ShopData
        {
            Statement =
            [
                new StatementLine { Date = d, Type = "Shipping", Title = "USPS shipping label", LabelId = "1", Net = -5.00m },
                new StatementLine { Date = d, Type = "Shipping", Title = "USPS shipping label", LabelId = "2", Net = -6.01m },
            ],
            Orders = [new Order { OrderId = "1", DateShipped = d }, new Order { OrderId = "2", DateShipped = d }],
        };
        var a = OrderReport.AllocateLabels(data);
        Assert.Equal(-11.01m, a["1"].Amount + a["2"].Amount);
        Assert.True(a["1"].Shared);
    }
}
