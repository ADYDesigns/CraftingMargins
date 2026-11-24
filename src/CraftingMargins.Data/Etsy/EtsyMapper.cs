// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using CraftingMargins.Core;

namespace CraftingMargins.Data.Etsy;

/// <summary>Turns Etsy API objects into the same records the CSV importer makes.</summary>
public static class EtsyMapper
{
    public static DateOnly ToDate(long epochSeconds, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(epochSeconds), tz).DateTime);

    private static decimal V(EtsyMoney? m) => m?.Value ?? 0m;

    /// <summary>Cancelled and unpaid receipts aren't in Etsy's order export either, so they are left out.</summary>
    public static bool IsCountable(EtsyReceipt r) =>
        r.IsPaid && !string.Equals(r.Status, "canceled", StringComparison.OrdinalIgnoreCase);

    public static Order ToOrder(EtsyReceipt r, IReadOnlyList<EtsyPayment> payments, TimeZoneInfo tz)
    {
        var fees = payments.Sum(p => V(p.AmountFees));
        var total = V(r.Grandtotal);
        var adjusted = payments.Any(p => p.AdjustedGross is not null);
        return new Order
        {
            OrderId = r.ReceiptId.ToString(CultureInfo.InvariantCulture),
            Source = "Etsy",
            SaleDate = ToDate(r.CreatedTimestamp, tz),
            DateShipped = ShippedDate(r, tz),
            NumberOfItems = r.Transactions.Sum(t => t.Quantity),
            Currency = r.Grandtotal?.CurrencyCode ?? "USD",
            OrderValue = V(r.TotalPrice),
            DiscountAmount = V(r.DiscountAmt),
            Shipping = V(r.TotalShippingCost),
            SalesTax = V(r.TotalTaxCost),
            OrderTotal = total,
            Status = TitleCase(r.Status),
            CardProcessingFees = fees,
            OrderNet = total - fees,
            AdjustedOrderTotal = adjusted ? payments.Sum(p => V(p.AdjustedGross)) : null,
            AdjustedCardProcessingFees = adjusted ? payments.Sum(p => V(p.AdjustedFees)) : null,
            AdjustedNetOrderAmount = adjusted ? payments.Sum(p => V(p.AdjustedNet)) : null,
        };
    }

    public static List<OrderItem> ToItems(EtsyReceipt r, IReadOnlyCollection<string> ignoredVariations, TimeZoneInfo tz)
    {
        var shipped = ShippedDate(r, tz);
        return r.Transactions.Select(t => new OrderItem
        {
            TransactionId = t.TransactionId.ToString(CultureInfo.InvariantCulture),
            OrderId = r.ReceiptId.ToString(CultureInfo.InvariantCulture),
            ListingId = t.ListingId?.ToString(CultureInfo.InvariantCulture) ?? "",
            SaleDate = ToDate(t.CreatedTimestamp is > 0 ? t.CreatedTimestamp.Value : r.CreatedTimestamp, tz),
            DateShipped = t.ShippedTimestamp is > 0 ? ToDate(t.ShippedTimestamp.Value, tz) : shipped,
            ItemName = EtsyText.Clean(t.Title),
            Quantity = t.Quantity,
            Price = V(t.Price),
            OrderShipping = V(r.TotalShippingCost),
            ItemTotal = V(t.Price) * t.Quantity,
            Currency = t.Price?.CurrencyCode ?? "USD",
            Variations = VariationText(t.Variations, ignoredVariations),
            Sku = t.Sku ?? "",
        }).ToList();
    }

    /// <summary>
    /// "Name:Value,Name:Value" like the CSV.  Personalization (free text the buyer typed) is
    /// dropped here, before anything is stored: every name in the ignore list, and anything
    /// Etsy itself calls personalization.
    /// </summary>
    public static string VariationText(IEnumerable<EtsyTransactionVariation> vars, IReadOnlyCollection<string> ignored)
    {
        var pairs = vars
            .Where(v => !string.IsNullOrWhiteSpace(v.FormattedName))
            .Where(v => !v.FormattedName!.Contains("Personaliz", StringComparison.OrdinalIgnoreCase))
            .Select(v => new KeyValuePair<string, string>(EtsyText.Clean(v.FormattedName), EtsyText.Clean(v.FormattedValue)));
        return Core.Variations.Strip(Core.Variations.Format(pairs), ignored);
    }

    private static DateOnly? ShippedDate(EtsyReceipt r, TimeZoneInfo tz)
    {
        var stamps = r.Transactions.Where(t => t.ShippedTimestamp is > 0).Select(t => t.ShippedTimestamp!.Value)
            .Concat(r.Shipments.Where(s => s.ShipmentNotificationTimestamp is > 0).Select(s => s.ShipmentNotificationTimestamp!.Value))
            .ToList();
        if (stamps.Count > 0) return ToDate(stamps.Min(), tz);
        return null;
    }

    public static Listing ToListing(EtsyListing l)
    {
        var listing = new Listing
        {
            ListingId = l.ListingId.ToString(CultureInfo.InvariantCulture),
            Title = EtsyText.Clean(l.Title),
            Price = V(l.Price),
            Quantity = l.Quantity,
            Sku = string.Join(", ", l.Skus.Where(s => s.Length > 0)),
            Materials = string.Join(", ", l.Materials),
            State = l.State ?? "",
            NumFavorers = l.NumFavorers,
        };
        var products = l.Inventory?.Products.Where(p => !p.IsDeleted).ToList() ?? [];
        foreach (var name in products.SelectMany(p => p.PropertyValues).Select(v => EtsyText.Clean(v.PropertyName)).Where(n => n.Length > 0).Distinct())
        {
            var values = products.SelectMany(p => p.PropertyValues)
                .Where(v => EtsyText.Clean(v.PropertyName) == name)
                .SelectMany(v => v.Values).Select(EtsyText.Clean).Distinct().ToList();
            listing.Variations.Add(new ListingVariation(name, values));
        }
        foreach (var p in products)
        {
            var offer = p.Offerings.FirstOrDefault(o => !o.IsDeleted);
            if (offer is null) continue;
            var options = p.PropertyValues
                .Where(v => !string.IsNullOrWhiteSpace(v.PropertyName))
                .ToDictionary(v => EtsyText.Clean(v.PropertyName), v => string.Join(", ", v.Values.Select(EtsyText.Clean)));
            listing.Offerings.Add(new ListingOffering(options, V(offer.Price), offer.Quantity, offer.IsEnabled));
        }
        return listing;
    }

    public static LedgerEntry ToLedger(EtsyLedgerEntry e, TimeZoneInfo tz) => new()
    {
        EntryId = e.EntryId,
        SequenceNumber = e.SequenceNumber,
        Date = ToDate(e.CreatedTimestamp, tz),
        Amount = e.Amount / 100m,   // ledger amounts are in cents
        Currency = e.Currency ?? "USD",
        Description = EtsyText.Clean(e.Description),
        LedgerType = e.LedgerType ?? "",
        ReferenceType = e.ReferenceType ?? "",
        ReferenceId = e.ReferenceId ?? "",
        ParentEntryId = e.ParentEntryId,
    };

    private static string TitleCase(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
}
