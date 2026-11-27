// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Tests;

/// <summary>The pricing check shows combinations that earn exactly the same as one row.</summary>
public class PricingCombineTests
{
    private static PriceRow Row(string size, string color, decimal price = 14m, decimal cost = 1.65m, decimal worst = 4.91m) => new()
    {
        ListingId = "100", Title = "Tool holder", Options = Variations.Format([new("Size", size), new("Color", color)]),
        ListPrice = price, Price = price, Shipping = 7.5m, Label = 7.5m, Cost = cost, Organic = worst + 2, WorstCase = worst, MinPrice = 12m,
        BelowFloor = worst < 5, PriceSource = "Etsy",
    };

    [Fact]
    public void Colors_at_one_price_and_cost_become_one_row()
    {
        var rows = PricingCheck.Combine([Row("2 tools", "White"), Row("2 tools", "Silver"), Row("2 tools", "Black"),
                                         Row("4 tools", "White", 16.5m, 1.65m, 8.52m), Row("4 tools", "Silver", 16.5m, 1.65m, 8.52m), Row("4 tools", "Black", 16.5m, 1.65m, 8.52m)]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Size: 2 tools; Color: White, Silver or Black", rows[0].OptionsText);
        Assert.Equal((3, 4.91m), (rows[0].Combinations, rows[0].WorstCase));
        Assert.Equal("Size: 4 tools; Color: White, Silver or Black", rows[1].OptionsText);
    }

    [Fact]
    public void A_combination_that_earns_differently_keeps_its_own_row()
    {
        var rows = PricingCheck.Combine([Row("2 tools", "White"), Row("2 tools", "Silver"), Row("2 tools", "Gold", 18m, 1.65m, 7.10m)]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Size: 2 tools; Color: White or Silver", rows[0].OptionsText);
        Assert.Equal(("Size: 2 tools, Color: Gold", 1), (rows[1].OptionsText, rows[1].Combinations));
    }

    [Fact]
    public void A_row_only_ever_stands_for_combinations_that_exist()
    {
        // Same result everywhere, but Black only comes in Small: never shown as "Small or Large; White or Black".
        var rows = PricingCheck.Combine([Row("Small", "White"), Row("Small", "Black"), Row("Large", "White")]);
        Assert.Equal(3, rows.Sum(r => r.Combinations));
        Assert.Equal(2, rows.Count);
        foreach (var r in rows.Where(r => r.Grouped is not null))
            Assert.True(r.OptionsText.Count(c => c == ',') + r.OptionsText.Split(" or ").Length - 1 <= 1, r.OptionsText);   // only one option lists several values
    }

    [Fact]
    public void A_part_for_an_uncosted_option_gets_a_rule_for_just_that_option()
    {
        var row = new PriceRow { ListingId = "300", Title = "Vinyl Roll Holder - Stackable Modular Storage Rack", Options = "Size:4 x 6 (24 rolls)", Price = 82m, Problem = "No rule matches these variations" };
        Assert.True(row.HasNoRule);
        var (part, rule) = PricingCheck.NewPartForOption(row, ["Vinyl Roll Holder – 4 x 4"], 3);
        Assert.Equal("Vinyl Roll Holder – 4 x 6 (24 rolls)", part.Name);
        Assert.Equal(new DateOnly(2000, 1, 1), part.Versions.Single().EffectiveFrom);   // covers every past sale
        Assert.Equal(("300", "4 x 6 (24 rolls)", 3), (rule.ListingId, rule.Name, rule.SortOrder));
        Assert.Equal("4 x 6 (24 rolls)", Assert.Single(rule.Conditions, c => c.Key == "Size").Value);
        // The name is never one a part already has.
        Assert.Equal("Vinyl Roll Holder – 4 x 6 (24 rolls) (2)", PricingCheck.NewPartForOption(row, [part.Name], 4).Part.Name);
    }

    [Fact]
    public void A_grouped_row_gets_a_rule_that_leaves_out_the_merged_option()
    {
        var noRule = PricingCheck.Combine([
            new PriceRow { ListingId = "100", Title = "Tool holder", Options = Variations.Format([new("Size", "6 tools"), new("Color", "White")]), Price = 19m, Problem = "No rule matches these variations" },
            new PriceRow { ListingId = "100", Title = "Tool holder", Options = Variations.Format([new("Size", "6 tools"), new("Color", "Black")]), Price = 19m, Problem = "No rule matches these variations" }]);
        var row = Assert.Single(noRule);
        var (_, rule) = PricingCheck.NewPartForOption(row, [], 0);
        Assert.Equal(["Size"], rule.Conditions.Keys);                      // every color
        Assert.Equal("6 tools", rule.Name);
    }

    [Fact]
    public void Only_an_option_with_no_rule_at_all_can_have_a_part_made()
    {
        Assert.False(Row("2 tools", "White").HasNoRule);                                                   // costed, just below the goal
        Assert.False(new PriceRow { Problem = "\"PLA\" (used by part \"Cup\") has no price" }.HasNoRule);  // a rule with a problem
        Assert.True(new PriceRow { Problem = "No rules for this listing" }.HasNoRule);
    }

    [Fact]
    public void A_listing_without_options_is_left_alone()
    {
        var single = new PriceRow { ListingId = "200", Title = "Cover", Price = 16m, Cost = 1.41m, WorstCase = 8.38m };
        var row = Assert.Single(PricingCheck.Combine([single]));
        Assert.Equal(("(no options)", 1), (row.OptionsText, row.Combinations));
    }
}
