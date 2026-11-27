// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

/// <summary>All imported data plus the cost model; the input to every report.</summary>
public sealed class ShopData
{
    public List<StatementLine> Statement { get; init; } = [];
    public List<Order> Orders { get; init; } = [];
    public List<OrderItem> Items { get; init; } = [];
    public List<FeeRates> FeeRates { get; init; } = [new()];
    public CostModel Costs { get; init; } = new();
    public Categorizer Categorizer { get; init; } = new();

    public FeeRates FeesAt(DateOnly d) =>
        FeeRates.Count == 0 ? new FeeRates() : Effective.At(FeeRates, f => f.EffectiveFrom, d);

    /// <summary>Costs only count sales up to the last statement date loaded, so fees and costs cover the same period.</summary>
    public DateOnly? CostCutoff => Statement.Count == 0 ? null : Statement.Max(s => s.Date);

    /// <summary>Order IDs of sales made outside Etsy (entered by hand).</summary>
    public HashSet<string> OtherOrderIds() => [.. Orders.Where(o => o.IsOther).Select(o => o.OrderId)];
}

/// <summary>A self-test problem.  Errors turn the month red; warnings are shown but don't.</summary>
public sealed record CheckIssue(string Month, bool IsError, string Message);

public sealed class MonthlyPnL
{
    public string Month { get; init; } = "";
    public decimal GrossSales { get; set; }
    public decimal SalesTax { get; set; }
    public decimal Refunds { get; set; }
    public decimal TransactionFees { get; set; }
    public decimal ProcessingFees { get; set; }
    public decimal ListingFees { get; set; }
    public decimal OffsiteAds { get; set; }
    public decimal EtsyAds { get; set; }
    public decimal ShippingLabels { get; set; }
    public decimal ExtraShipping { get; set; }
    public decimal Material { get; set; }
    public decimal Waste { get; set; }
    public decimal Components { get; set; }
    /// <summary>The maker's own time at their hourly rate (zero unless they set one).</summary>
    public decimal OwnCost { get; set; }
    public decimal MachineCost { get; set; }
    public decimal FixedCosts { get; set; }
    public decimal Packaging { get; set; }
    public decimal UnknownLines { get; set; }
    /// <summary>Sales made outside Etsy: price + shipping charged.</summary>
    public decimal OtherSales { get; set; }
    /// <summary>That channel's fees (card reader, market stall commission), negative.</summary>
    public decimal OtherFees { get; set; }
    public int Orders { get; set; }
    public int Units { get; set; }
    public int UnmappedLines { get; set; }
    /// <summary>Sum of the Net column of every non-excluded statement line, straight from the file.</summary>
    public decimal StatementNet { get; set; }
    public List<CheckIssue> Issues { get; } = [];

    public decimal NetSales => GrossSales + SalesTax;
    public decimal EtsyFees => TransactionFees + ProcessingFees + ListingFees + OffsiteAds + EtsyAds;
    public decimal Shipping => ShippingLabels + ExtraShipping;
    public decimal OtherCosts => Material + Waste + Components + MachineCost + OwnCost + FixedCosts + Packaging;
    public decimal NetProfit => NetSales + Refunds + EtsyFees + Shipping + OtherCosts + OtherSales + OtherFees;
    public bool SelfTestPassed => !Issues.Any(i => i.IsError);
}

public static class PnLBuilder
{
    /// <summary>
    /// Builds one P&amp;L row per statement month, plus the self-test:
    /// every statement line must be Amount + Fees &amp; Taxes = Net, every line must have a
    /// category, and the categorized totals must equal the statement's own Net total
    /// (= Net Profit − other costs − manual shipping) to the penny.  Cross-checks
    /// against the order export (processing fees, sales tax, missing orders) are warnings.
    /// </summary>
    public static List<MonthlyPnL> Build(ShopData data)
    {
        var months = new SortedDictionary<string, MonthlyPnL>();
        MonthlyPnL M(string key) => months.TryGetValue(key, out var m) ? m : months[key] = new MonthlyPnL { Month = key };

        foreach (var line in data.Statement)
        {
            var m = M(line.Month);
            var cat = data.Categorizer.Categorize(line);
            if (line.Amount + line.FeesAndTaxes != line.Net)
                m.Issues.Add(new(m.Month, true, $"{line.Date}: {line.Title} — Amount + Fees ≠ Net ({Money.Format(line.Amount)} + {Money.Format(line.FeesAndTaxes)} vs {Money.Format(line.Net)})"));
            if (cat == Category.Excluded) continue;
            m.StatementNet += line.Net;
            switch (cat)
            {
                case Category.GrossSales: m.GrossSales += line.Net; break;
                case Category.SalesTax: m.SalesTax += line.Net; break;
                case Category.Refunds: m.Refunds += line.Net; break;
                case Category.TransactionFees: m.TransactionFees += line.Net; break;
                case Category.ProcessingFees: m.ProcessingFees += line.Net; break;
                case Category.ListingFees: m.ListingFees += line.Net; break;
                case Category.OffsiteAds: m.OffsiteAds += line.Net; break;
                case Category.EtsyAds: m.EtsyAds += line.Net; break;
                case Category.ShippingLabels: m.ShippingLabels += line.Net; break;
                default:
                    m.UnknownLines += line.Net;
                    m.Issues.Add(new(m.Month, true, $"Unknown statement line: {line.Type} / {line.Title} ({Money.Format(line.Net)})"));
                    break;
            }
        }

        // Sales outside Etsy: their money is known straight away, so they count in their own month
        // (creating it if no statement covers it) whatever statements are loaded.
        var other = data.OtherOrderIds();
        foreach (var order in data.Orders.Where(o => o.IsOther))
        {
            var m = M(order.SaleDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
            m.OtherSales += order.OrderValue + order.Shipping;
            m.OtherFees -= order.CardProcessingFees;
        }

        // Costs Etsy doesn't take: by sale date, only up to the last statement date loaded
        // (sales outside Etsy: always).
        var cutoff = data.CostCutoff;
        {
            foreach (var item in data.Items.Where(i => other.Contains(i.OrderId) || (cutoff is not null && i.SaleDate <= cutoff)))
            {
                var key = item.SaleDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
                if (!months.ContainsKey(key)) continue;
                var m = months[key];
                var c = data.Costs.CostOf(item.ListingId, item.Variations, item.Quantity, item.SaleDate);
                m.Units += item.Quantity;
                if (!c.Mapped) m.UnmappedLines++;
                m.Material -= c.Material;
                m.Waste -= c.Waste;
                m.Components -= c.Components;
                m.MachineCost -= c.MachineCost;
                m.OwnCost -= c.OwnCost;
                m.FixedCosts -= c.Fixed;
                m.Packaging -= data.Costs.PackagingPerUnit(item.ListingId, item.SaleDate) * item.Quantity;
            }
            foreach (var order in data.Orders.Where(o => o.IsOther || (cutoff is not null && o.SaleDate <= cutoff)))
            {
                var key = order.SaleDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
                if (!months.ContainsKey(key)) continue;
                var m = months[key];
                m.Orders++;
                m.Packaging -= data.Costs.SettingsAt(order.SaleDate).PackagingPerOrder;
                m.ExtraShipping -= order.ExtraShippingCost;
            }
        }

        foreach (var m in months.Values)
        {
            // The core self-test: Etsy's own lines must add up to the penny.
            var etsyPart = m.NetProfit - m.OtherCosts - m.ExtraShipping - m.OtherSales - m.OtherFees;
            if (etsyPart + m.UnknownLines != m.StatementNet)
                m.Issues.Add(new(m.Month, true, $"Categorized total {Money.Format(etsyPart)} ≠ statement total {Money.Format(m.StatementNet)}"));
            if (m.UnmappedLines > 0)
                m.Issues.Add(new(m.Month, false, $"{m.UnmappedLines} sold line(s) have no cost rule, so their making cost is missing"));
        }

        CrossCheckOrders(data, months);
        return [.. months.Values];
    }

    /// <summary>Independent checks against the order export.  Warnings only, since refunds and reimbursements can legitimately differ.</summary>
    private static void CrossCheckOrders(ShopData data, SortedDictionary<string, MonthlyPnL> months)
    {
        var orders = data.Orders.ToDictionary(o => o.OrderId);
        var byOrder = data.Statement.Where(s => s.OrderId is not null).GroupBy(s => s.OrderId!);
        foreach (var g in byOrder)
        {
            var sale = g.FirstOrDefault(s => data.Categorizer.Categorize(s) == Category.GrossSales);
            if (sale is null) continue;
            var m = months[sale.Month];
            if (!orders.TryGetValue(g.Key, out var order))
            {
                // Cancelled orders are refunded and left out of Etsy's order export.  That's expected, not a problem.
                if (g.Any(s => data.Categorizer.Categorize(s) == Category.Refunds)) continue;
                m.Issues.Add(new(m.Month, false, $"Order #{g.Key} is on the statement but not in the order export (import a newer EtsySoldOrders file)"));
                continue;
            }
            var hasRefund = g.Any(s => data.Categorizer.Categorize(s) == Category.Refunds);
            var processing = -g.Where(s => data.Categorizer.Categorize(s) == Category.ProcessingFees).Sum(s => s.Net);
            var expected = hasRefund ? order.AdjustedCardProcessingFees ?? order.CardProcessingFees : order.CardProcessingFees;
            // Not shipped yet and no fee from Etsy: Etsy fills it in later.  Profit uses the statement's fee either way.
            if (expected == 0 && order.DateShipped is null) continue;
            if (processing != expected)
                m.Issues.Add(new(m.Month, false, $"Order #{g.Key}: processing fee on statement {Money.Format(processing)} vs order export {Money.Format(expected)}"));
        }
    }
}

/// <summary>Profit of one order.</summary>
public sealed class OrderProfit
{
    public string OrderId { get; init; } = "";
    public DateOnly SaleDate { get; init; }
    public DateOnly? DateShipped { get; init; }
    public string Items { get; init; } = "";
    public int Units { get; init; }
    public decimal ItemPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal ShippingDiscount { get; init; }
    /// <summary>Coupon code(s) used on the order, if the order export had them.</summary>
    public string Coupon { get; init; } = "";
    public decimal ShippingCharged { get; init; }
    public decimal SalesTax { get; init; }
    /// <summary>"Payment for Order" Net (includes tax and any shipping reimbursement).</summary>
    public decimal Gross { get; set; }
    public decimal TaxLines { get; set; }
    public decimal Refunds { get; set; }
    public decimal TransactionFees { get; set; }
    public decimal ProcessingFees { get; set; }
    public decimal OffsiteAds { get; set; }
    /// <summary>Listing fee lines aren't tied to an order on the statement, so this is rate × units.</summary>
    public decimal ListingFees { get; set; }
    public decimal Label { get; set; }
    public bool LabelShared { get; set; }
    public decimal ExtraShipping { get; set; }
    public decimal Costs { get; set; }
    public decimal Packaging { get; set; }
    public bool Unmapped { get; set; }
    public bool OnStatement { get; set; }
    /// <summary>"Etsy", or where a sale outside Etsy was made (craft fair…).</summary>
    public string Channel { get; set; } = "Etsy";
    public bool IsOther => Channel != "Etsy";

    /// <summary>The statement has an Offsite Ads line for this order (a fee, even one Etsy later credited back).</summary>
    public bool HasOffsiteAdsLine { get; set; }

    /// <summary>
    /// The sale came through an Offsite Ad.  Still true when the fee was credited back (a
    /// refunded order, say): the ad brought the sale either way, and the money columns already
    /// show that it cost nothing in the end.
    /// </summary>
    public bool AdAttributed => HasOffsiteAdsLine || OffsiteAds != 0;
    public decimal EtsyFees => TransactionFees + ProcessingFees + OffsiteAds + ListingFees;
    public decimal Profit => Gross + TaxLines + Refunds + EtsyFees + Label + ExtraShipping + Costs + Packaging;
    /// <summary>Profit for display: blank until the order's money is on a loaded statement.</summary>
    public decimal? ProfitIfKnown => OnStatement ? Profit : null;
    public string Status => IsOther ? $"Sold outside Etsy: {Channel}" + (Unmapped ? " (missing cost mapping)" : "")
        : !OnStatement ? "Not on statement yet" : Unmapped ? "Missing cost mapping" : LabelShared ? "Label cost is an even share (not matched one to one)" : "";
}

public static class OrderReport
{
    public static List<OrderProfit> Build(ShopData data)
    {
        var lines = data.Statement.Where(s => s.OrderId is not null).ToLookup(s => s.OrderId!);
        var items = data.Items.ToLookup(i => i.OrderId);
        var labels = AllocateLabels(data);
        var result = new List<OrderProfit>();

        foreach (var o in data.Orders.OrderByDescending(o => o.SaleDate))
        {
            var its = items[o.OrderId].ToList();
            var units = its.Sum(i => i.Quantity);
            var p = new OrderProfit
            {
                OrderId = o.OrderId,
                SaleDate = o.SaleDate,
                DateShipped = o.DateShipped,
                Items = string.Join("; ", its.Select(i => (i.Quantity > 1 ? $"{i.Quantity}× " : "") + i.ItemName + (i.Variations.Length > 0 ? $" ({i.Variations.Replace("\\:", ":")})" : ""))),
                Units = units,
                ItemPrice = its.Sum(i => i.Price * i.Quantity),
                Discount = o.DiscountAmount,
                ShippingDiscount = o.ShippingDiscount,
                Coupon = string.Join(", ", its.Select(i => i.CouponCode.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)),
                ShippingCharged = o.Shipping,
                SalesTax = o.SalesTax,
                ExtraShipping = -o.ExtraShippingCost,
            };

            foreach (var s in lines[o.OrderId])
            {
                p.OnStatement = true;
                switch (data.Categorizer.Categorize(s))
                {
                    case Category.GrossSales: p.Gross += s.Net; break;
                    case Category.SalesTax: p.TaxLines += s.Net; break;
                    case Category.Refunds: p.Refunds += s.Net; break;
                    case Category.TransactionFees: p.TransactionFees += s.Net; break;
                    case Category.ProcessingFees: p.ProcessingFees += s.Net; break;
                    case Category.OffsiteAds: p.OffsiteAds += s.Net; p.HasOffsiteAdsLine = true; break;
                }
            }
            // Refund lines carry the order number in the title, which StatementIds already handles.

            if (o.IsOther)
            {
                // Sold outside Etsy: what the buyer paid, less that channel's fees.  No Etsy fees.
                p.OnStatement = true;
                p.Gross = o.OrderValue + o.Shipping;
                p.ProcessingFees = -o.CardProcessingFees;
                p.Channel = o.OrderType.Length > 0 ? o.OrderType : "Outside Etsy";
            }
            else
                p.ListingFees = -Money.RoundFee(data.FeesAt(o.SaleDate).ListingFee * units);
            if (labels.TryGetValue(o.OrderId, out var lab)) { p.Label = lab.Amount; p.LabelShared = lab.Shared; }

            var settings = data.Costs.SettingsAt(o.SaleDate);
            p.Packaging = -(settings.PackagingPerOrder + its.Sum(i => data.Costs.PackagingPerUnit(i.ListingId, o.SaleDate) * i.Quantity));
            foreach (var i in its)
            {
                var c = data.Costs.CostOf(i.ListingId, i.Variations, i.Quantity, i.SaleDate);
                p.Costs -= c.Total;
                if (!c.Mapped) p.Unmapped = true;
            }
            result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Label lines on the statement are linked to a label number, not an order, so labels are
    /// matched to orders by date and amount.  See <see cref="AllocateLabelsDetailed"/>.
    /// </summary>
    public static Dictionary<string, (decimal Amount, bool Shared)> AllocateLabels(ShopData data) => AllocateLabelsDetailed(data).ByOrder;

    /// <summary>How many days before the ship date a label may have been bought and still be matched.</summary>
    public const int LabelLookbackDays = 7;

    /// <summary>
    /// Matches shipping labels to orders.
    /// <list type="number">
    /// <item>Lines with the same label number are one label: the purchase plus any later USPS
    /// adjustment or credit, whatever day those landed on.</item>
    /// <item>A label whose price equals exactly what one order charged for shipping is that
    /// order's label (same day first, then up to <see cref="LabelLookbackDays"/> days before shipping).</item>
    /// <item>Otherwise each ship day's orders take that day's labels, then unclaimed labels
    /// bought on earlier days (a label is often bought a day or two before the package goes out).
    /// When it isn't one label for one order, the cost is shared evenly and flagged.</item>
    /// <item>What nothing claims is reported as unmatched, never dropped silently.</item>
    /// </list>
    /// </summary>
    public static LabelAllocation AllocateLabelsDetailed(ShopData data)
    {
        var result = new Dictionary<string, (decimal Amount, bool Shared)>();
        var unmatched = new List<UnmatchedLabel>();

        // 1. One entry per label number.
        var labels = new List<Lab>();
        foreach (var s in data.Statement.Where(s => s.LabelId is null && data.Categorizer.Categorize(s) == Category.ShippingLabels))
            unmatched.Add(new(s.Date, s.Net, 1));
        foreach (var g in data.Statement.Where(s => s.LabelId is not null && data.Categorizer.Categorize(s) == Category.ShippingLabels).GroupBy(s => s.LabelId!))
        {
            var purchases = g.Where(s => !ShippingReport.IsAdjustment(s)).OrderBy(s => s.Date).ToList();
            if (purchases.Count == 0)
            {
                // An adjustment for a label bought in a month that isn't loaded.
                foreach (var s in g) unmatched.Add(new(s.Date, s.Net, 1));
                continue;
            }
            var total = g.Sum(s => s.Net);
            if (total == 0) continue;   // bought and fully refunded: nothing to charge to an order
            labels.Add(new Lab(purchases[0].Date, purchases.Sum(s => s.Net), total));
        }

        var shipped = data.Orders.Where(o => o.DateShipped is not null && !o.IsOther).OrderBy(o => o.DateShipped).ThenBy(o => o.OrderId).ToList();
        bool Open(Order o) => !result.ContainsKey(o.OrderId);
        static bool InWindow(Order o, DateOnly labelDay)
        {
            var ship = o.DateShipped!.Value;
            return labelDay <= ship && labelDay >= ship.AddDays(-LabelLookbackDays) && labelDay >= o.SaleDate;
        }

        // 2a. Exact amount, same day (Etsy-calculated shipping charges the buyer the label price).
        foreach (var day in shipped.GroupBy(o => o.DateShipped!.Value))
        {
            var orders = day.ToList();
            foreach (var o in orders.Where(o => o.Shipping != 0))
            {
                var same = labels.Where(l => l.Day == day.Key && -l.Price == o.Shipping).ToList();
                if (same.Count == 1 && orders.Count(x => x.Shipping == o.Shipping) == 1)
                {
                    result[o.OrderId] = (same[0].Total, false);
                    labels.Remove(same[0]);
                }
            }
        }
        // 2b. Exact amount, bought up to a week before shipping.
        foreach (var o in shipped.Where(o => Open(o) && o.Shipping != 0).ToList())
        {
            var same = labels.Where(l => -l.Price == o.Shipping && InWindow(o, l.Day)).ToList();
            if (same.Count != 1) continue;
            if (shipped.Count(x => Open(x) && x.Shipping == o.Shipping && InWindow(x, same[0].Day)) != 1) continue;
            result[o.OrderId] = (same[0].Total, false);
            labels.Remove(same[0]);
        }

        // 3. By day.  What's left of each day's labels is a pool: when only some of a day's labels
        // are taken there is no telling which, so each one taken counts as that day's average.
        var pools = labels.GroupBy(l => l.Day).ToDictionary(g => g.Key, g => new Pool { Count = g.Count(), Total = g.Sum(l => l.Total) });
        foreach (var day in shipped.Where(Open).GroupBy(o => o.DateShipped!.Value).OrderBy(g => g.Key).ToList())
        {
            var orders = day.ToList();
            var earliestSale = orders.Min(o => o.SaleDate);
            int need = orders.Count, taken = 0;
            decimal total = 0;
            var partial = false;
            for (int back = 0; back <= LabelLookbackDays && need > 0; back++)
            {
                var d = day.Key.AddDays(-back);
                if (back > 0 && d < earliestSale) break;
                if (!pools.TryGetValue(d, out var pool) || pool.Count == 0) continue;
                var k = Math.Min(pool.Count, need);
                if (k < pool.Count) partial = true;
                total += pool.Take(k);
                taken += k;
                need -= k;
            }
            if (taken == 0) continue;
            Share(orders, total, orders.Count > 1 || taken != 1 || partial);
        }

        // 4. Labels nobody claimed.
        foreach (var (d, pool) in pools.Where(p => p.Value.Count > 0).OrderBy(p => p.Key).ToList())
        {
            // An order sold before the label was bought that isn't marked shipped yet (or was marked the day before).
            var waiting = data.Orders
                .Where(o => !o.IsOther && Open(o) && (o.DateShipped is null ? o.SaleDate <= d && o.SaleDate >= d.AddDays(-14) : o.DateShipped == d.AddDays(-1)))
                .OrderBy(o => o.SaleDate).ThenBy(o => o.OrderId).ToList();
            if (waiting.Count > 0)
            {
                var k = Math.Min(pool.Count, waiting.Count);
                var exact = pool.Count == 1 && waiting.Count == 1;
                Share(waiting.Take(k).ToList(), pool.Take(k), !exact);
            }
            if (pool.Count == 0) continue;
            // More labels than orders that day: an order that went out in more than one box.
            var sameDay = shipped.Where(o => o.DateShipped == d && result.ContainsKey(o.OrderId)).ToList();
            if (sameDay.Count > 0)
            {
                var extra = pool.Take(pool.Count);
                var share = Math.Round(extra / sameDay.Count, 2, MidpointRounding.AwayFromZero);
                for (int i = 0; i < sameDay.Count; i++)
                    result[sameDay[i].OrderId] = (result[sameDay[i].OrderId].Amount + (i == 0 ? extra - share * (sameDay.Count - 1) : share), true);
                continue;
            }
            unmatched.Add(new(d, pool.Total, pool.Count));
        }
        return new LabelAllocation(result, unmatched);

        void Share(List<Order> orders, decimal total, bool shared)
        {
            var share = Math.Round(total / orders.Count, 2, MidpointRounding.AwayFromZero);
            for (int i = 0; i < orders.Count; i++)
                // The first order takes the rounding remainder so it still adds up.
                result[orders[i].OrderId] = (i == 0 ? total - share * (orders.Count - 1) : share, shared);
        }
    }

    /// <summary>One label: the day it was bought, its purchase price, and its total with adjustments (both negative).</summary>
    private sealed record Lab(DateOnly Day, decimal Price, decimal Total);

    private sealed class Pool
    {
        public int Count;
        public decimal Total;
        public decimal Take(int k)
        {
            var amount = k >= Count ? Total : Math.Round(Total * k / Count, 2, MidpointRounding.AwayFromZero);
            Total -= amount;
            Count -= k;
            return amount;
        }
    }
}

/// <summary>Label money on the statement that couldn't be tied to an order.</summary>
public sealed record UnmatchedLabel(DateOnly Date, decimal Amount, int Labels);

public sealed record LabelAllocation(Dictionary<string, (decimal Amount, bool Shared)> ByOrder, List<UnmatchedLabel> Unmatched)
{
    public decimal UnmatchedTotal => Unmatched.Sum(u => u.Amount);
}
