// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Tests;

/// <summary>Matching shipping labels to orders when they aren't bought on the ship date.</summary>
public class LabelTests
{
    private static readonly DateOnly Sun = new(2026, 11, 22), Mon = new(2026, 11, 23), Tue = new(2026, 11, 24);

    private static StatementLine Label(DateOnly d, string id, decimal net, string title = "USPS shipping label")
    {
        var l = new StatementLine { Date = d, Type = "Shipping", Title = title, Info = $"Label #{id}", Amount = 0, FeesAndTaxes = net, Net = net };
        StatementIds.Fill(l);
        return l;
    }

    private static Order Shipped(string id, DateOnly sold, DateOnly? shipped, decimal shipping = 0) =>
        new() { OrderId = id, SaleDate = sold, DateShipped = shipped, Shipping = shipping };

    [Fact]
    public void Label_bought_two_days_before_shipping_goes_to_that_order()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun.AddDays(-1), Tue)],
            Statement = [Label(Sun, "1", -5.00m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal((-5.00m, false), a.ByOrder["A"]);
        Assert.Empty(a.Unmatched);
    }

    [Fact]
    public void Early_label_is_not_given_to_orders_that_shipped_the_day_it_was_bought()
    {
        // Sunday: order A ships with its own label, and the label for B (shipping Tuesday) is bought too.
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun.AddDays(-2), Sun), Shipped("B", Sun.AddDays(-1), Tue)],
            Statement = [Label(Sun, "1", -5.00m), Label(Sun, "2", -5.00m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal(-5.00m, a.ByOrder["A"].Amount);
        Assert.Equal(-5.00m, a.ByOrder["B"].Amount);
        Assert.Empty(a.Unmatched);
    }

    [Fact]
    public void Early_label_is_shared_with_a_same_day_label_when_two_orders_ship_together()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun.AddDays(-1), Tue), Shipped("B", Mon, Tue)],
            Statement = [Label(Sun, "1", -4.00m), Label(Tue, "2", -6.00m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal(-10.00m, a.ByOrder["A"].Amount + a.ByOrder["B"].Amount);
        Assert.True(a.ByOrder["A"].Shared);
        Assert.Empty(a.Unmatched);
    }

    [Fact]
    public void Exact_amount_match_works_across_days()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun.AddDays(-1), Tue, 7.14m), Shipped("B", Sun.AddDays(-1), Tue, 4.50m)],
            Statement = [Label(Sun, "1", -4.50m), Label(Mon, "2", -7.14m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal((-7.14m, false), a.ByOrder["A"]);
        Assert.Equal((-4.50m, false), a.ByOrder["B"]);
    }

    [Fact]
    public void Adjustment_follows_its_label_whatever_day_it_lands()
    {
        var d1 = new DateOnly(2026, 10, 1);
        var d9 = new DateOnly(2026, 10, 9);
        var data = new ShopData
        {
            Orders = [Shipped("A", d1, d1, 5.00m), Shipped("B", d9, d9, 6.00m)],
            Statement =
            [
                Label(d1, "111", -5.00m),
                Label(d9, "222", -6.00m),
                Label(d9, "111", -1.25m, "Adjustment for USPS shipping label"),   // USPS re-measured A's package
            ],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal((-6.25m, false), a.ByOrder["A"]);
        Assert.Equal((-6.00m, false), a.ByOrder["B"]);
        Assert.Equal(data.Statement.Sum(s => s.Net), a.ByOrder.Values.Sum(v => v.Amount));
    }

    [Fact]
    public void Refunded_label_is_charged_to_nobody()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun, Mon)],
            Statement = [Label(Sun, "1", -5.00m), Label(Mon, "1", 5.00m, "Credit for USPS shipping label"), Label(Mon, "2", -5.50m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal((-5.50m, false), a.ByOrder["A"]);
        Assert.Empty(a.Unmatched);
    }

    [Fact]
    public void Label_with_no_possible_order_is_reported_as_unmatched()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Tue, Tue)],                       // sold after the label was bought
            Statement = [Label(Sun, "1", -5.00m), Label(Tue, "9", -0.75m, "Adjustment for USPS shipping label")],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Empty(a.ByOrder);
        Assert.Equal(-5.75m, a.UnmatchedTotal);
    }

    [Fact]
    public void Label_bought_for_an_order_not_marked_shipped_yet_goes_to_it()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun.AddDays(-1), null)],
            Statement = [Label(Sun, "1", -5.00m)],
        };
        Assert.Equal((-5.00m, false), OrderReport.AllocateLabelsDetailed(data).ByOrder["A"]);
    }

    [Fact]
    public void Two_labels_for_one_order_both_count()
    {
        var data = new ShopData
        {
            Orders = [Shipped("A", Sun, Mon)],
            Statement = [Label(Mon, "1", -5.00m), Label(Mon, "2", -6.00m)],
        };
        var a = OrderReport.AllocateLabelsDetailed(data);
        Assert.Equal(-11.00m, a.ByOrder["A"].Amount);
        Assert.True(a.ByOrder["A"].Shared);
    }
}
