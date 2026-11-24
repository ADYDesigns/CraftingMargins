// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

// Expense log and the tax summary.
// Not tax advice: the Schedule C lines are suggestions the user can change.

/// <summary>One business purchase not on the Etsy statement.  Amount is positive dollars spent.</summary>
public sealed class Expense
{
    public long Id { get; set; }
    public string ShopId { get; set; } = Shop.Default;
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>
/// An expense category.  CountedPerSale = the app already counts this as a per-sale cost
/// (filament, hardware, packaging), so the tax summary uses either the purchases or the
/// per-sale amounts, never both.
/// </summary>
public sealed record ExpenseCategory(string Name, string ScheduleCLine, bool CountedPerSale);

/// <summary>Which side of a supply feeds the tax total.</summary>
public enum SuppliesBasis { Used, Bought }

public static class ScheduleC
{
    public const string GrossReceipts = "1 Gross receipts";
    public const string Returns = "2 Returns and allowances";
    public const string Advertising = "8 Advertising";
    public const string Commissions = "10 Commissions and fees";
    public const string Depreciation = "13 Depreciation and section 179";
    public const string Office = "18 Office expense";
    public const string Supplies = "22 Supplies";
    public const string Utilities = "25 Utilities";
    public const string Other = "27a Other expenses";
    public const string CostOfGoods = "4 Cost of goods sold (Part III)";

    public static readonly string[] Lines = [GrossReceipts, Returns, CostOfGoods, Advertising, Commissions, Depreciation, Office, Supplies, Utilities, Other, ""];

    /// <summary>The starting categories.  The first two are named for the craft; all of them can be renamed.</summary>
    public static List<ExpenseCategory> DefaultCategories(Craft craft = Craft.Printing3D) =>
    [
        new(Crafts.MaterialsCategory(craft), Supplies, true),
        new(Crafts.ComponentsCategory(craft), Supplies, true),
        new("Packaging supplies", Supplies, true),
        new("Equipment", Depreciation, false),
        new("Software/subscriptions", Other, false),
        new("Other", Other, false),
    ];
}

/// <summary>One line of the tax summary: twelve months plus the year.  Income positive, costs negative.</summary>
public sealed class TaxLine
{
    public string Section { get; init; } = "";
    public string Name { get; init; } = "";
    public string ScheduleCLine { get; init; } = "";
    public decimal[] Months { get; } = new decimal[12];
    public decimal Total => Months.Sum();
    /// <summary>False = shown for reference but left out of the total (the other side of a supply, or an estimate).</summary>
    public bool InTotal { get; init; } = true;
    public string Note { get; init; } = "";
    public bool IsTotal { get; init; }
    public decimal Month(int m) => Months[m - 1];
}

public sealed class TaxSummary
{
    public int Year { get; init; }
    public SuppliesBasis Basis { get; init; }
    public List<TaxLine> Lines { get; } = [];
    public List<string> Notes { get; } = [];
    public decimal NetProfit => Lines.Where(l => l.InTotal && !l.IsTotal).Sum(l => l.Total);

    /// <summary>Lines that count, rolled up by Schedule C line (in form order).</summary>
    public List<TaxLine> ByScheduleC()
    {
        var order = ScheduleC.Lines.ToList();
        var result = Lines.Where(l => l.InTotal && !l.IsTotal)
            .GroupBy(l => l.ScheduleCLine.Length == 0 ? "(no line chosen)" : l.ScheduleCLine)
            .OrderBy(g => order.IndexOf(g.Key) is var i && i >= 0 ? i : 99)
            .Select(g =>
            {
                var t = new TaxLine { Section = "Schedule C", Name = g.Key, ScheduleCLine = g.Key, Note = string.Join(", ", g.Select(l => l.Name)) };
                foreach (var l in g) for (int m = 0; m < 12; m++) t.Months[m] += l.Months[m];
                return t;
            }).ToList();
        result.Add(TotalLine(result));
        return result;
    }

    internal static TaxLine TotalLine(IEnumerable<TaxLine> lines)
    {
        var t = new TaxLine { Section = "", Name = "Net profit", IsTotal = true };
        foreach (var l in lines.Where(l => l.InTotal && !l.IsTotal)) for (int m = 0; m < 12; m++) t.Months[m] += l.Months[m];
        return t;
    }
}

/// <summary>Your 1099-K total against the app's figure for the same year.</summary>
public sealed record Form1099KCheck(int Year, decimal EtsyPayments, decimal Refunds, decimal? Form1099K)
{
    public decimal? Difference => Form1099K is { } f ? f - EtsyPayments : null;
}

public static class TaxReport
{
    /// <summary>
    /// Etsy's 1099-K reports the gross of every payment for orders in the calendar year:
    /// item price, shipping and the sales tax buyers paid, before fees and before refunds.
    /// The closest figure from the statements is the year's "Payment for Order" total.
    /// </summary>
    public static Form1099KCheck Check1099K(int year, IEnumerable<MonthlyPnL> pnl, decimal? form1099K)
    {
        var months = pnl.Where(m => m.Month.StartsWith($"{year}-")).ToList();
        return new Form1099KCheck(year, months.Sum(m => m.GrossSales), months.Sum(m => m.Refunds), form1099K);
    }

    public const string SalesSection = "Sales";
    public const string EtsySection = "Etsy fees";
    public const string ShippingSection = "Shipping";
    public const string UsedSection = "Costs per sale (used)";
    public const string BoughtSection = "Expense log (bought)";

    /// <summary>
    /// Builds the year from the monthly P&amp;L (statement months) and the expense log.
    /// Per-sale costs and the expense categories marked CountedPerSale are both shown,
    /// but only the side picked by <paramref name="basis"/> counts in the total.
    /// </summary>
    public static TaxSummary Build(int year, IEnumerable<MonthlyPnL> pnl, IEnumerable<Expense> expenses,
        IReadOnlyList<ExpenseCategory> categories, SuppliesBasis basis)
    {
        var s = new TaxSummary { Year = year, Basis = basis };
        var months = pnl.Where(m => m.Month.StartsWith($"{year}-")).ToList();
        TaxLine Add(string section, string name, string line, Func<MonthlyPnL, decimal> value, bool inTotal = true, string note = "")
        {
            var t = new TaxLine { Section = section, Name = name, ScheduleCLine = line, InTotal = inTotal, Note = note };
            foreach (var m in months) t.Months[int.Parse(m.Month[5..7]) - 1] += value(m);
            s.Lines.Add(t);
            return t;
        }

        Add(SalesSection, "Sales (sales tax taken out)", ScheduleC.GrossReceipts, m => m.NetSales, note: "Payments for orders minus the sales tax Etsy collected and paid for you");
        Add(SalesSection, "Refunds", ScheduleC.Returns, m => m.Refunds);
        if (months.Any(m => m.OtherSales != 0 || m.OtherFees != 0))
        {
            Add(SalesSection, "Sales outside Etsy", ScheduleC.GrossReceipts, m => m.OtherSales, note: "Craft fairs and other sales you entered");
            Add(SalesSection, "Fees outside Etsy", ScheduleC.Commissions, m => m.OtherFees, note: "Card reader or market fees on those sales");
        }
        Add(EtsySection, "Transaction fees", ScheduleC.Commissions, m => m.TransactionFees);
        Add(EtsySection, "Processing fees", ScheduleC.Commissions, m => m.ProcessingFees);
        Add(EtsySection, "Listing fees", ScheduleC.Commissions, m => m.ListingFees);
        Add(EtsySection, "Offsite Ads fees", ScheduleC.Advertising, m => m.OffsiteAds);
        Add(EtsySection, "Etsy Ads fees", ScheduleC.Advertising, m => m.EtsyAds);
        Add(ShippingSection, "Etsy shipping labels", ScheduleC.Other, m => m.ShippingLabels, note: "Postage");
        Add(ShippingSection, "Shipping paid outside Etsy", ScheduleC.Other, m => m.ExtraShipping, note: "Postage you entered on orders");
        if (months.Any(m => m.UnknownLines != 0))
            Add(EtsySection, "Statement lines of an unknown type", "", m => m.UnknownLines, note: "Give them a category on Needs Attention");

        var used = basis == SuppliesBasis.Used;
        var usedNote = used ? "" : "Not in the total: purchases from the expense log are used instead";
        Add(UsedSection, "Materials used", ScheduleC.CostOfGoods, m => m.Material + m.Waste, used, usedNote);
        Add(UsedSection, "Components used", ScheduleC.CostOfGoods, m => m.Components, used, usedNote);
        Add(UsedSection, "Packaging used", ScheduleC.CostOfGoods, m => m.Packaging, used, usedNote);
        // These two have no purchase in the expense log standing in for them, so on the "bought"
        // basis they are left out of the total.  That must be said out loud, with the amounts,
        // never silently: see the note added below.
        const string noPurchase = "Not in the total on the \"bought\" basis, and nothing in the expense log replaces it. Log the real purchase as an expense if you want it counted";
        var fixedLine = Add(UsedSection, "Fixed item costs", ScheduleC.CostOfGoods, m => m.FixedCosts, used, used ? "" : noPurchase);
        var powerLine = Add(UsedSection, "Machine time (estimate)", ScheduleC.CostOfGoods, m => m.MachineCost, used, used ? "Your $ per machine hour × machine time" : noPurchase);
        if (!used && (fixedLine.Total != 0 || powerLine.Total != 0))
        {
            var parts = new List<string>();
            if (fixedLine.Total != 0) parts.Add($"fixed item costs of {Money.Format(-fixedLine.Total)}");
            if (powerLine.Total != 0) parts.Add($"the machine-time estimate of {Money.Format(-powerLine.Total)}");
            s.Notes.Add($"Counting supplies as bought leaves out {string.Join(" and ", parts)} for {year}: no purchase in the expense log stands for them. " +
                        "They are shown greyed in the list. To count them, log the real purchases (the items you bought in, the electricity bill) as expenses.");
        }
        // The maker's own time at their hourly rate is a cost in the Monthly P&L (profit after paying yourself),
        // but a sole owner's own labour is not a business expense, so it never counts here.
        Add(UsedSection, "Your own time", "", m => m.OwnCost, inTotal: false, note: "Not in the total: your own time is not a business expense. It does count in the Monthly P&L");

        var byCat = categories.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var yearExpenses = expenses.Where(e => e.Date.Year == year).ToList();
        foreach (var g in yearExpenses.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            var cat = byCat.GetValueOrDefault(g.Key) ?? new ExpenseCategory(g.Key, "", false);
            var inTotal = !cat.CountedPerSale || !used;
            var t = new TaxLine
            {
                Section = BoughtSection, Name = g.Key.Length == 0 ? "(no category)" : g.Key, ScheduleCLine = cat.ScheduleCLine, InTotal = inTotal,
                Note = inTotal ? "" : "Not in the total: the per-sale amount above is used instead",
            };
            foreach (var e in g) t.Months[e.Date.Month - 1] -= e.Amount;
            s.Lines.Add(t);
        }

        // Drop lines that are zero all year, except the main ones people look for.
        s.Lines.RemoveAll(l => l.Months.All(v => v == 0) && (l.Section is UsedSection or ShippingSection) && l.Name != "Etsy shipping labels");
        s.Lines.Add(TaxSummary.TotalLine(s.Lines));

        if (months.Count == 0) s.Notes.Add($"No statements loaded for {year}.");
        else if (months.Count < 12) s.Notes.Add($"Statements loaded for {months.Count} month(s) of {year}: {string.Join(", ", months.Select(m => m.Month))}.");
        if (months.Any(m => !m.SelfTestPassed)) s.Notes.Add("Some months fail the self-test; check Monthly P&L before relying on these numbers.");
        if (months.Any(m => m.UnmappedLines > 0)) s.Notes.Add("Some sales have no cost rule, so their per-sale costs are missing.");
        return s;
    }
}
