// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

// Imported data.  Every record carries ShopId so more shops can be added later
// without changing the database; today it is always "default".

/// <summary>One line of an Etsy monthly statement (or, later, an API ledger entry).</summary>
public sealed class StatementLine
{
    public long Id { get; set; }
    public string ShopId { get; set; } = Shop.Default;
    public DateOnly Date { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Info { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal Amount { get; set; }
    public decimal FeesAndTaxes { get; set; }
    public decimal Net { get; set; }
    public string? OrderId { get; set; }
    public string? ListingId { get; set; }
    public string? LabelId { get; set; }
    public string? AdjustmentId { get; set; }
    public string SourceFile { get; set; } = "";

    /// <summary>Statement month as "yyyy-MM".</summary>
    public string Month => Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One order (from EtsySoldOrders).  Buyer details are never stored.</summary>
public sealed class Order
{
    public string ShopId { get; set; } = Shop.Default;
    public string OrderId { get; set; } = "";
    /// <summary>Sales channel.  Always "Etsy" today; other channels may come later.</summary>
    public string Source { get; set; } = "Etsy";
    public DateOnly SaleDate { get; set; }
    public DateOnly? DateShipped { get; set; }
    public int NumberOfItems { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal OrderValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingDiscount { get; set; }
    public decimal Shipping { get; set; }
    public decimal SalesTax { get; set; }
    public decimal OrderTotal { get; set; }
    public string Status { get; set; } = "";
    public decimal CardProcessingFees { get; set; }
    public decimal OrderNet { get; set; }
    public decimal? AdjustedOrderTotal { get; set; }
    public decimal? AdjustedCardProcessingFees { get; set; }
    public decimal? AdjustedNetOrderAmount { get; set; }
    public string OrderType { get; set; } = "";
    /// <summary>User-entered shipping cost not on the Etsy statement (label bought elsewhere).  Kept on re-import.</summary>
    public decimal ExtraShippingCost { get; set; }

    /// <summary>
    /// A sale made outside Etsy (craft fair, direct sale), entered by hand.  Source = "Other",
    /// OrderType = where it was sold, CardProcessingFees = that channel's fees, Status = a note.
    /// Its money is known straight away; it never appears on an Etsy statement.
    /// </summary>
    public bool IsOther => Source == OtherSource;
    public const string OtherSource = "Other";
}

/// <summary>One sold item line (from EtsySoldOrderItems).</summary>
public sealed class OrderItem
{
    public string ShopId { get; set; } = Shop.Default;
    public string TransactionId { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string ListingId { get; set; } = "";
    public DateOnly SaleDate { get; set; }
    public DateOnly? DateShipped { get; set; }
    public string ItemName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingDiscount { get; set; }
    public decimal OrderShipping { get; set; }
    public decimal ItemTotal { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>Variations as cleaned "Name:Value" pairs (see <see cref="Variations"/>).</summary>
    public string Variations { get; set; } = "";
    public string Sku { get; set; } = "";
    public string CouponCode { get; set; } = "";
}

/// <summary>
/// What a listing is known by.  Etsy's listing number wherever there is one (sold items and
/// the API both carry it).  The listings download has no listing number, so a listing known
/// only from that file gets a stand-in key made from its title until it sells or is synced.
/// </summary>
public static class ListingKey
{
    public const string TitlePrefix = "title:";
    public static string ForTitle(string title) => TitlePrefix + EtsyText.Clean(title).ToLowerInvariant();
    public static bool IsTitle(string? id) => id is not null && id.StartsWith(TitlePrefix, StringComparison.Ordinal);
}

/// <summary>One listing, from the Etsy API or from EtsyListingsDownload (which has no Listing ID).</summary>
public sealed class Listing
{
    public string ShopId { get; set; } = Shop.Default;
    /// <summary>The Etsy listing number if known, else a stand-in made from the title.  Unique per shop.</summary>
    public string Key => string.IsNullOrEmpty(ListingId) ? ListingKey.ForTitle(Title) : ListingId;
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Sku { get; set; } = "";
    public string Materials { get; set; } = "";
    /// <summary>Variation names and their offered values.</summary>
    public List<ListingVariation> Variations { get; set; } = [];

    // Only filled by the Etsy API (the CSV download has none of these).
    public string? ListingId { get; set; }
    public string State { get; set; } = "";
    public int? NumFavorers { get; set; }
    /// <summary>The price of every variation combination.</summary>
    public List<ListingOffering> Offerings { get; set; } = [];
}

public sealed record ListingVariation(string Name, List<string> Values);

/// <summary>One buyable combination of a listing, e.g. Color:Red + Size:Large, and its price.</summary>
public sealed record ListingOffering(Dictionary<string, string> Options, decimal Price, int Quantity, bool Enabled);

/// <summary>
/// One entry of the shop's payment-account ledger (Etsy API).  The API equivalent of a
/// statement line.  Amounts are converted from cents.
/// </summary>
public sealed class LedgerEntry
{
    public string ShopId { get; set; } = Shop.Default;
    public long EntryId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Description { get; set; } = "";
    public string LedgerType { get; set; } = "";
    public string ReferenceType { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public long? ParentEntryId { get; set; }
    public long SequenceNumber { get; set; }

    public string Month => Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}

public static class Shop
{
    public const string Default = "default";
}
