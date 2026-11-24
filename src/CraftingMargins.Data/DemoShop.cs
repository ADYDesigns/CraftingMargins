// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Data;

/// <summary>
/// A made-up 3D-printing shop for trying the app (demo mode) and for screenshots.  Nothing
/// here comes from a real shop: products, prices, order numbers and dates are invented.
/// The patterns are realistic for a small Etsy shop: mostly one-unit orders, about half
/// through Offsite Ads, labels close to the shipping charged, a few free-shipping items, the
/// odd refund, label adjustment and coupon, steady sales with slow growth and a small
/// Christmas bump.  Every fee is worked out with the app's own fee math, so the self-test
/// passes.  It runs from January of last year to today.  A few things are left for Needs attention on purpose: a listing with no cost
/// rule, a rule that adds up to $0, a part with no print time and a personalization field.
/// Same seed and same "today" = the same shop.
/// </summary>
public static class DemoShop
{
    public const string ShopName = "Demo Shop";
    public const string SourceFile = "Demo data";
    private const decimal HardwarePiece = 0.05m;

    private sealed record Variant(string Value, decimal Price, long PartId, decimal PartQty);

    private sealed record Product(
        string Id, string Title, string? OptionName, Variant[] Variants,
        decimal Shipping, decimal Label, double Popularity, bool Christmas = false);

    // Parts: id, name, material (1 PLA, 2 PETG, null = hardware only), grams, print minutes, hardware $.
    private static readonly (long Id, string Name, long? Mat, decimal Grams, decimal Minutes, decimal Hardware)[] PartList =
    [
        (1, "Phone stand", 1, 62, 150, 0),
        (2, "Headphone stand", 1, 145, 330, 0),
        (3, "Plant pot, small", 2, 70, 180, 0),
        (4, "Plant pot, medium", 2, 120, 290, 0),
        (5, "Plant pot, large", 2, 210, 480, 0),
        (6, "Cable clip", 1, 3, 8, 0),
        (7, "Drawer tray", 1, 160, 360, 0),
        (8, "Wall hook", 2, 18, 45, 0.15m),
        (9, "Coaster", 1, 22, 50, 0.10m),
        (10, "Bookend", 1, 110, 260, 0.60m),
        (11, "Pen cup", 1, 85, 0, 0),             // print time never entered: shows on Needs attention
        (12, "Controller holder", 1, 95, 230, 0),
        (13, "Under-desk hook", 2, 40, 95, 0.25m),
        (14, "Bag clip", 1, 4, 10, 0),
        (15, "Snowflake ornament", 1, 9, 25, 0.05m),
    ];

    private static readonly Product[] Products =
    [
        new("7100001", "Minimalist Phone Stand", null, [new("", 14m, 1, 1)], 5.25m, 5.25m, 10),
        new("7100002", "Headphone Stand", "Color", [new("Black", 26m, 2, 1), new("White", 26m, 2, 1), new("Gray", 26m, 2, 1)], 7.10m, 7.10m, 6),
        new("7100003", "Geometric Plant Pot with Drainage", "Size", [new("Small", 12m, 3, 1), new("Medium", 18m, 4, 1), new("Large", 26m, 5, 1)], 6.40m, 6.40m, 9),
        new("7100004", "Cable Management Clips", "Pack", [new("6-Pack", 8m, 6, 1), new("12-Pack", 13m, 6, 1)], 0m, 4.10m, 8),
        new("7100005", "Desk Drawer Organizer Tray", null, [new("", 22m, 7, 1)], 6.90m, 6.90m, 4),
        new("7100006", "Adhesive Wall Hooks, Set of 4", null, [new("", 10m, 8, 4)], 4.75m, 4.75m, 7),
        new("7100007", "Coaster Set of 4", "Color", [new("Black", 18m, 9, 4), new("White", 18m, 9, 4)], 5.80m, 5.80m, 5),
        new("7100008", "Magnetic Bookends, Pair", null, [new("", 28m, 10, 2)], 7.60m, 7.60m, 3),
        new("7100009", "Pen Cup", null, [new("", 12m, 11, 1)], 5.10m, 5.10m, 4),
        new("7100010", "Game Controller Holder", null, [new("", 19m, 12, 1)], 6.20m, 6.20m, 5),
        new("7100011", "Under-Desk Headphone Hook", null, [new("", 11m, 13, 1)], 4.60m, 4.60m, 4),
        new("7100012", "Bag Clips, 10 Pack", null, [new("", 9m, 14, 10)], 0m, 3.90m, 6),
        new("7100013", "Snowflake Christmas Ornament", null, [new("", 7m, 15, 1)], 4.20m, 4.20m, 0.5, Christmas: true),
        new("7100014", "Dice Tower", null, [new("", 32m, 0, 0)], 8.40m, 8.40m, 2),                 // no cost rule: Needs attention
        new("7100015", "Custom Name Keychain", null, [new("", 9m, 0, 0)], 4.30m, 4.30m, 3),       // rule with no parts: costs $0
    ];

    /// <summary>Fills an empty database (and the settings) with the demo shop.</summary>
    public static void Build(Database db, AppSettings settings, DateOnly today, int seed = 20261119)
    {
        var rnd = new Random(seed);
        var fees = new FeeRates();
        var start = new DateOnly(today.Year - 1, 1, 1);   // all of last year, so year over year has something to compare
        var statementEnd = today.AddDays(-2);

        settings.ShopName = ShopName;
        settings.ProfitFloor = 5m;
        settings.CheckForUpdates = false;
        settings.Craft = Craft.Printing3D;
        settings.TrackMachineTime = true;

        // ----- cost model -----
        db.SaveMaterials(
        [
            new Material { Id = 1, Name = "PLA", Prices = [new(new(2000, 1, 1), 0.018m)] },
            new Material { Id = 2, Name = "PETG", Prices = [new(new(2000, 1, 1), 0.022m)] },
            // Bought by the piece: a component.  The hardware amounts in PartList are dollars, so they become pieces at 5 cents each.
            new Material { Id = 3, Name = "Small hardware (magnets, pads)", Unit = Units.Each, Prices = [new(new(2000, 1, 1), HardwarePiece)] },
        ]);
        db.SaveParts([.. PartList.Select(p =>
        {
            var lines = new List<PartLine>();
            if (p.Mat is long mat && p.Grams > 0) lines.Add(new PartLine(mat, p.Grams));
            if (p.Hardware > 0) lines.Add(new PartLine(3, p.Hardware / HardwarePiece));
            return new Part
            {
                Id = p.Id, Name = p.Name, Versions = [new(new(2000, 1, 1), lines, p.Minutes)],
                Notes = p.Minutes == 0 ? "Print time not measured yet." : "",
            };
        })]);
        var rules = new List<MappingRule>();
        foreach (var pr in Products.Where(p => p.Id != "7100014"))
        {
            if (pr.Id == "7100015") { rules.Add(new MappingRule { ListingId = pr.Id, Name = "Keychain (cost not filled in yet)" }); continue; }
            foreach (var v in pr.Variants.GroupBy(v => (v.PartId, v.PartQty)))
            {
                var r = new MappingRule { ListingId = pr.Id, Name = pr.OptionName is null ? "Every sale" : string.Join(" / ", v.Select(x => x.Value)) };
                if (pr.OptionName is not null && pr.Variants.Select(x => (x.PartId, x.PartQty)).Distinct().Count() > 1)
                    r.Conditions[pr.OptionName] = v.First().Value;
                r.Parts.Add(pr.Id == "7100004"
                    ? new RulePart(v.Key.PartId, 1, MultiplyByVariation: "Pack")      // 6-Pack → 6 clips
                    : new RulePart(v.Key.PartId, v.Key.PartQty));
                rules.Add(r);
            }
        }
        db.SaveRules(rules);
        db.SaveCostSettings([new CostSettings { MachineHourRate = 0.12m, WastePercent = 0.05m, PackagingPerOrder = 0.45m }]);
        db.SaveFeeRates([fees]);
        db.SaveListingPackaging(new() { ["7100008"] = 1.20m });   // bookends ship in their own box

        db.ReplaceListings([.. Products.Select(p => new Listing
        {
            Title = p.Title, ListingId = p.Id, State = "active", Price = p.Variants.Min(v => v.Price), Quantity = 25,
            NumFavorers = rnd.Next(5, 140),
            Variations = p.OptionName is null ? [] : [new ListingVariation(p.OptionName, [.. p.Variants.Select(v => v.Value)])],
            Offerings = [.. p.Variants.Select(v => new ListingOffering(
                p.OptionName is null ? [] : new Dictionary<string, string> { [p.OptionName] = v.Value }, v.Price, 25, true))],
        })]);

        // ----- orders, items and their statement lines -----
        var orders = new List<Order>();
        var items = new List<OrderItem>();
        var lines = new List<StatementLine>();
        long orderNo = 3_100_000_000, txNo = 4_200_000_000, labelNo = 180_000_000;
        var keychains = 0;
        void Line(DateOnly d, string type, string title, string info, decimal amount, decimal feesAndTaxes)
        {
            if (d > statementEnd) return;
            var l = new StatementLine { Date = d, Type = type, Title = title, Info = info, Amount = amount, FeesAndTaxes = feesAndTaxes, Net = amount + feesAndTaxes, SourceFile = SourceFile };
            StatementIds.Fill(l);
            lines.Add(l);
        }

        for (var day = start; day <= today; day = day.AddDays(1))
        {
            var monthsIn = (day.Year - start.Year) * 12 + day.Month - start.Month;
            var christmas = (day.Month == 11 && day.Day >= 20) || (day.Month == 12 && day.Day <= 20);
            var rate = 0.75 * Math.Pow(1.025, monthsIn) * (christmas ? 1.35 : 1.0);
            var count = Poisson(rnd, rate);
            for (int n = 0; n < count; n++)
            {
                var id = (++orderNo).ToString();
                var picks = new List<(Product P, Variant V, int Qty)>();
                var distinct = rnd.NextDouble() < 0.08 ? 2 : 1;
                while (picks.Count < distinct)
                {
                    var p = Pick(rnd, christmas);
                    if (picks.Any(x => x.P == p)) continue;
                    var qty = rnd.NextDouble() switch { < 0.82 => 1, < 0.96 => 2, _ => 3 };
                    picks.Add((p, p.Variants[rnd.Next(p.Variants.Length)], qty));
                }
                var units = picks.Sum(x => x.Qty);
                var itemTotal = picks.Sum(x => x.V.Price * x.Qty);
                var coupon = rnd.NextDouble() < 0.07;
                var discount = coupon ? Money.RoundFee(itemTotal * 0.10m) : 0m;
                var charged = picks.Max(x => x.P.Shipping);
                var shipping = charged == 0 ? 0 : charged + 1.00m * (units - 1);
                var taxable = rnd.NextDouble() < 0.75;
                var tax = taxable ? Money.RoundFee((itemTotal - discount + shipping) * (decimal)(0.06 + rnd.NextDouble() * 0.03)) : 0m;
                var ad = rnd.NextDouble() < 0.5;
                var sale = itemTotal - discount + shipping + tax;
                var f = FeeCalculator.Compute(fees, itemTotal - discount, shipping, tax, units, ad);
                var shipped = day.AddDays(rnd.Next(1, 4));

                orders.Add(new Order
                {
                    OrderId = id, SaleDate = day, DateShipped = shipped <= today ? shipped : null, NumberOfItems = units,
                    OrderValue = itemTotal, DiscountAmount = discount, Shipping = shipping, SalesTax = tax, OrderTotal = sale,
                    Status = "Completed", CardProcessingFees = f.Processing, OrderNet = sale - f.Processing, OrderType = "online",
                });
                foreach (var (p, v, qty) in picks)
                {
                    // The keychain carries buyer-typed text (a different name on every order), which
                    // Needs attention flags as possible personalization.
                    var vars = p.OptionName is null ? "" : $"{p.OptionName}:{v.Value}";
                    if (p.Id == "7100015") { vars = $"Name on keychain:{DemoNames[keychains % DemoNames.Length]} {(char)('A' + keychains / DemoNames.Length % 26)}."; keychains++; }
                    items.Add(new OrderItem
                    {
                        TransactionId = (++txNo).ToString(), OrderId = id, ListingId = p.Id, SaleDate = day,
                        DateShipped = shipped <= today ? shipped : null, ItemName = p.Title, Quantity = qty, Price = v.Price,
                        DiscountAmount = coupon ? Money.RoundFee(v.Price * qty * 0.10m) : 0, ItemTotal = v.Price * qty,
                        Variations = vars, CouponCode = coupon ? "SAVE10" : "",
                    });
                }

                // Statement lines, the way Etsy writes them.
                Line(day, "Sale", $"Payment for Order #{id}", "", sale, 0);
                if (tax > 0) Line(day, "Tax", "Sales tax paid by buyer", $"Order #{id}", 0, -tax);
                var itemFeeLeft = f.TransactionItem;
                for (int k = 0; k < picks.Count; k++)
                {
                    var part = k == picks.Count - 1 ? itemFeeLeft : Money.RoundFee(fees.TransactionRate * (picks[k].V.Price * picks[k].Qty - (coupon ? Money.RoundFee(picks[k].V.Price * picks[k].Qty * 0.10m) : 0)));
                    itemFeeLeft -= part;
                    Line(day, "Fee", $"Transaction fee: {picks[k].P.Title}", $"Order #{id}", 0, -part);
                }
                if (f.TransactionShipping > 0) Line(day, "Fee", "Transaction fee: Shipping", $"Order #{id}", 0, -f.TransactionShipping);
                Line(day, "Fee", "Processing fee", $"Order #{id}", 0, -f.Processing);
                if (ad) Line(day, "Marketing", "Fee for sale made through Offsite Ads", $"Order #{id}", 0, -f.OffsiteAds);
                foreach (var (p, _, qty) in picks)
                    for (int u = 0; u < qty; u++) Line(day, "Fee", "Listing fee", $"Listing #{p.Id}", 0, -fees.ListingFee);

                // The label: what the buyer paid when Etsy calculated it, otherwise what USPS charged.
                var label = units == 1 && picks.Count == 1 && charged > 0 && rnd.NextDouble() < 0.85
                    ? shipping
                    : Money.RoundFee(picks.Max(x => x.P.Label) + 0.80m * (units - 1) + (decimal)(rnd.NextDouble() * 0.6 - 0.3));
                var lab = (++labelNo).ToString();
                Line(shipped, "Shipping", "USPS shipping label", $"Label #{lab}", 0, -label);
                if (rnd.NextDouble() < 0.03)
                    Line(shipped.AddDays(rnd.Next(3, 9)), "Shipping", "Adjustment for USPS shipping label", $"Label #{lab}", 0, -Money.RoundFee((decimal)(0.5 + rnd.NextDouble())));

                // Now and then a partial refund (Etsy credits the transaction fee, not processing).
                if (rnd.NextDouble() < 0.02 && day.AddDays(5) <= statementEnd)
                {
                    var refund = Money.RoundFee((itemTotal - discount) * 0.5m);
                    var refundDay = day.AddDays(rnd.Next(5, 12));
                    Line(refundDay, "Refund", $"Refund for Order #{id}", "", -refund, 0);
                    Line(refundDay, "Fee", "Credit for transaction fee", $"Order #{id}", 0, Money.RoundFee(fees.TransactionRate * refund));
                    orders[^1].AdjustedCardProcessingFees = f.Processing;
                }
            }
            // Money goes to the bank on the first of the month (excluded from profit).
            if (day.Day == 1 && day > start) { var dep = rnd.Next(150, 900); Line(day, "Deposit", $"${dep}.00 sent to your bank account", "", -dep, 0); }
        }

        // ----- sales outside Etsy: a spring craft fair and a holiday market -----
        var fairs = new[] { (Date: FirstSaturday(start.AddMonths(5)), Name: "Spring craft fair"), (Date: LastDecemberSaturday(start, today), Name: "Holiday market") };
        var m = 0;
        foreach (var (date, name) in fairs.Where(x => x.Date <= today))
            for (int k = 0; k < 8; k++)
            {
                var p = Pick(rnd, name == "Holiday market");
                var v = p.Variants[rnd.Next(p.Variants.Length)];
                var id = $"M-{++m}";
                var fee = Money.RoundFee(v.Price * 0.026m + 0.15m);   // card reader
                orders.Add(new Order { OrderId = id, Source = Order.OtherSource, OrderType = name, SaleDate = date, NumberOfItems = 1, OrderValue = v.Price, CardProcessingFees = fee, OrderTotal = v.Price, OrderNet = v.Price - fee });
                items.Add(new OrderItem
                {
                    TransactionId = id + "-1", OrderId = id, ListingId = p.Id, SaleDate = date, ItemName = p.Title, Quantity = 1, Price = v.Price, ItemTotal = v.Price,
                    Variations = p.OptionName is null || p.Id == "7100015" ? "" : $"{p.OptionName}:{v.Value}",
                });
            }

        db.UpsertOrders(orders);
        db.UpsertItems(items);
        db.ReplaceStatementMonths([.. lines.Select(l => l.Month).Distinct()], lines);

        // ----- expense log -----
        var expenses = new List<Expense>();
        for (var mo = start; mo <= today; mo = mo.AddMonths(1))
        {
            expenses.Add(new Expense { Date = mo.AddDays(3), Amount = 19.99m * rnd.Next(2, 4), Category = "Filament", Vendor = "Filament supplier", Note = "PLA and PETG spools" });
            if (mo.Month % 3 == 1) expenses.Add(new Expense { Date = mo.AddDays(9), Amount = 24.50m, Category = "Packaging supplies", Vendor = "Packaging store", Note = "Mailers and tissue" });
            if (mo.Month % 2 == 0) expenses.Add(new Expense { Date = mo.AddDays(14), Amount = 12.90m, Category = "Hardware", Vendor = "Hardware store", Note = "Magnets, felt pads, screws" });
            expenses.Add(new Expense { Date = mo.AddDays(20), Amount = 9.99m, Category = "Software/subscriptions", Vendor = "Design software", Note = "Monthly plan" });
        }
        expenses.Add(new Expense { Date = start.AddMonths(2).AddDays(11), Amount = 349m, Category = "Equipment", Vendor = "Printer maker", Note = "Second 3D printer" });
        db.SaveExpenses([.. expenses.Where(e => e.Date <= today)]);
    }

    private static readonly string[] DemoNames = ["Sam", "Alex", "Jordan", "Taylor", "Casey", "Riley", "Morgan", "Jamie"];

    private static Product Pick(Random rnd, bool christmas)
    {
        double W(Product p) => p.Christmas ? (christmas ? 12 : p.Popularity) : p.Popularity;
        var total = Products.Sum(W);
        var x = rnd.NextDouble() * total;
        foreach (var p in Products) { x -= W(p); if (x <= 0) return p; }
        return Products[0];
    }

    private static int Poisson(Random rnd, double lambda)
    {
        var l = Math.Exp(-lambda);
        int k = 0;
        double p = 1;
        do { k++; p *= rnd.NextDouble(); } while (p > l);
        return k - 1;
    }

    private static DateOnly FirstSaturday(DateOnly month)
    {
        var d = new DateOnly(month.Year, month.Month, 1);
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
        return d;
    }

    /// <summary>The second Saturday of the latest December in the demo period (or before it started: none).</summary>
    private static DateOnly LastDecemberSaturday(DateOnly start, DateOnly today)
    {
        var year = today.Month == 12 && today.Day >= 14 ? today.Year : today.Year - 1;
        var d = FirstSaturday(new DateOnly(year, 12, 1)).AddDays(7);
        return d < start ? DateOnly.MaxValue : d;
    }
}
