// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using CraftingMargins.Core;

namespace CraftingMargins.Data.Etsy;

public sealed record SyncResult(int Orders, int Cancelled, int Items, int Listings, int LedgerEntries, List<string> ApiMonths, int Calls)
{
    public string Summary =>
        $"{Orders} order(s) updated" + (Cancelled > 0 ? $", {Cancelled} cancelled order(s) removed" : "") +
        $", {Listings} listing(s), {LedgerEntries} ledger entr{(LedgerEntries == 1 ? "y" : "ies")}" +
        (ApiMonths.Count > 0 ? $"; months filled from the API: {string.Join(", ", ApiMonths)}" : "") +
        $" ({Calls} API calls)";
}

/// <summary>
/// Pulls the shop's data from the Etsy API into the same tables the CSV importer fills.
/// Orders: only receipts changed since the last sync.  Listings: all active ones (Etsy's
/// 6-hour freshness rule for listing content).  Ledger: from the start of last month on.
/// </summary>
public sealed class EtsySync(EtsyClient client, Database db, AppSettings settings, TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _tz = timeZone ?? TimeZoneInfo.Local;

    public DateOnly SyncFrom =>
        DateOnly.TryParseExact(settings.ApiSyncFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : new DateOnly(DateTime.Today.Year, 1, 1);

    public Task<long> EnsureShopIdAsync(CancellationToken ct = default) => LookUpShopAsync(client, settings, ct);

    public static async Task<long> LookUpShopAsync(EtsyClient client, AppSettings settings, CancellationToken ct = default)
    {
        if (settings.EtsyShopId is not long id)
        {
            var me = await client.GetAsync<EtsySelf>("users/me", ct: ct);
            settings.EtsyUserId = me.UserId;
            id = me.ShopId is > 0 ? me.ShopId.Value : me.UserId;   // the docs say the user ID also works as the shop ID
            CheckShopMatchesData(settings, id);
            settings.EtsyShopId = id;
        }
        CheckShopMatchesData(settings, id);
        return id;
    }

    /// <summary>
    /// One data folder holds one shop: orders, listings and ledger lines carry no shop of their
    /// own, so a second shop's data would mix in with the first's and every report would be
    /// wrong.  Once data has come from one Etsy shop, another shop is refused.
    /// </summary>
    public static void CheckShopMatchesData(AppSettings settings, long shopId)
    {
        if (settings.DataEtsyShopId is long data && data != shopId)
            throw new EtsyAuthException(
                $"This data folder already holds Etsy shop {data}, and these keys are for shop {shopId}. Mixing two shops in one data folder would make every report wrong. " +
                "To use this shop, move to another data folder first (Settings → Program settings → Move to another folder, and pick an empty one), or delete all data here.");
    }

    public async Task<SyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var shop = await EnsureShopIdAsync(ct);
        settings.DataEtsyShopId ??= shop;   // from here on this folder holds this shop
        var (orders, cancelled, items) = await SyncReceiptsAsync(shop, progress, ct);
        var listings = await SyncListingsAsync(shop, progress, ct);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var ledgerFrom = settings.LastApiSync is DateTime last
            ? new DateOnly(last.Year, last.Month, 1).AddMonths(-1)
            : SyncFrom;
        if (ledgerFrom < SyncFrom) ledgerFrom = SyncFrom;
        var ledger = 0;
        for (var m = new DateOnly(ledgerFrom.Year, ledgerFrom.Month, 1); m <= today; m = m.AddMonths(1))
            ledger += await SyncLedgerMonthAsync(shop, m, progress, ct);

        var apiMonths = settings.UseApiLedger ? db.RebuildApiStatementMonths() : [];
        settings.LastApiSync = DateTime.Now;
        var result = new SyncResult(orders, cancelled, items, listings, ledger, apiMonths, client.CallCount);
        Log.Write("Etsy API sync: " + result.Summary);
        return result;
    }

    private async Task<(int Orders, int Cancelled, int Items)> SyncReceiptsAsync(long shop, IProgress<string>? progress, CancellationToken ct)
    {
        var q = new List<KeyValuePair<string, string>>();
        if (settings.ReceiptsModifiedSince > 0)
            q.Add(new("min_last_modified", (settings.ReceiptsModifiedSince - 86400).ToString(CultureInfo.InvariantCulture)));
        else
            q.Add(new("min_created", Epoch(SyncFrom).ToString(CultureInfo.InvariantCulture)));

        var receipts = await client.GetAllAsync<EtsyReceipt>($"shops/{shop}/receipts", q, progress, "Orders", ct);
        var existing = db.LoadOrders().ToDictionary(o => o.OrderId);
        var existingItems = db.LoadItems().ToDictionary(i => i.TransactionId);
        var orders = new List<Order>();
        var items = new List<OrderItem>();
        var cancelled = new List<string>();

        // Oldest change first, saved every 50 orders with the watermark moved up to match.  The
        // first sync asks Etsy for each shipped order's payment one at a time, so if it's cut off
        // (the daily request limit, a dropped connection) what was fetched is kept and the next
        // sync carries on from there instead of starting again.
        receipts = [.. receipts.OrderBy(r => r.UpdatedTimestamp)];
        int savedOrders = 0, savedItems = 0;
        var itemsByOrder = existingItems.Values.ToLookup(i => i.OrderId);
        void Save(long watermark)
        {
            db.UpsertOrders(orders);
            db.UpsertItems(items);
            // The API gives every line of an order, so a stored line that isn't among them was
            // taken off the order: remove it, or it would go on counting cost and units.
            var current = items.Select(i => i.TransactionId).ToHashSet();
            db.DeleteItems([.. orders.SelectMany(o => itemsByOrder[o.OrderId]).Select(i => i.TransactionId).Where(id => !current.Contains(id))]);
            savedOrders += orders.Count;
            savedItems += items.Count;
            orders.Clear();
            items.Clear();
            if (watermark > 0) settings.ReceiptsModifiedSince = Math.Max(settings.ReceiptsModifiedSince, watermark);
        }

        int n = 0;
        foreach (var r in receipts)
        {
            ct.ThrowIfCancellationRequested();
            if (orders.Count >= 50) Save(r.UpdatedTimestamp - 1);
            if (client.RemainingToday is <= 5)
            {
                Save(r.UpdatedTimestamp - 1);
                if (cancelled.Count > 0) db.DeleteOrders(cancelled);
                throw new EtsyApiException($"Etsy's daily request limit is nearly used up. {savedOrders} order(s) were saved; sync again tomorrow to fetch the rest.");
            }
            var id = r.ReceiptId.ToString(CultureInfo.InvariantCulture);
            if (!EtsyMapper.IsCountable(r))
            {
                if (existing.ContainsKey(id)) cancelled.Add(id);
                continue;
            }
            progress?.Report($"Card fees: order {++n} of {receipts.Count}");
            // Asked for every order: the statement shows Etsy charging the card fee at the sale, not
            // at shipping.  If Etsy has no payment record yet, the fee is filled in by a later sync
            // (shipping changes the order, so it is fetched again).
            List<EtsyPayment> payments;
            try { payments = (await client.GetAsync<EtsyPage<EtsyPayment>>($"shops/{shop}/receipts/{r.ReceiptId}/payments", ct: ct)).Results; }
            catch (EtsyApiException ex) when (!r.IsShipped && ex.Status == System.Net.HttpStatusCode.NotFound) { payments = []; }   // no payment record yet
            var o = EtsyMapper.ToOrder(r, payments, _tz);
            if (existing.TryGetValue(id, out var old))
            {
                // Fields the API doesn't have: keep what a CSV import put there.
                o.ShippingDiscount = old.ShippingDiscount;
                if (o.OrderType.Length == 0) o.OrderType = old.OrderType;
                if (payments.Count == 0 && old.CardProcessingFees != 0)
                {
                    o.CardProcessingFees = old.CardProcessingFees;
                    o.OrderNet = old.OrderNet;
                    o.AdjustedOrderTotal = old.AdjustedOrderTotal;
                    o.AdjustedCardProcessingFees = old.AdjustedCardProcessingFees;
                    o.AdjustedNetOrderAmount = old.AdjustedNetOrderAmount;
                }
            }
            orders.Add(o);
            foreach (var i in EtsyMapper.ToItems(r, settings.IgnoredVariationNames, _tz))
            {
                if (existingItems.TryGetValue(i.TransactionId, out var oldItem))
                {
                    i.DiscountAmount = oldItem.DiscountAmount;
                    i.ShippingDiscount = oldItem.ShippingDiscount;
                    i.CouponCode = oldItem.CouponCode;
                    if (oldItem.ItemTotal != 0) i.ItemTotal = oldItem.ItemTotal;
                }
                items.Add(i);
            }
        }
        Save(receipts.Count > 0 ? receipts.Max(r => r.UpdatedTimestamp) : 0);
        if (cancelled.Count > 0) db.DeleteOrders(cancelled);
        return (savedOrders, cancelled.Count, savedItems);
    }

    private async Task<int> SyncListingsAsync(long shop, IProgress<string>? progress, CancellationToken ct)
    {
        var list = await client.GetAllAsync<EtsyListing>($"shops/{shop}/listings",
            [new("state", "active"), new("includes", "Inventory")], progress, "Listings", ct);
        var listings = list.Select(EtsyMapper.ToListing).Where(l => l.Title.Length > 0)
            .GroupBy(l => l.Key).Select(g => g.First())   // stored by Etsy's listing number, so two with the same title are both kept
            .ToList();
        db.ReplaceApiListings(listings);
        return listings.Count;
    }

    /// <summary>Downloads one month of the payment-account ledger.  Used by the sync and by Ledger compare.</summary>
    public async Task<int> SyncLedgerMonthAsync(long shop, DateOnly month, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        CheckShopMatchesData(settings, shop);
        settings.DataEtsyShopId ??= shop;
        var first = new DateOnly(month.Year, month.Month, 1);
        var q = new List<KeyValuePair<string, string>>
        {
            new("min_created", Epoch(first).ToString(CultureInfo.InvariantCulture)),
            new("max_created", (Epoch(first.AddMonths(1)) - 1).ToString(CultureInfo.InvariantCulture)),
        };
        var entries = await client.GetAllAsync<EtsyLedgerEntry>($"shops/{shop}/payment-account/ledger-entries", q, progress,
            "Ledger " + first.ToString("yyyy-MM", CultureInfo.InvariantCulture), ct);
        db.UpsertLedger(entries.Select(e => EtsyMapper.ToLedger(e, _tz)));
        return entries.Count;
    }

    private long Epoch(DateOnly d)
    {
        var local = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _tz.GetUtcOffset(local)).ToUnixTimeSeconds();
    }
}
