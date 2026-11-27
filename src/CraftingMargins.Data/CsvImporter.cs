// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.Text.RegularExpressions;
using CraftingMargins.Core;
using CsvHelper;
using CsvHelper.Configuration;

namespace CraftingMargins.Data;

/// <summary>What a dropped CSV file is.  The last three are the app's own cost files (see <see cref="CostsFile"/>).</summary>
public enum CsvKind { Unknown, Statement, OrderItems, Orders, Listings, Materials, Parts, Rules }

public sealed record ImportResult(string FileName, CsvKind Kind, int Rows, string Summary, bool Ok);

/// <summary>
/// Reads Etsy CSV exports.  Files are recognized by their header row, not their name.
/// Buyer names, user IDs and addresses are never read, so they can't be stored.
/// </summary>
public sealed partial class CsvImporter(Database db, AppSettings settings, Categorizer? categorizer = null)
{
    private readonly Categorizer _categorizer = categorizer ?? new Categorizer();

    private static readonly string[] DateFormats =
        ["MMMM d, yyyy", "MMM d, yyyy", "MM/dd/yy", "M/d/yy", "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"];

    public static CsvKind Detect(IReadOnlyCollection<string> header)
    {
        bool Has(params string[] cols) => cols.All(c => header.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (Has("Date", "Type", "Title", "Info", "Amount", "Fees & Taxes", "Net")) return CsvKind.Statement;
        if (Has("Transaction ID", "Listing ID", "Order ID", "Item Name", "Quantity", "Price")) return CsvKind.OrderItems;
        if (Has("Order ID", "Order Value", "Card Processing Fees", "Sales Tax")) return CsvKind.Orders;
        if (Has("TITLE", "PRICE", "VARIATION 1 NAME")) return CsvKind.Listings;
        if (Has(CostsFile.MaterialsHeader)) return CsvKind.Materials;
        if (Has(CostsFile.PartsHeader)) return CsvKind.Parts;
        if (Has(CostsFile.RulesHeader)) return CsvKind.Rules;
        return CsvKind.Unknown;
    }

    public List<ImportResult> ImportFiles(IEnumerable<string> paths)
    {
        var results = new List<ImportResult>();
        // Files dropped together are read in the order they depend on each other, whatever order
        // they were picked in: materials before the parts that use them, parts before the rules
        // that use them, and listings before sold items (whose option text is read with the
        // listing's option names).
        CsvKind[] order = [CsvKind.Materials, CsvKind.Parts, CsvKind.Rules, CsvKind.Listings, CsvKind.Orders, CsvKind.OrderItems, CsvKind.Statement, CsvKind.Unknown];
        foreach (var p in paths.Select(p => (Path: p, Kind: PeekKind(p))).OrderBy(x => Array.IndexOf(order, x.Kind)).Select(x => x.Path).ToList())
        {
            try
            {
                // The cost workbook holds all three cost files (materials, parts, rules) in one .xlsx.
                if (CostsWorkbook.IsWorkbook(p)) results.AddRange(CostsWorkbook.Import(db, p));
                else results.Add(ImportFile(p));
            }
            catch (Exception ex)
            {
                // The message can quote a cell from the file (an amount), and the log never holds money values: log the kind of error only.
                Log.Write($"Import failed for {Path.GetFileName(p)}: {ex.GetType().Name}");
                results.Add(new(Path.GetFileName(p), CsvKind.Unknown, 0, "Could not import: " + ex.Message, false));
            }
        }
        return results;
    }

    /// <summary>What kind of file this is, from its header row only.  Unknown if it can't be read (the import itself then says why).</summary>
    public static CsvKind PeekKind(string path)
    {
        if (CostsWorkbook.IsWorkbook(path)) return CsvKind.Materials;     // read with the cost files, first
        try
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { BadDataFound = null, MissingFieldFound = null, TrimOptions = TrimOptions.Trim });
            if (!csv.Read()) return CsvKind.Unknown;
            csv.ReadHeader();
            return Detect(csv.HeaderRecord ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CsvHelperException) { return CsvKind.Unknown; }
    }

    public ImportResult ImportFile(string path)
    {
        var name = Path.GetFileName(path);
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        });
        if (!csv.Read()) return new(name, CsvKind.Unknown, 0, "The file is empty.", false);
        csv.ReadHeader();
        var header = csv.HeaderRecord ?? [];
        var kind = Detect(header);
        var result = kind switch
        {
            CsvKind.Statement => ImportStatement(csv, name),
            CsvKind.OrderItems => ImportItems(csv, name),
            CsvKind.Orders => ImportOrders(csv, name),
            CsvKind.Listings => ImportListings(csv, name),
            CsvKind.Materials => CostsFile.ImportMaterials(db, csv, name),
            CsvKind.Parts => CostsFile.ImportParts(db, csv, name),
            CsvKind.Rules => CostsFile.ImportRules(db, csv, name),
            _ => new ImportResult(name, CsvKind.Unknown, 0, "Not a file this app recognizes: an Etsy statement, sold orders, sold order items or listings file, or one of the app's own cost files.", false),
        };
        if (result.Ok) db.LogImport(name, kind.ToString(), result.Rows, result.Summary);
        Log.Write($"Imported {name}: {kind}, {result.Rows} rows. {result.Summary}");
        return result;
    }

    private static string Get(CsvReader csv, string col) => csv.TryGetField<string>(col, out var v) ? EtsyText.Clean(v) : "";

    private static DateOnly ParseDate(string text) =>
        DateOnly.ParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static DateOnly? ParseDateOrNull(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseDate(text);

    private static decimal? MoneyOrNull(string text) => string.IsNullOrWhiteSpace(text) ? null : Money.Parse(text);

    private static int ParseInt(string text) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private ImportResult ImportStatement(CsvReader csv, string name)
    {
        var lines = new List<StatementLine>();
        while (csv.Read())
        {
            var line = new StatementLine
            {
                Date = ParseDate(Get(csv, "Date")),
                Type = Get(csv, "Type"),
                Title = Get(csv, "Title"),
                Info = Get(csv, "Info"),
                Currency = Get(csv, "Currency") is { Length: > 0 } cur ? cur : "USD",
                Amount = Money.Parse(Get(csv, "Amount")),
                FeesAndTaxes = Money.Parse(Get(csv, "Fees & Taxes")),
                Net = Money.Parse(Get(csv, "Net")),
                SourceFile = name,
            };
            StatementIds.Fill(line);
            lines.Add(line);
        }

        // A statement file is one month.  Re-importing replaces that month (files grow when re-downloaded).
        var months = lines.Select(l => l.Month).Distinct().ToList();
        if (months.Count == 0 && StatementFileName().Match(name) is { Success: true } m)
            months.Add($"{m.Groups[1].Value}-{int.Parse(m.Groups[2].Value):00}");

        // Statement lines have no number of their own, so a month is replaced as a whole.  That
        // must never quietly throw lines away: a month already stored is only replaced by a file
        // that has at least as many lines for it.  A file with fewer is an older download, a
        // partial one, or a file for the month next door with a stray line in this one.
        var stored = db.LoadStatement().Where(l => l.SourceFile != LedgerLines.ApiSource).ToLookup(l => l.Month);
        static string Key(StatementLine l) => string.Join('\u001f', Database.D(l.Date), l.Type, l.Title, l.Info, Database.M(l.Amount), Database.M(l.FeesAndTaxes), Database.M(l.Net));
        var write = new List<string>();
        var notes = new List<string>();
        var conflict = false;
        foreach (var month in months)
        {
            var incoming = lines.Where(l => l.Month == month).ToList();
            var have = stored[month].ToList();
            if (have.Count == 0 || incoming.Count >= have.Count) { write.Add(month); continue; }

            // Fewer lines than are stored.  If every one of them is already stored, this file adds nothing.
            var left = have.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
            var allStored = incoming.All(l => left.TryGetValue(Key(l), out var n) && n > 0 && (left[Key(l)] = n - 1) >= 0);
            if (allStored)
                notes.Add($"{month} was left as it is: the {have.Count} lines already stored include all {incoming.Count} from this file (an older or partial download)");
            else
            {
                conflict = true;
                notes.Add($"{month} was NOT changed: this file has {incoming.Count} lines for it, fewer than the {have.Count} stored, and some of them differ. " +
                          "Download that month's statement again from Etsy and import the fresh file");
            }
        }
        var writing = lines.Where(l => write.Contains(l.Month)).ToList();
        if (write.Count > 0) db.ReplaceStatementMonths(write, writing);

        var unknown = writing.Count(l => _categorizer.Categorize(l) == Category.Unknown);
        var summary = (write.Count > 0 ? $"Statement for {string.Join(", ", write)}: {writing.Count} lines (month replaced)" : "Statement: nothing new") +
                      (unknown > 0 ? $", {unknown} line(s) of an unknown type" : "") +
                      (notes.Count > 0 ? ".  " + string.Join(".  ", notes) : "");
        return new(name, CsvKind.Statement, writing.Count, summary, !conflict);
    }

    private ImportResult ImportOrders(CsvReader csv, string name)
    {
        var orders = new List<Order>();
        while (csv.Read())
        {
            orders.Add(new Order
            {
                OrderId = Get(csv, "Order ID"),
                SaleDate = ParseDate(Get(csv, "Sale Date")),
                DateShipped = ParseDateOrNull(Get(csv, "Date Shipped")),
                NumberOfItems = ParseInt(Get(csv, "Number of Items")),
                Currency = Get(csv, "Currency") is { Length: > 0 } cur ? cur : "USD",
                OrderValue = Money.Parse(Get(csv, "Order Value")),
                DiscountAmount = Money.Parse(Get(csv, "Discount Amount")),
                ShippingDiscount = Money.Parse(Get(csv, "Shipping Discount")),
                Shipping = Money.Parse(Get(csv, "Shipping")),
                SalesTax = Money.Parse(Get(csv, "Sales Tax")),
                OrderTotal = Money.Parse(Get(csv, "Order Total")),
                Status = Get(csv, "Status"),
                CardProcessingFees = Money.Parse(Get(csv, "Card Processing Fees")),
                OrderNet = Money.Parse(Get(csv, "Order Net")),
                AdjustedOrderTotal = MoneyOrNull(Get(csv, "Adjusted Order Total")),
                AdjustedCardProcessingFees = MoneyOrNull(Get(csv, "Adjusted Card Processing Fees")),
                AdjustedNetOrderAmount = MoneyOrNull(Get(csv, "Adjusted Net Order Amount")),
                OrderType = Get(csv, "Order Type"),
                // Deliberately not read: Buyer User ID, Full/First/Last Name, Street, City, State, Zip, Country, Buyer.
            });
        }
        db.UpsertOrders(orders);
        return new(name, CsvKind.Orders, orders.Count, $"{orders.Count} orders added or updated", true);
    }

    private ImportResult ImportItems(CsvReader csv, string name)
    {
        var items = new List<OrderItem>();
        var ignored = settings.IgnoredVariationNames;

        // The option names each listing really has, so a buyer-typed value like
        // "Est. 2020, time: 5pm" isn't mistaken for another option called "time".
        // Found by listing number (API listings) or by title (the listings file).
        var listings = db.LoadListings();
        var namesById = listings.Where(l => !string.IsNullOrEmpty(l.ListingId)).GroupBy(l => l.ListingId!).ToDictionary(g => g.Key, g => g.First());
        var namesByTitle = listings.GroupBy(l => ListingKey.ForTitle(l.Title)).ToDictionary(g => g.Key, g => g.First());
        string[] alwaysKnown = [.. ignored, .. settings.KeptVariationNames];
        IReadOnlyCollection<string> KnownNames(string listingId, string itemName)
        {
            var l = namesById.GetValueOrDefault(listingId) ?? namesByTitle.GetValueOrDefault(ListingKey.ForTitle(itemName));
            return l is null || l.Variations.Count == 0 ? [] : [.. l.Variations.Select(v => v.Name), .. alwaysKnown];
        }
        string ReadVariations()
        {
            var pairs = Variations.Parse(Get(csv, "Variations"), KnownNames(Get(csv, "Listing ID"), Get(csv, "Item Name")));
            return Variations.Format(pairs.Where(p => !ignored.Contains(p.Key, StringComparer.OrdinalIgnoreCase)));
        }

        while (csv.Read())
        {
            items.Add(new OrderItem
            {
                TransactionId = Get(csv, "Transaction ID"),
                OrderId = Get(csv, "Order ID"),
                ListingId = Get(csv, "Listing ID"),
                SaleDate = ParseDate(Get(csv, "Sale Date")),
                DateShipped = ParseDateOrNull(Get(csv, "Date Shipped")),
                ItemName = Get(csv, "Item Name"),
                Quantity = ParseInt(Get(csv, "Quantity")),
                Price = Money.Parse(Get(csv, "Price")),
                DiscountAmount = Money.Parse(Get(csv, "Discount Amount")),
                ShippingDiscount = Money.Parse(Get(csv, "Shipping Discount")),
                OrderShipping = Money.Parse(Get(csv, "Order Shipping")),
                ItemTotal = Money.Parse(Get(csv, "Item Total")),
                Currency = Get(csv, "Currency") is { Length: > 0 } cur ? cur : "USD",
                Variations = ReadVariations(),
                Sku = Get(csv, "SKU"),
                CouponCode = Get(csv, "Coupon Code"),
                // Deliberately not read: Buyer, Ship Name, Ship Address1/2, Ship City/State/Zipcode/Country.
            });
        }
        db.UpsertItems(items);
        return new(name, CsvKind.OrderItems, items.Count, $"{items.Count} sold item lines added or updated", true);
    }

    private ImportResult ImportListings(CsvReader csv, string name)
    {
        var listings = new List<Listing>();
        while (csv.Read())
        {
            var l = new Listing
            {
                Title = Get(csv, "TITLE"),
                Price = Money.Parse(Get(csv, "PRICE")),
                Quantity = ParseInt(Get(csv, "QUANTITY")),
                Sku = Get(csv, "SKU"),
                Materials = Get(csv, "MATERIALS"),
            };
            for (int n = 1; n <= 3; n++)
            {
                var vName = Get(csv, $"VARIATION {n} NAME");
                if (vName.Length == 0) continue;
                var values = Get(csv, $"VARIATION {n} VALUES").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                l.Variations.Add(new ListingVariation(vName, values));
            }
            if (l.Title.Length > 0) listings.Add(l);
        }
        // Two rows with the same title can't be told apart in this file (it has no listing number): keep the first.
        var distinct = listings.GroupBy(l => l.Key).Select(g => g.First()).ToList();
        var fromApi = db.ReplaceFileListings(distinct);
        return new(name, CsvKind.Listings, listings.Count,
            $"{distinct.Count - fromApi} listings (the earlier listings file replaced)" +
            (fromApi > 0 ? $"; {fromApi} more are already known from the Etsy API and were left as they are" : "") +
            (distinct.Count < listings.Count ? $"; {listings.Count - distinct.Count} row(s) repeated a title and were skipped" : ""), true);
    }

    [GeneratedRegex(@"etsy_statement_(\d{4})_(\d{1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex StatementFileName();
}
