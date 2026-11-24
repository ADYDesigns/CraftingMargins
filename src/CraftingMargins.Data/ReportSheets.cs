// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Data;

/// <summary>Turns reports into export sheets (Excel workbook or CSV).</summary>
public static class ReportSheets
{
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static Sheet Tax(TaxSummary t, bool byScheduleC = false)
    {
        var s = new Sheet(byScheduleC ? $"Schedule C {t.Year}" : $"Tax summary {t.Year}");
        s.Add([byScheduleC ? "Schedule C line" : "Section", byScheduleC ? "Made up of" : "Line", .. MonthNames, t.Year.ToString(), "Counts in total", "Schedule C line (suggested)", "Note"]);
        foreach (var l in byScheduleC ? t.ByScheduleC() : t.Lines)
        {
            object?[] row = [byScheduleC ? l.Name : l.Section, byScheduleC ? l.Note : l.Name, .. l.Months.Cast<object?>(), l.Total,
                             l.IsTotal ? "" : l.InTotal ? "Yes" : "No", byScheduleC ? "" : l.ScheduleCLine, byScheduleC ? "" : l.Note];
            if (l.IsTotal) s.AddBold(row); else s.Add(row);
        }
        s.Add();
        s.Add($"Materials, components and packaging counted as: {(t.Basis == SuppliesBasis.Used ? "used per sale" : "bought (expense log)")}.");
        foreach (var n in t.Notes) s.Add(n);
        s.Add("Not tax advice. Schedule C lines are suggestions; check them with a tax professional.");
        return s;
    }

    public static Sheet Pnl(IEnumerable<MonthlyPnL> months)
    {
        var s = new Sheet("Monthly P&L");
        s.Add("Month", "Self-test", "Orders", "Units", "Net sales", "Refunds", "Transaction fees", "Processing fees", "Listing fees",
            "Offsite Ads", "Etsy Ads", "Shipping labels", "Extra shipping", "Material", "Waste allowance", "Components", "Machine time", "Your own time",
            "Fixed costs", "Packaging", "Sales outside Etsy", "Fees outside Etsy", "Net profit");
        foreach (var m in months)
            s.Add(m.Month, m.SelfTestPassed ? "Pass" : "Fail", m.Orders, m.Units, m.NetSales, m.Refunds, m.TransactionFees, m.ProcessingFees,
                m.ListingFees, m.OffsiteAds, m.EtsyAds, m.ShippingLabels, m.ExtraShipping, m.Material, m.Waste, m.Components,
                m.MachineCost, m.OwnCost, m.FixedCosts, m.Packaging, m.OtherSales, m.OtherFees, m.NetProfit);
        return s;
    }

    public static Sheet Orders(IEnumerable<OrderProfit> orders, string name = "Orders")
    {
        var s = new Sheet(name);
        s.Add("Sold", "Shipped", "Order", "Items", "Units", "Item price", "Discount", "Coupon", "Shipping charged", "Sales tax",
            "Offsite Ad", "Transaction fees", "Processing fees", "Offsite Ads", "Listing fees", "Label", "Extra shipping",
            "Costs", "Packaging", "Refunds", "Profit", "Note");
        foreach (var o in orders.OrderBy(o => o.SaleDate))
            s.Add(o.SaleDate, o.DateShipped, o.OrderId, o.Items, o.Units, o.ItemPrice, o.Discount, o.Coupon, o.ShippingCharged, o.SalesTax,
                o.AdAttributed ? "Yes" : "", o.TransactionFees, o.ProcessingFees, o.OffsiteAds, o.ListingFees, o.Label, o.ExtraShipping,
                o.Costs, o.Packaging, o.Refunds, o.ProfitIfKnown, o.Status);
        return s;
    }

    public static Sheet Expenses(IEnumerable<Expense> expenses, string name = "Expenses")
    {
        var s = new Sheet(name);
        s.Add("Date", "Amount", "Category", "Vendor", "Note");
        foreach (var e in expenses.OrderBy(e => e.Date))
            s.Add(e.Date, e.Amount, e.Category, e.Vendor, e.Note);
        return s;
    }

    public static Sheet Products(IEnumerable<ProductSales> rows)
    {
        var s = new Sheet("Sales by product");
        s.Add("Listing", "Listing ID", "Orders", "Units", "Offsite Ad share", "Total profit", "Average profit", "Worst order", "Top options");
        foreach (var r in rows)
            s.Add(r.Name, r.ListingId, r.Orders, r.Units, $"{r.AdShare:0%}", r.Profit, r.AverageProfit, r.WorstOrder, r.TopOptions);
        return s;
    }

    public static Sheet Pricing(IEnumerable<PriceRow> rows, string name = "Pricing check")
    {
        var s = new Sheet(name);
        s.Add("Listing", "Options", "List price", "Price used", "Shipping charged", "Label", "Cost", "Organic profit", "Worst-case profit",
            "Lowest price for your goal", "Making hours", "Organic $ per making hour", "Price from", "Status");
        foreach (var r in rows)
            s.Add(r.Title, r.OptionsText, r.ListPrice, r.Price, r.Shipping, r.Label, r.Cost, r.Organic, r.WorstCase, r.MinPrice,
                r.MakingHours == 0 ? null : r.MakingHours, r.OrganicPerHour, r.PriceSource, r.Status);
        return s;
    }

    /// <summary>The year-end workbook: tax summary both ways, P&amp;L, that year's orders and expenses.</summary>
    public static List<Sheet> YearWorkbook(TaxSummary tax, IEnumerable<MonthlyPnL> months, IEnumerable<OrderProfit> orders, IEnumerable<Expense> expenses)
    {
        var y = tax.Year;
        return
        [
            Tax(tax),
            Tax(tax, byScheduleC: true),
            Pnl(months.Where(m => m.Month.StartsWith($"{y}-"))),
            Orders(orders.Where(o => o.SaleDate.Year == y), $"Orders {y}"),
            Expenses(expenses.Where(e => e.Date.Year == y), $"Expenses {y}"),
        ];
    }
}
