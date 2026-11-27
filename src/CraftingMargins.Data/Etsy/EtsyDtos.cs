// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftingMargins.Data.Etsy;

// Only the fields the app uses are declared.  Buyer names, emails, addresses, messages and
// buyer user IDs are deliberately absent, so they are discarded while parsing and never
// held in an object, let alone stored.

public static class EtsyJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientStringConverter() },
    };
}

/// <summary>
/// Reads a text field whether Etsy sends it as text, a number or true/false.  Etsy's own
/// documentation isn't always right about which (the ledger's reference_id is documented
/// as text but arrives as a number), and one surprise like that shouldn't stop a sync.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string>
{
    public override bool HandleNull => false;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        _ => throw new JsonException($"Expected text but found {reader.TokenType}."),
    };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public sealed class EtsyMoney
{
    public long Amount { get; set; }
    public int Divisor { get; set; } = 100;
    public string? CurrencyCode { get; set; }
    public decimal Value => Divisor == 0 ? Amount : (decimal)Amount / Divisor;
}

public sealed class EtsyPage<T>
{
    public int Count { get; set; }
    public List<T> Results { get; set; } = [];
}

public sealed class EtsySelf
{
    public long UserId { get; set; }
    public long? ShopId { get; set; }
}

public sealed class EtsyReceipt
{
    public long ReceiptId { get; set; }
    public string? Status { get; set; }
    public bool IsPaid { get; set; }
    public bool IsShipped { get; set; }
    public long CreatedTimestamp { get; set; }
    public long UpdatedTimestamp { get; set; }
    public EtsyMoney? Grandtotal { get; set; }
    public EtsyMoney? Subtotal { get; set; }
    public EtsyMoney? TotalPrice { get; set; }
    public EtsyMoney? TotalShippingCost { get; set; }
    public EtsyMoney? TotalTaxCost { get; set; }
    public EtsyMoney? DiscountAmt { get; set; }
    public List<EtsyShipment> Shipments { get; set; } = [];
    public List<EtsyTransaction> Transactions { get; set; } = [];
}

public sealed class EtsyShipment
{
    public long? ShipmentNotificationTimestamp { get; set; }
}

public sealed class EtsyTransaction
{
    public long TransactionId { get; set; }
    public long ReceiptId { get; set; }
    public long? ListingId { get; set; }
    public string? Title { get; set; }
    public int Quantity { get; set; }
    public EtsyMoney? Price { get; set; }
    public EtsyMoney? ShippingCost { get; set; }
    public string? Sku { get; set; }
    public long? CreatedTimestamp { get; set; }
    public long? ShippedTimestamp { get; set; }
    public List<EtsyTransactionVariation> Variations { get; set; } = [];
}

public sealed class EtsyTransactionVariation
{
    public long? PropertyId { get; set; }
    public string? FormattedName { get; set; }
    public string? FormattedValue { get; set; }
}

public sealed class EtsyPayment
{
    public long PaymentId { get; set; }
    public long ReceiptId { get; set; }
    public EtsyMoney? AmountFees { get; set; }
    public EtsyMoney? AdjustedGross { get; set; }
    public EtsyMoney? AdjustedFees { get; set; }
    public EtsyMoney? AdjustedNet { get; set; }
}

public sealed class EtsyLedgerEntry
{
    public long EntryId { get; set; }
    public long SequenceNumber { get; set; }
    public long Amount { get; set; }
    public string? Currency { get; set; }
    public string? Description { get; set; }
    public long CreatedTimestamp { get; set; }
    public string? LedgerType { get; set; }
    public string? ReferenceType { get; set; }
    public string? ReferenceId { get; set; }
    public long? ParentEntryId { get; set; }
}

public sealed class EtsyListing
{
    public long ListingId { get; set; }
    public string? Title { get; set; }
    public string? State { get; set; }
    public int Quantity { get; set; }
    public int? NumFavorers { get; set; }
    public List<string> Materials { get; set; } = [];
    public List<string> Skus { get; set; } = [];
    public EtsyMoney? Price { get; set; }
    public EtsyInventory? Inventory { get; set; }
}

public sealed class EtsyInventory
{
    public List<EtsyProduct> Products { get; set; } = [];
}

public sealed class EtsyProduct
{
    public string? Sku { get; set; }
    public bool IsDeleted { get; set; }
    public List<EtsyOffering> Offerings { get; set; } = [];
    public List<EtsyPropertyValue> PropertyValues { get; set; } = [];
}

public sealed class EtsyOffering
{
    public int Quantity { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public EtsyMoney? Price { get; set; }
}

public sealed class EtsyPropertyValue
{
    public string? PropertyName { get; set; }
    public List<string> Values { get; set; } = [];
}
