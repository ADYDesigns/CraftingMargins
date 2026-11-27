// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

// Reports: sales per product, ad vs organic, shipping,
// pricing check / planned sale / profit per printer-hour, discounted orders.
// All of them read the same ShopData and OrderProfit list the other screens use.

/// <summary>"2026-03" → "Mar 2026" for display; anything else ("Total") is left as it is.</summary>
public static class Months
{
    public static string Name(string key) =>
        DateOnly.TryParseExact(key + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : key;
}

/// <summary>A date range for reports.  Null ends are open.</summary>
public sealed record Period(DateOnly? From, DateOnly? To)
{
    public static readonly Period All = new(null, null);
    public static Period Year(int y) => new(new DateOnly(y, 1, 1), new DateOnly(y, 12, 31));
    public bool Contains(DateOnly d) => (From is null || d >= From) && (To is null || d <= To);
}

// ---------------------------------------------------------------- report 3

/// <summary>One listing's sales.  Profit only counts orders whose money is on a loaded statement.</summary>
public sealed class ProductSales
{
    public string ListingId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Orders { get; set; }
    public int Units { get; set; }
    public int AdOrders { get; set; }
    public int OrdersWithProfit { get; set; }
    public decimal Profit { get; set; }
    public decimal? WorstOrder { get; set; }
    public string TopOptions { get; set; } = "";
    public decimal AdShare => Orders == 0 ? 0 : (decimal)AdOrders / Orders;
    public decimal? AverageProfit => OrdersWithProfit == 0 ? null : Math.Round(Profit / OrdersWithProfit, 2, MidpointRounding.AwayFromZero);
}

public static class ProductReport
{
    /// <summary>
    /// Orders with several listings split their profit between them by item value
    /// (price × quantity), or by units when every item is free.  The last listing takes
    /// the rounding remainder so the split always adds back up to the order's profit.
    /// </summary>
    public static List<ProductSales> Build(ShopData data, IEnumerable<OrderProfit> orders, Period period)
    {
        var items = data.Items.ToLookup(i => i.OrderId);
        var rows = new Dictionary<string, ProductSales>();
        var options = new Dictionary<string, Dictionary<string, int>>();

        foreach (var o in orders.Where(o => period.Contains(o.SaleDate)))
        {
            var its = items[o.OrderId].ToList();
            if (its.Count == 0) continue;
            var byListing = its.GroupBy(i => i.ListingId).ToList();
            var shares = ShareOut(o.OnStatement ? o.Profit : 0m, byListing.Select(g => (g.Sum(i => i.Price * i.Quantity), g.Sum(i => i.Quantity))).ToList());

            for (int k = 0; k < byListing.Count; k++)
            {
                var g = byListing[k];
                if (!rows.TryGetValue(g.Key, out var row))
                {
                    var name = data.Items.Where(i => i.ListingId == g.Key).OrderByDescending(i => i.SaleDate).First().ItemName;
                    rows[g.Key] = row = new ProductSales { ListingId = g.Key, Name = name };
                    options[g.Key] = new(StringComparer.OrdinalIgnoreCase);
                }
                row.Orders++;
                row.Units += g.Sum(i => i.Quantity);
                if (o.AdAttributed) row.AdOrders++;
                foreach (var i in g)
                {
                    var key = i.Variations.Length == 0 ? "(no options)" : i.Variations;
                    options[g.Key][key] = options[g.Key].GetValueOrDefault(key) + i.Quantity;
                }
                if (!o.OnStatement) continue;
                row.OrdersWithProfit++;
                row.Profit += shares[k];
                row.WorstOrder = row.WorstOrder is { } w ? Math.Min(w, shares[k]) : shares[k];
            }
        }

        foreach (var (id, row) in rows)
            row.TopOptions = string.Join(";  ", options[id].OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => $"{kv.Value}× {Readable(kv.Key)}"));
        return [.. rows.Values.OrderByDescending(r => r.Profit).ThenByDescending(r => r.Units)];
    }

    /// <summary>"Size:Large,Color:Red" → "Size: Large, Color: Red".</summary>
    private static string Readable(string vars)
    {
        var pairs = Variations.Parse(vars);
        return pairs.Count == 0 ? vars : string.Join(", ", pairs.Select(p => $"{p.Key}: {p.Value}"));
    }

    public static List<decimal> ShareOut(decimal total, List<(decimal Value, int Units)> parts)
    {
        var weights = parts.Sum(p => p.Value) > 0 ? parts.Select(p => p.Value).ToList() : parts.Select(p => (decimal)p.Units).ToList();
        var sum = weights.Sum();
        var result = new List<decimal>();
        for (int i = 0; i < parts.Count; i++)
            result.Add(i == parts.Count - 1
                ? total - result.Sum()
                : sum == 0 ? 0 : Math.Round(total * weights[i] / sum, 2, MidpointRounding.AwayFromZero));
        return result;
    }
}

// ---------------------------------------------------------------- report 4

/// <summary>Ad-attributed vs organic orders for one month, or a total row.</summary>
public sealed class AdRow
{
    public string Label { get; init; } = "";
    public int AdOrders { get; set; }
    public int OrganicOrders { get; set; }
    public decimal AdProfit { get; set; }
    public decimal OrganicProfit { get; set; }
    /// <summary>Offsite Ads fees on the statement (credits included), negative.</summary>
    public decimal OffsiteAdsPaid { get; set; }
    public decimal EtsyAdsPaid { get; set; }
    public int Orders => AdOrders + OrganicOrders;
    public decimal AdShare => Orders == 0 ? 0 : (decimal)AdOrders / Orders;
    public decimal? AdAverage => AdOrders == 0 ? null : Math.Round(AdProfit / AdOrders, 2, MidpointRounding.AwayFromZero);
    public decimal? OrganicAverage => OrganicOrders == 0 ? null : Math.Round(OrganicProfit / OrganicOrders, 2, MidpointRounding.AwayFromZero);
    public bool IsTotal { get; init; }
    public string MonthName => Months.Name(Label);
}

public static class AdReport
{
    /// <summary>Only orders on a loaded statement count (that's how an ad sale is recognized).  Last row = total.</summary>
    public static List<AdRow> Build(ShopData data, IEnumerable<OrderProfit> orders, Period period)
    {
        var months = new SortedDictionary<string, AdRow>();
        AdRow M(string key) => months.TryGetValue(key, out var m) ? m : months[key] = new AdRow { Label = key };

        foreach (var o in orders.Where(o => o.OnStatement && !o.IsOther && period.Contains(o.SaleDate)))
        {
            var m = M(o.SaleDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
            if (o.AdAttributed) { m.AdOrders++; m.AdProfit += o.Profit; }
            else { m.OrganicOrders++; m.OrganicProfit += o.Profit; }
        }
        foreach (var s in data.Statement.Where(s => period.Contains(s.Date)))
        {
            var cat = data.Categorizer.Categorize(s);
            if (cat == Category.OffsiteAds) M(s.Month).OffsiteAdsPaid += s.Net;
            else if (cat == Category.EtsyAds) M(s.Month).EtsyAdsPaid += s.Net;
        }
        var list = months.Values.ToList();
        list.Add(new AdRow
        {
            Label = "Total", IsTotal = true,
            AdOrders = list.Sum(r => r.AdOrders), OrganicOrders = list.Sum(r => r.OrganicOrders),
            AdProfit = list.Sum(r => r.AdProfit), OrganicProfit = list.Sum(r => r.OrganicProfit),
            OffsiteAdsPaid = list.Sum(r => r.OffsiteAdsPaid), EtsyAdsPaid = list.Sum(r => r.EtsyAdsPaid),
        });
        return list;
    }
}

// ---------------------------------------------------------------- report 5

/// <summary>Shipping charged vs what labels cost, by month.  Costs negative.</summary>
public sealed class ShippingRow
{
    public string Label { get; init; } = "";
    public int Orders { get; set; }
    public decimal Charged { get; set; }
    public decimal Labels { get; set; }
    /// <summary>USPS re-measured a package: "Adjustment for…" charges and "Credit for…" refunds.</summary>
    public decimal Adjustments { get; set; }
    public decimal ExtraShipping { get; set; }
    /// <summary>
    /// The part of Labels + Adjustments that couldn't be tied to an order (already counted in
    /// those two columns, so not part of Net).  Per-order profit is missing this much label cost.
    /// </summary>
    public decimal Unmatched { get; set; }
    public decimal Net => Charged + Labels + Adjustments + ExtraShipping;
    public bool IsTotal { get; init; }
    public string MonthName => Months.Name(Label);
}

public static class ShippingReport
{
    public static bool IsAdjustment(StatementLine s) =>
        s.Title.Contains("Adjustment", StringComparison.OrdinalIgnoreCase) || s.Title.Contains("Credit", StringComparison.OrdinalIgnoreCase);

    /// <summary>Charged and manual costs by sale month; label lines by statement month.  Last row = total.</summary>
    public static List<ShippingRow> Build(ShopData data, Period period, IEnumerable<UnmatchedLabel>? unmatched = null)
    {
        var months = new SortedDictionary<string, ShippingRow>();
        ShippingRow M(string key) => months.TryGetValue(key, out var m) ? m : months[key] = new ShippingRow { Label = key };

        foreach (var o in data.Orders.Where(o => period.Contains(o.SaleDate)))
        {
            var m = M(o.SaleDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
            m.Orders++;
            m.Charged += o.Shipping;
            m.ExtraShipping -= o.ExtraShippingCost;
        }
        foreach (var s in data.Statement.Where(s => period.Contains(s.Date) && data.Categorizer.Categorize(s) == Category.ShippingLabels))
        {
            if (IsAdjustment(s)) M(s.Month).Adjustments += s.Net;
            else M(s.Month).Labels += s.Net;
        }
        foreach (var u in (unmatched ?? []).Where(u => period.Contains(u.Date)))
            M(u.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).Unmatched += u.Amount;
        var list = months.Values.ToList();
        list.Add(new ShippingRow
        {
            Label = "Total", IsTotal = true, Orders = list.Sum(r => r.Orders), Charged = list.Sum(r => r.Charged),
            Labels = list.Sum(r => r.Labels), Adjustments = list.Sum(r => r.Adjustments), ExtraShipping = list.Sum(r => r.ExtraShipping),
            Unmatched = list.Sum(r => r.Unmatched),
        });
        return list;
    }

    /// <summary>Orders whose matched label cost more than the buyer paid for shipping.</summary>
    public static List<OrderProfit> LostOnShipping(IEnumerable<OrderProfit> orders, Period period) =>
        [.. orders.Where(o => period.Contains(o.SaleDate) && o.Label != 0 && o.ShippingCharged + o.Label + o.ExtraShipping < 0)
                  .OrderBy(o => o.ShippingCharged + o.Label + o.ExtraShipping)];
}

// ---------------------------------------------------------------- report 8

public static class DiscountReport
{
    public static bool IsDiscounted(OrderProfit o) => o.Discount != 0 || o.ShippingDiscount != 0 || o.Coupon.Length > 0;

    public static List<OrderProfit> Build(IEnumerable<OrderProfit> orders, Period period) =>
        [.. orders.Where(o => period.Contains(o.SaleDate) && IsDiscounted(o))];
}

// ---------------------------------------------------------------- year over year

/// <summary>One month (or the year so far) against the same period a year earlier.</summary>
public sealed record YearRow(string Label, int Orders, int LastOrders, decimal Sales, decimal LastSales, decimal Profit, decimal LastProfit, bool IsTotal = false)
{
    public decimal? SalesChange => LastSales == 0 ? null : (Sales - LastSales) / Math.Abs(LastSales);
    public decimal ProfitDifference => Profit - LastProfit;
}

public static class YearOverYear
{
    /// <summary>
    /// Each month of <paramref name="year"/> next to the same month of the year before, from the
    /// Monthly P&amp;L (sales = Etsy net sales + sales outside Etsy).  The total compares only the
    /// months that both years have, so a part year isn't measured against a whole one.
    /// </summary>
    public static List<YearRow> Build(IEnumerable<MonthlyPnL> pnl, int year)
    {
        var byMonth = pnl.ToDictionary(m => m.Month);
        var rows = new List<YearRow>();
        var withData = new List<YearRow>();
        for (int mo = 1; mo <= 12; mo++)
        {
            byMonth.TryGetValue($"{year}-{mo:00}", out var now);
            byMonth.TryGetValue($"{year - 1}-{mo:00}", out var then);
            if (now is null && then is null) continue;
            var row = new YearRow(new DateOnly(year, mo, 1).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture),
                now?.Orders ?? 0, then?.Orders ?? 0, Sales(now), Sales(then), now?.NetProfit ?? 0, then?.NetProfit ?? 0);
            rows.Add(row);
            if (now is not null && then is not null) withData.Add(row);
        }
        rows.Add(new YearRow(year < DateTime.Today.Year ? $"{year} (months in both years)" : $"{year} so far", withData.Sum(r => r.Orders), withData.Sum(r => r.LastOrders), withData.Sum(r => r.Sales),
            withData.Sum(r => r.LastSales), withData.Sum(r => r.Profit), withData.Sum(r => r.LastProfit), IsTotal: true));
        return rows;

        static decimal Sales(MonthlyPnL? m) => m is null ? 0 : m.NetSales + m.OtherSales;
    }
}

// ---------------------------------------------------------------- sales outside Etsy

/// <summary>
/// Items sold outside Etsy that aren't an Etsy listing get a made-up listing ID from their
/// name, so they show up in Products &amp; Costs and can have cost rules like any listing.
/// </summary>
public static class OtherItem
{
    public const string IdPrefix = "other:";
    public static string IdFor(string name) => IdPrefix + EtsyText.Clean(name).ToLowerInvariant();
}

// ---------------------------------------------------------------- price calculator

/// <summary>What one unit at a price earns, line by line (fees negative).</summary>
public sealed record PriceQuote(
    decimal Price, decimal Shipping, decimal Label, decimal Cost,
    decimal TransactionFee, decimal ProcessingFee, decimal ListingFee, decimal OffsiteAdsFee,
    decimal Organic, decimal WorstCase, decimal MinPrice, decimal MakingHours)
{
    public decimal? OrganicPerHour => MakingHours > 0 ? Money.Round(Organic / MakingHours) : null;
    public decimal EtsyFeesOrganic => TransactionFee + ProcessingFee + ListingFee;
}

public static class PriceCalculator
{
    /// <summary>
    /// One unit of a product that may not be listed yet: the quick estimate with each fee
    /// shown on its own.  Label null = assume it costs what the buyer pays for shipping.
    /// </summary>
    public static PriceQuote Quote(FeeRates r, decimal price, decimal shipping, decimal? label, decimal cost, decimal goal, decimal makingHours = 0)
    {
        var l = label ?? shipping;
        var tx = -Money.RoundFee(r.TransactionRate * (price + shipping));
        var proc = -Money.RoundFee(r.ProcessingRate * (price + shipping) + r.ProcessingFixed);
        var listing = -r.ListingFee;
        var ads = -Math.Min(Money.RoundFee(r.OffsiteAdsRate * (price + shipping)), r.OffsiteAdsCap);
        var organic = price + shipping - l - cost + tx + proc + listing;
        var min = Math.Ceiling(PricingEstimate.MinPriceForTarget(r, goal, shipping, cost, label) * 100) / 100;
        return new PriceQuote(price, shipping, l, cost, tx, proc, listing, ads, organic, organic + ads, Math.Max(min, 0), makingHours);
    }
}

// ---------------------------------------------------------------- reports 6, 7, 9

/// <summary>One buyable option of an active listing, priced.</summary>
public sealed class PriceRow
{
    public string ListingId { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>"Name:Value,Name:Value" (empty for a listing without options).</summary>
    public string Options { get; init; } = "";
    public string OptionsText => Grouped ?? (Options.Length == 0 ? "(no options)" : Variations.Readable(Options));
    /// <summary>
    /// Set when this row stands for several combinations that earn exactly the same (colors at one
    /// price, say): "Size: Large; Color: White, Silver or Black".  <see cref="Options"/> is then the first of them.
    /// </summary>
    public string? Grouped { get; set; }
    /// <summary>How many buyable combinations this row stands for.</summary>
    public int Combinations { get; set; } = 1;
    /// <summary>
    /// The options every combination in this row has (in a grouped row, the ones not merged:
    /// "Size: Large" but not the colors).  A cost rule with these as its conditions covers the whole row.
    /// </summary>
    public List<KeyValuePair<string, string>> SharedOptions
    {
        get => _shared ?? Variations.Parse(Options);
        set => _shared = value;
    }
    private List<KeyValuePair<string, string>>? _shared;
    /// <summary>No cost rule covers this option at all (rather than a rule with a problem, or a cost that's below the goal).</summary>
    public bool HasNoRule => Cost is null && Problem.StartsWith("No rule", StringComparison.Ordinal);
    /// <summary>Price on Etsy, before any planned sale.</summary>
    public decimal ListPrice { get; init; }
    /// <summary>Price used for the profit columns (list price, or the planned sale price).</summary>
    public decimal Price { get; init; }
    public decimal Shipping { get; init; }
    /// <summary>Label cost assumed for one unit (negative is never used; positive dollars).</summary>
    public decimal Label { get; init; }
    public string LabelSource { get; init; } = "";
    public decimal? Cost { get; init; }
    public decimal MakingHours { get; init; }
    public decimal? Organic { get; init; }
    public decimal? WorstCase { get; init; }
    public decimal? MinPrice { get; init; }
    public bool BelowFloor { get; init; }
    public string PriceSource { get; init; } = "";
    public string Problem { get; init; } = "";
    public decimal? OrganicPerHour => Organic is { } o && MakingHours > 0 ? Math.Round(o / MakingHours, 2, MidpointRounding.AwayFromZero) : null;
    public decimal? WorstPerHour => WorstCase is { } w && MakingHours > 0 ? Math.Round(w / MakingHours, 2, MidpointRounding.AwayFromZero) : null;
    public string Status => Problem.Length > 0 ? Problem : BelowFloor ? "Below your profit goal" : "";
    /// <summary>Has a cost and clears the goal: the green dot.</summary>
    public bool Ok => Cost is not null && !BelowFloor;
}

public sealed record PricingOptions
{
    public decimal Floor { get; init; } = 5m;
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Planned sale: 0.20 = 20% off.  0 = the pricing check itself.</summary>
    public decimal DiscountPercent { get; init; }
    /// <summary>Planned sale: only these listing IDs get the discount (null = all).</summary>
    public IReadOnlySet<string>? DiscountedListings { get; init; }
}

public static class PricingCheck
{
    /// <summary>
    /// Every active option, with the 1-unit quick estimates.  Prices come from the
    /// Etsy API (every option, sold or not) when listings were synced; otherwise from the
    /// most recent sale of each option (only options that have sold).  Retired options are
    /// skipped.  Shipping is the typical amount buyers paid on single-item orders of the
    /// listing; the label is the typical label matched to those orders (or = shipping).
    /// Options with no cost rule get no profit figures: never a guess.
    /// </summary>
    public static List<PriceRow> Build(ShopData data, IReadOnlyList<Listing> listings, IEnumerable<OrderProfit> orders, PricingOptions opt)
    {
        var fees = data.FeesAt(opt.Today);
        var settings = data.Costs.SettingsAt(opt.Today);

        var shipping = TypicalShipping(data, orders);
        var rows = new List<PriceRow>();

        // Listing ID for each active listing: from the API, or by matching the title to sold items,
        // or (a listing from the listings file that hasn't sold) a stand-in made from its title.
        var active = listings.Where(IsForSale).ToList();
        var activeIds = new HashSet<string>();
        var listingById = new Dictionary<string, Listing>();
        var soldIds = data.Items.Select(i => i.ListingId).ToHashSet();

        foreach (var (id, l) in ListingsById(data, active))
        {
            activeIds.Add(id);
            listingById[id] = l;
            if (l.Offerings.Count > 0)
                foreach (var (vars, price) in OfferedOptions(l))
                    rows.Add(Row(id, l.Title, vars, price, "Etsy"));
            else if (!soldIds.Contains(id))
                // From the listings file and never sold: that file has one price for the whole listing.
                foreach (var (vars, price) in OfferedOptions(l))
                    rows.Add(Row(id, l.Title, vars, price, "Listings file (not sold yet)"));
        }

        // Options known only from sales (no API data for the listing).
        var fromApi = rows.Select(r => r.ListingId).ToHashSet();
        var other = data.OtherOrderIds();   // prices at a craft fair aren't Etsy prices
        var sold = data.Items
            .Where(i => !other.Contains(i.OrderId) && !fromApi.Contains(i.ListingId) && (active.Count == 0 || activeIds.Contains(i.ListingId)))
            .GroupBy(i => (i.ListingId, Vars: Variations.Format(data.Costs.ApplyAliases(i.ListingId, Variations.Parse(i.Variations)))));
        // Titles change over time; use the newest one for every option of a listing.
        var newestName = data.Items.GroupBy(i => i.ListingId).ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SaleDate).First().ItemName);
        foreach (var g in sold)
        {
            // Grouped by today's option values, so sales under a retired name count toward the current option.
            var last = g.OrderByDescending(i => i.SaleDate).First();
            if (listingById.TryGetValue(g.Key.ListingId, out var current) && !OfferedToday(current, g.Key.Vars)) continue;
            rows.Add(Row(g.Key.ListingId, newestName[g.Key.ListingId], g.Key.Vars, last.Price, $"Last sold {last.SaleDate:MMM d, yyyy}"));
        }

        return [.. Combine(rows.Where(r => !Variations.Parse(r.Options).Any(v => data.Costs.IsRetired(r.ListingId, v.Key, v.Value))))
                       .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Options)];

        PriceRow Row(string id, string title, string vars, decimal listPrice, string source)
        {
            var discounted = opt.DiscountPercent > 0 && (opt.DiscountedListings is null || opt.DiscountedListings.Contains(id));
            var price = discounted ? Money.RoundFee(listPrice * (1 - opt.DiscountPercent)) : listPrice;
            var (ship, label, labelSource) = shipping.TryGetValue(id, out var s) ? s : (0m, (decimal?)null, "No single-item orders yet; assumed free shipping");
            var c = data.Costs.CostOf(id, vars, 1, opt.Today);
            // A rule whose parts add up to $0 (a part not filled in yet) isn't a real cost: no profit figures, never a green dot.
            if (c.Mapped && data.Costs.NeedsCost(id, c)) c = LineCost.Unmapped("Its parts add up to $0 so far: fill in what goes into them");
            if (!c.Mapped)
                return new PriceRow
                {
                    ListingId = id, Title = EtsyText.Clean(title), Options = vars, ListPrice = listPrice, Price = price, Shipping = ship,
                    Label = label ?? ship, LabelSource = labelSource, PriceSource = source, MakingHours = c.MakingHours,
                    Problem = c.Problem.Length > 0 ? c.Problem : "No cost rule",
                };
            var cost = c.Total + settings.PackagingPerOrder + data.Costs.PackagingPerUnit(id, opt.Today);
            var organic = Money.Round(PricingEstimate.Organic(fees, price, ship, cost, label));
            var worst = Money.Round(PricingEstimate.WorstCase(fees, price, ship, cost, label));
            var min = Math.Ceiling(PricingEstimate.MinPriceForTarget(fees, opt.Floor, ship, cost, label) * 100) / 100;
            return new PriceRow
            {
                ListingId = id, Title = EtsyText.Clean(title), Options = vars, ListPrice = listPrice, Price = price, Shipping = ship,
                Label = label ?? ship, LabelSource = labelSource, Cost = Money.Round(cost), MakingHours = Math.Round(c.MakingHours, 2),
                Organic = organic, WorstCase = worst, MinPrice = min, BelowFloor = worst < opt.Floor, PriceSource = source,
            };
        }
    }

    /// <summary>
    /// One row for combinations of a listing that earn exactly the same: same price, shipping,
    /// label, cost, making time and result.  Typically a color that doesn't change the price or
    /// the cost.  Merged one option at a time, so a row always stands for every combination of the
    /// values it shows (never "White or Black" when only White comes in Large).  A combination
    /// that differs in anything keeps its own row.
    /// </summary>
    public static List<PriceRow> Combine(IEnumerable<PriceRow> rows)
    {
        var groups = new List<(PriceRow First, List<(string Name, List<string> Values)> Options, int Count)>();
        foreach (var r in rows)
            groups.Add((r, Variations.Parse(r.Options).Select(p => (p.Key, new List<string> { p.Value })).ToList(), 1));

        var names = groups.SelectMany(g => g.Options.Select(o => o.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in names)
        {
            var merged = new List<(PriceRow First, List<(string Name, List<string> Values)> Options, int Count)>();
            foreach (var g in groups.GroupBy(g => (Same(g.First), Others(g.Options, name))))
            {
                var list = g.ToList();
                if (list.Count == 1 || list.Any(x => !x.Options.Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) { merged.AddRange(list); continue; }
                var options = list[0].Options.Select(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                    ? (o.Name, list.SelectMany(x => x.Options.First(y => y.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Values).Distinct().ToList())
                    : o).ToList();
                merged.Add((list[0].First, options, list.Sum(x => x.Count)));
            }
            groups = merged;
        }

        foreach (var g in groups.Where(g => g.Count > 1))
        {
            g.First.Combinations = g.Count;
            g.First.Grouped = string.Join("; ", g.Options.Select(o => $"{o.Name}: {Or(o.Values)}"));
            g.First.SharedOptions = [.. g.Options.Where(o => o.Values.Count == 1).Select(o => new KeyValuePair<string, string>(o.Name, o.Values[0]))];
        }
        return [.. groups.Select(g => g.First)];

        static string Or(List<string> v) => v.Count == 1 ? v[0] : string.Join(", ", v.Take(v.Count - 1)) + " or " + v[^1];
        static string Others(List<(string Name, List<string> Values)> opts, string except) =>
            string.Join("\u001f", opts.Where(o => !o.Name.Equals(except, StringComparison.OrdinalIgnoreCase))
                                      .Select(o => o.Name.ToLowerInvariant() + "=" + string.Join("\u001e", o.Values.Order(StringComparer.Ordinal))));
        static string Same(PriceRow r) => FormattableString.Invariant(
            $"{r.ListingId}|{r.Title}|{r.ListPrice}|{r.Price}|{r.Shipping}|{r.Label}|{r.Cost}|{r.MakingHours}|{r.Organic}|{r.WorstCase}|{r.MinPrice}|{r.BelowFloor}|{r.PriceSource}|{r.Problem}|{r.LabelSource}");
    }

    /// <summary>
    /// An empty part for an option that has no cost rule, and the rule linking that option to it:
    /// "Vinyl Roll Holder – 4 x 6 (24 rolls)", used when Size is 4 x 6 (24 rolls).  Options a grouped
    /// row merged (colors, say) are left out of the rule, so it covers the whole row.  The part
    /// starts in 2000 so every past sale of the option is costed once its materials are filled in.
    /// </summary>
    public static (Part Part, MappingRule Rule) NewPartForOption(PriceRow row, IEnumerable<string> existingPartNames, int sortOrder)
    {
        var shared = row.SharedOptions;
        var values = string.Join(", ", shared.Select(o => o.Value));
        var baseName = values.Length > 0 ? $"{Suggestions.Short(row.Title)} – {values}" : Suggestions.Short(row.Title);
        var taken = existingPartNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (int n = 2; taken.Contains(name); n++) name = $"{baseName} ({n})";
        var part = new Part
        {
            Name = name, Versions = [new PartVersion(new DateOnly(2000, 1, 1), [])],
            Notes = "Made from the pricing check" + (shared.Count > 0 ? $" for {string.Join(", ", shared.Select(o => $"{o.Key}: {o.Value}"))}" : ""),
        };
        var rule = new MappingRule
        {
            ListingId = row.ListingId, Name = values.Length > 0 ? values : "Every sale", SortOrder = sortOrder,
            Conditions = new(shared.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
        };
        return (part, rule);
    }

    /// <summary>For sale now: the API says "active", or it came from the "currently for sale" listings file (which has no state).</summary>
    public static bool IsForSale(Listing l) => l.State.Length == 0 || l.State.Equals("active", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Each listing under the ID its sales and cost rules use: Etsy's listing number from the
    /// API, or found by matching the title to sold items, or (never sold, listings file only)
    /// the stand-in <see cref="ListingKey.ForTitle"/>.
    /// </summary>
    public static Dictionary<string, Listing> ListingsById(ShopData data, IEnumerable<Listing> listings)
    {
        var idByTitle = data.Items.Where(i => !i.ListingId.StartsWith(OtherItem.IdPrefix)).GroupBy(i => EtsyText.Clean(i.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.SaleDate).First().ListingId, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, Listing>();
        foreach (var l in listings)
        {
            var id = !string.IsNullOrEmpty(l.ListingId) ? l.ListingId : idByTitle.GetValueOrDefault(EtsyText.Clean(l.Title)) ?? l.Key;
            result.TryAdd(id, l);
        }
        return result;
    }

    /// <summary>Above this many combinations a listings-file listing is shown as one row instead of every combination.</summary>
    public const int MaxCombinations = 200;

    /// <summary>
    /// The options a buyer can pick, as "Name:Value,Name:Value" with a price.  From the API:
    /// each enabled offering at its own price.  From the listings file: every combination of
    /// its option values, all at the listing's one price (that file has no per-option prices).
    /// </summary>
    public static List<(string Options, decimal Price)> OfferedOptions(Listing l)
    {
        if (l.Offerings.Count > 0)
            return [.. l.Offerings.Where(o => o.Enabled)
                .Select(o => (Variations.Format(o.Options.Select(kv => new KeyValuePair<string, string>(EtsyText.Clean(kv.Key), EtsyText.Clean(kv.Value)))), o.Price))];

        var vars = l.Variations.Where(v => v.Values.Count > 0 && EtsyText.Clean(v.Name).Length > 0).ToList();
        var combos = new List<string> { "" };
        foreach (var v in vars)
        {
            if ((long)combos.Count * v.Values.Count > MaxCombinations) return [("", l.Price)];
            combos = [.. combos.SelectMany(c => v.Values.Select(x => (c.Length > 0 ? c + "," : "") + $"{EtsyText.Clean(v.Name)}:{EtsyText.Clean(x)}"))];
        }
        return [.. combos.Select(c => (c, l.Price))];
    }

    /// <summary>
    /// False when the listings download says the listing no longer offers one of these
    /// values (a retired option).  Options the download doesn't list (e.g. personalization) are ignored.
    /// </summary>
    private static bool OfferedToday(Listing listing, string vars)
    {
        foreach (var (name, value) in Variations.Parse(vars))
        {
            var v = listing.Variations.FirstOrDefault(x => string.Equals(EtsyText.Clean(x.Name), name, StringComparison.OrdinalIgnoreCase));
            if (v is null || v.Values.Count == 0) continue;
            if (!v.Values.Any(x => string.Equals(EtsyText.Clean(x), value.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }

    /// <summary>
    /// Per listing: the median shipping buyers paid on orders of one unit of that listing, and
    /// the median label matched exactly to those orders (null = assume label = shipping).
    /// </summary>
    public static Dictionary<string, (decimal Shipping, decimal? Label, string Source)> TypicalShipping(ShopData data, IEnumerable<OrderProfit> orders)
    {
        var items = data.Items.ToLookup(i => i.OrderId);
        var byId = orders.ToDictionary(o => o.OrderId);
        var result = new Dictionary<string, (decimal, decimal?, string)>();
        var singles = data.Orders.Where(o => !o.IsOther)
            .Select(o => (Order: o, Items: items[o.OrderId].ToList()))
            .Where(x => x.Items.Count == 1 && x.Items[0].Quantity == 1)
            .GroupBy(x => x.Items[0].ListingId);
        foreach (var g in singles)
        {
            var ship = Median(g.Select(x => x.Order.Shipping));
            var labels = g.Select(x => byId.GetValueOrDefault(x.Order.OrderId)).Where(p => p is { Label: < 0, LabelShared: false }).Select(p => -p!.Label).ToList();
            result[g.Key] = labels.Count > 0
                ? (ship, Median(labels), $"Typical label on {labels.Count} order(s)")
                : (ship, null, "Label assumed equal to shipping charged");
        }
        return result;
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return 0;
        return v.Count % 2 == 1 ? v[v.Count / 2] : Money.Round((v[v.Count / 2 - 1] + v[v.Count / 2]) / 2);
    }
}
