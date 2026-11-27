// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CraftingMargins.Core;

/// <summary>The categories a statement line can land in.</summary>
public static class Category
{
    public const string GrossSales = "Gross Sales";
    public const string SalesTax = "Sales Tax";
    public const string Refunds = "Refunds";
    public const string TransactionFees = "Transaction Fees";
    public const string ProcessingFees = "Processing Fees";
    public const string ListingFees = "Listing Fees";
    public const string OffsiteAds = "Offsite Ads Fees";
    public const string EtsyAds = "Etsy Ads Fees";
    public const string ShippingLabels = "Shipping Labels";
    public const string Excluded = "Excluded";
    public const string Unknown = "Unknown";

    /// <summary>Every category a user may assign (Unknown is never assignable).</summary>
    public static readonly string[] Assignable =
        [GrossSales, SalesTax, Refunds, TransactionFees, ProcessingFees, ListingFees, OffsiteAds, EtsyAds, ShippingLabels, Excluded];
}

/// <summary>
/// One rule: statement Type must equal <see cref="Type"/> and Title must contain
/// <see cref="TitleContains"/> (null or empty = any title).  Case-insensitive.
/// </summary>
public sealed record CategoryRule(string Type, string? TitleContains, string Category, string? Note = null);

/// <summary>
/// Puts each statement line in exactly one category.  User rules are checked
/// before the built-in rules; the first match wins; no match = Unknown (never guessed).
/// </summary>
public sealed class Categorizer
{
    private readonly List<CategoryRule> _rules;

    public Categorizer(IEnumerable<CategoryRule>? userRules = null, IEnumerable<CategoryRule>? builtInRules = null)
    {
        _rules = [.. userRules ?? [], .. builtInRules ?? DefaultRules];
    }

    public IReadOnlyList<CategoryRule> Rules => _rules;

    public string Categorize(StatementLine line) => Categorize(line.Type, line.Title);

    public string Categorize(string type, string title)
    {
        foreach (var r in _rules)
        {
            if (!string.Equals(r.Type.Trim(), type.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(r.TitleContains) ||
                title.Contains(r.TitleContains, StringComparison.OrdinalIgnoreCase))
                return r.Category;
        }
        return Category.Unknown;
    }

    /// <summary>Built-in US rules.  Order matters.</summary>
    public static readonly CategoryRule[] DefaultRules =
    [
        new("Sale", "Payment for Order", Category.GrossSales),
        new("Tax", null, Category.SalesTax, "Includes tax refunds to buyers"),
        new("Buyer Fee", null, Category.SalesTax, "Pass-through, e.g. Colorado Retail Delivery Fee"),
        new("Refund", null, Category.Refunds),
        new("Fee", "Transaction fee", Category.TransactionFees, "Includes credits"),
        new("Fee", "Processing fee", Category.ProcessingFees, "Includes credits"),
        new("Fee", "Listing fee", Category.ListingFees, "Includes credits"),
        new("Marketing", "Offsite Ads", Category.OffsiteAds),
        new("Marketing", "Etsy Ads", Category.EtsyAds),
        new("Shipping", "shipping label", Category.ShippingLabels, "Labels, adjustments and adjustment credits"),
        new("Shipping", "Insurance", Category.ShippingLabels, "Package insurance bought with a label (Shipsurance); counts with that label"),
        new("Deposit", null, Category.Excluded, "Transfer to bank"),
        new("Payment", "Card Payment", Category.Excluded, "Paying a negative Etsy balance"),
    ];

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<CategoryRule> LoadRules(string json) =>
        JsonSerializer.Deserialize<List<CategoryRule>>(json) ?? [];

    public static string SaveRules(IEnumerable<CategoryRule> rules) =>
        JsonSerializer.Serialize(rules, JsonOpts);
}

/// <summary>Pulls IDs out of statement Info and Title text.</summary>
public static partial class StatementIds
{
    public static void Fill(StatementLine line)
    {
        line.OrderId = Match(OrderRx(), line.Info) ?? Match(OrderRx(), line.Title);
        line.ListingId = Match(ListingRx(), line.Info);
        line.LabelId = Match(LabelRx(), line.Info);
        line.AdjustmentId = Match(AdjustmentRx(), line.Info);
    }

    private static string? Match(Regex rx, string text)
    {
        var m = rx.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"Order #(\d+)", RegexOptions.IgnoreCase)] private static partial Regex OrderRx();
    [GeneratedRegex(@"Listing #(\d+)", RegexOptions.IgnoreCase)] private static partial Regex ListingRx();
    [GeneratedRegex(@"Label #(\d+)", RegexOptions.IgnoreCase)] private static partial Regex LabelRx();
    [GeneratedRegex(@"Adjustment #(\d+)", RegexOptions.IgnoreCase)] private static partial Regex AdjustmentRx();
}
