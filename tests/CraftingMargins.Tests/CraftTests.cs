// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Tests;

/// <summary>
/// The cost model isn't tied to one craft: materials have their own unit, a part is a recipe
/// of several materials, and time can be a machine's or the maker's own.
/// </summary>
public class CraftTests
{
    private static readonly DateOnly Day = new(2026, 11, 1);
    private static readonly DateOnly Start = new(2000, 1, 1);

    /// <summary>An 8 oz candle: 7 oz of wax, 0.6 oz of fragrance, one wick, one jar, 15 minutes of the maker's time.</summary>
    private static CostModel CandleShop(decimal waste = 0, decimal ownRate = 0) => new()
    {
        Materials =
        [
            new Material { Id = 1, Name = "Soy wax", Unit = "oz", Prices = [new(Start, 0.20m)] },
            new Material { Id = 2, Name = "Fragrance oil", Unit = "oz", Prices = [new(Start, 1.50m)] },
            new Material { Id = 3, Name = "Wick", Unit = Units.Each, Prices = [new(Start, 0.12m)] },
            new Material { Id = 4, Name = "Jar with lid", Unit = Units.Each, Prices = [new(Start, 1.35m)] },
        ],
        Parts =
        [
            new Part
            {
                Id = 1, Name = "8 oz candle",
                Versions = [new(Start, [new(1, 7m), new(2, 0.6m), new(3, 1m), new(4, 1m)], MachineMinutes: 0, OwnMinutes: 15)],
            },
        ],
        Rules = [new MappingRule { ListingId = "C1", Name = "Candle", Parts = [new(1, 1)] }],
        CostSettings = [new CostSettings { WastePercent = waste, OwnHourRate = ownRate }],
    };

    [Fact]
    public void A_candle_is_costed_from_several_materials_in_their_own_units()
    {
        var c = CandleShop().CostOf("C1", "Scent:Lavender", 2, Day);
        Assert.True(c.Mapped);
        Assert.Equal(2 * (7m * 0.20m + 0.6m * 1.50m), c.Material);       // wax and oil, by the ounce
        Assert.Equal(2 * (0.12m + 1.35m), c.Components);                  // wick and jar, by the piece
        Assert.Equal(0m, c.MachineHours);
        Assert.Equal(0.5m, c.OwnHours);                                   // 2 × 15 minutes
        Assert.Equal(0m, c.OwnCost);                                      // no rate set: the hours are known, nothing is charged
        Assert.Equal(c.Material + c.Components, c.Total);
    }

    [Fact]
    public void Waste_applies_to_materials_but_not_to_things_bought_by_the_piece()
    {
        var c = CandleShop(waste: 0.10m).CostOf("C1", "", 1, Day);
        Assert.Equal((7m * 0.20m + 0.6m * 1.50m) * 0.10m, c.Waste);       // 10% of wax and oil only
        Assert.Equal(0.12m + 1.35m, c.Components);                        // you don't spill 10% of a jar
    }

    [Fact]
    public void Your_own_time_is_a_cost_only_when_you_give_it_a_rate()
    {
        var c = CandleShop(ownRate: 20m).CostOf("C1", "", 1, Day);
        Assert.Equal(0.25m, c.OwnHours);
        Assert.Equal(5m, c.OwnCost);                                      // 15 minutes at $20 an hour
        Assert.Equal(c.Material + c.Components + 5m, c.Total);
        Assert.Equal(0.25m, c.MakingHours);                               // what the per-hour figures divide by
    }

    [Fact]
    public void Own_time_counts_in_the_monthly_profit_but_never_in_the_tax_total()
    {
        var data = new ShopData
        {
            Costs = CandleShop(ownRate: 20m),
            Orders = [new Order { OrderId = "M-1", Source = Order.OtherSource, SaleDate = Day, OrderValue = 30m }],
            Items = [new OrderItem { TransactionId = "t", OrderId = "M-1", ListingId = "C1", SaleDate = Day, Quantity = 1, Price = 30m }],
        };
        var month = Assert.Single(PnLBuilder.Build(data));
        Assert.Equal(-5m, month.OwnCost);
        var materials = 7m * 0.20m + 0.6m * 1.50m + 0.12m + 1.35m;
        Assert.Equal(30m - materials - 5m, month.NetProfit);              // profit after paying yourself

        var tax = TaxReport.Build(2026, [month], [], ScheduleC.DefaultCategories(Craft.CandlesAndSoap), SuppliesBasis.Used);
        var own = tax.Lines.Single(l => l.Name == "Your own time");
        Assert.False(own.InTotal);                                        // a sole owner's labour isn't a business expense
        Assert.Equal(30m - materials, tax.NetProfit);
    }

    [Fact]
    public void A_material_removed_while_a_part_still_uses_it_is_reported_not_ignored()
    {
        var model = CandleShop();
        model.Materials.RemoveAll(m => m.Name == "Wick");
        var c = model.CostOf("C1", "", 1, Day);
        Assert.False(c.Mapped);
        Assert.Contains("no longer exists", c.Problem);
    }

    [Theory]
    [InlineData(Craft.Printing3D, "g", true, "Filament")]
    [InlineData(Craft.CandlesAndSoap, "oz", false, "Wax, oils and fragrance")]
    [InlineData(Craft.YarnAndFiber, "yd", false, "Yarn and fabric")]
    [InlineData(Craft.Other, "g", false, "Materials")]
    public void The_craft_sets_starting_points(Craft craft, string unit, bool machineTime, string category)
    {
        Assert.Equal(unit, Crafts.DefaultUnit(craft));
        Assert.Contains(unit, Units.All);
        Assert.Equal(machineTime, Crafts.UsesMachineTime(craft));
        var cats = ScheduleC.DefaultCategories(craft);
        Assert.Equal(category, cats[0].Name);
        Assert.True(cats[0].CountedPerSale && cats[1].CountedPerSale);    // materials and components are counted per sale
        Assert.Equal(cats.Count, cats.Select(c => c.Name).Distinct().Count());
    }
}
