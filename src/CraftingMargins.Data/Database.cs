// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.Text.Json;
using CraftingMargins.Core;
using Microsoft.Data.Sqlite;

namespace CraftingMargins.Data;

/// <summary>
/// The local SQLite database.  Money is stored as exact decimal text, dates as yyyy-MM-dd.
/// Rollback-journal mode keeps it a single file, which is friendlier to OneDrive than WAL.
/// </summary>
public sealed class Database
{
    /// <summary>
    /// The schema version: the layout of this database.  Shown on the About page next to the
    /// program version, and written into every database and backup file.
    ///
    /// It is 1: the layout the first public release ships with.  Test builds before that used
    /// 2, 3 and 4 while the layout was still changing; databases from those builds are not
    /// read (they are refused as "newer"), and their data is imported again from Etsy.
    ///
    /// It stays 1 until the repository is public and the first version is released; until then
    /// the layout may change freely without raising the number or writing an upgrade.
    /// AFTER that, raise it once in the next release that changes a table or column (however
    /// many changes that release has; a release with none keeps the number), and every
    /// change must come with a step in <see cref="CreateSchema"/> that brings each earlier
    /// published schema up to the new one without losing data, and a test that upgrades a
    /// database from the old schema.  People's cost rules, parts and expenses are typed in by
    /// hand and can't be re-imported.
    /// </summary>
    public const int SchemaVersion = 1;
    private readonly string _connectionString;
    public string FilePath { get; }
    public string ShopId { get; } = Shop.Default;

    public Database(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = filePath, Pooling = false }.ToString();
        Upgrade();
    }

    /// <summary>
    /// Creates missing tables (and, once there are published schemas to upgrade from, brings an
    /// older database up to this version's layout).  Run when the database is opened and again
    /// after a backup is restored.  A database with a HIGHER schema number is refused, not guessed at.
    /// </summary>
    public void Upgrade()
    {
        var found = StoredSchemaVersion(FilePath);
        if (found > SchemaVersion)
            throw new DatabaseTooNewException(found, SchemaVersion);
        CreateSchema();
    }

    /// <summary>The schema version a database file says it is (0 = new, or no such file).</summary>
    public static int StoredSchemaVersion(string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length == 0) return 0;
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A number SQLite keeps in the file's header and raises on every change to the data.
    /// Comparing it with the value at the last backup tells whether anything changed since,
    /// without the database having to be told about every write.  Null if it can't be read.
    /// </summary>
    public uint? ChangeCounter()
    {
        try
        {
            using var f = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var b = new byte[4];
            f.Seek(24, SeekOrigin.Begin);
            if (f.Read(b, 0, 4) != 4) return null;
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return c;
    }

    private void CreateSchema()
    {
        using var c = Open();
        Exec(c, null, """
            CREATE TABLE IF NOT EXISTS StatementLines(
              Id INTEGER PRIMARY KEY AUTOINCREMENT, ShopId TEXT NOT NULL, Month TEXT NOT NULL, Date TEXT NOT NULL,
              Type TEXT NOT NULL, Title TEXT NOT NULL, Info TEXT NOT NULL, Currency TEXT NOT NULL,
              Amount TEXT NOT NULL, FeesAndTaxes TEXT NOT NULL, Net TEXT NOT NULL,
              OrderId TEXT, ListingId TEXT, LabelId TEXT, AdjustmentId TEXT, SourceFile TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_Statement_Month ON StatementLines(ShopId, Month);
            CREATE TABLE IF NOT EXISTS Orders(
              ShopId TEXT NOT NULL, OrderId TEXT NOT NULL, Source TEXT NOT NULL, SaleDate TEXT NOT NULL, DateShipped TEXT,
              NumberOfItems INTEGER NOT NULL, Currency TEXT NOT NULL, OrderValue TEXT NOT NULL, DiscountAmount TEXT NOT NULL,
              ShippingDiscount TEXT NOT NULL, Shipping TEXT NOT NULL, SalesTax TEXT NOT NULL, OrderTotal TEXT NOT NULL,
              Status TEXT NOT NULL, CardProcessingFees TEXT NOT NULL, OrderNet TEXT NOT NULL,
              AdjustedOrderTotal TEXT, AdjustedCardProcessingFees TEXT, AdjustedNetOrderAmount TEXT,
              OrderType TEXT NOT NULL, ExtraShippingCost TEXT NOT NULL DEFAULT '0',
              PRIMARY KEY(ShopId, OrderId));
            CREATE TABLE IF NOT EXISTS OrderItems(
              ShopId TEXT NOT NULL, TransactionId TEXT NOT NULL, OrderId TEXT NOT NULL, ListingId TEXT NOT NULL,
              SaleDate TEXT NOT NULL, DateShipped TEXT, ItemName TEXT NOT NULL, Quantity INTEGER NOT NULL,
              Price TEXT NOT NULL, DiscountAmount TEXT NOT NULL, ShippingDiscount TEXT NOT NULL, OrderShipping TEXT NOT NULL,
              ItemTotal TEXT NOT NULL, Currency TEXT NOT NULL, Variations TEXT NOT NULL, Sku TEXT NOT NULL, CouponCode TEXT NOT NULL,
              PRIMARY KEY(ShopId, TransactionId));
            CREATE TABLE IF NOT EXISTS Listings(
              ShopId TEXT NOT NULL, ListingKey TEXT NOT NULL, Title TEXT NOT NULL, Price TEXT NOT NULL, Quantity INTEGER NOT NULL,
              Sku TEXT NOT NULL, Materials TEXT NOT NULL, VariationsJson TEXT NOT NULL,
              ListingId TEXT, State TEXT NOT NULL DEFAULT '', NumFavorers INTEGER, OfferingsJson TEXT NOT NULL DEFAULT '[]',
              PRIMARY KEY(ShopId, ListingKey));
            CREATE TABLE IF NOT EXISTS Imports(
              Id INTEGER PRIMARY KEY AUTOINCREMENT, ShopId TEXT NOT NULL, ImportedAt TEXT NOT NULL,
              FileName TEXT NOT NULL, Kind TEXT NOT NULL, Rows INTEGER NOT NULL, Summary TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Materials(
              Id INTEGER PRIMARY KEY, ShopId TEXT NOT NULL, Name TEXT NOT NULL, Unit TEXT NOT NULL, PricesJson TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Parts(
              Id INTEGER PRIMARY KEY, ShopId TEXT NOT NULL, Name TEXT NOT NULL,
              VersionsJson TEXT NOT NULL, Notes TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS MappingRules(
              Id INTEGER PRIMARY KEY, ShopId TEXT NOT NULL, ListingId TEXT NOT NULL, Name TEXT NOT NULL,
              ConditionsJson TEXT NOT NULL, PartsJson TEXT NOT NULL, FixedCost TEXT NOT NULL, SortOrder INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS VariationAliases(
              Id INTEGER PRIMARY KEY, ShopId TEXT NOT NULL, ListingId TEXT NOT NULL, VariationName TEXT NOT NULL,
              OldValue TEXT NOT NULL, NewValue TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DatedSettings(
              ShopId TEXT NOT NULL, Kind TEXT NOT NULL, Json TEXT NOT NULL, PRIMARY KEY(ShopId, Kind));
            CREATE TABLE IF NOT EXISTS LedgerEntries(
              ShopId TEXT NOT NULL, EntryId INTEGER NOT NULL, Month TEXT NOT NULL, Date TEXT NOT NULL, Amount TEXT NOT NULL,
              Currency TEXT NOT NULL, Description TEXT NOT NULL, LedgerType TEXT NOT NULL, ReferenceType TEXT NOT NULL,
              ReferenceId TEXT NOT NULL, ParentEntryId INTEGER, SequenceNumber INTEGER NOT NULL, PRIMARY KEY(ShopId, EntryId));
            CREATE INDEX IF NOT EXISTS IX_Ledger_Month ON LedgerEntries(ShopId, Month);
            CREATE TABLE IF NOT EXISTS Expenses(
              Id INTEGER PRIMARY KEY, ShopId TEXT NOT NULL, Date TEXT NOT NULL, Amount TEXT NOT NULL,
              Category TEXT NOT NULL, Vendor TEXT NOT NULL, Note TEXT NOT NULL);
            """);
        // Schema 1 is the layout above.  There are no upgrade steps yet because nothing has been
        // published.  The first change after publishing goes here: check the stored schema number
        // and bring that layout up to the new one (see the note on SchemaVersion).
        Exec(c, null, $"PRAGMA user_version = {SchemaVersion};");
    }

    private static void InsertListing(SqliteConnection c, SqliteTransaction tx, Listing l) =>
        Exec(c, tx, """
            INSERT OR REPLACE INTO Listings(ShopId,ListingKey,Title,Price,Quantity,Sku,Materials,VariationsJson,ListingId,State,NumFavorers,OfferingsJson)
            VALUES($s,$k,$t,$p,$q,$sku,$m,$v,$id,$st,$fav,$off)
            """,
            ("$s", l.ShopId), ("$k", l.Key), ("$t", l.Title), ("$p", M(l.Price)), ("$q", l.Quantity), ("$sku", l.Sku), ("$m", l.Materials), ("$v", J(l.Variations)),
            ("$id", string.IsNullOrEmpty(l.ListingId) ? null : l.ListingId), ("$st", l.State), ("$fav", l.NumFavorers), ("$off", J(l.Offerings)));

    private static Listing ReadListing(SqliteDataReader r) => new()
    {
        ShopId = S(r, "ShopId"), Title = S(r, "Title"), Price = Dec(r, "Price"), Quantity = Int(r, "Quantity"),
        Sku = S(r, "Sku"), Materials = S(r, "Materials"), Variations = FromJ<List<ListingVariation>>(S(r, "VariationsJson")),
        ListingId = SN(r, "ListingId"), State = S(r, "State"),
        NumFavorers = r.IsDBNull(r.GetOrdinal("NumFavorers")) ? null : Int(r, "NumFavorers"),
        Offerings = FromJ<List<ListingOffering>>(S(r, "OfferingsJson")),
    };

    // ---------- small helpers ----------

    public static void Exec(SqliteConnection c, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static string M(decimal d) => d.ToString(CultureInfo.InvariantCulture);
    public static string? M(decimal? d) => d?.ToString(CultureInfo.InvariantCulture);
    public static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string? D(DateOnly? d) => d is null ? null : D(d.Value);

    private static string S(SqliteDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? "" : r.GetString(r.GetOrdinal(col));
    private static string? SN(SqliteDataReader r, string col) => r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));
    private static decimal Dec(SqliteDataReader r, string col) => decimal.Parse(S(r, col), CultureInfo.InvariantCulture);
    private static decimal? DecN(SqliteDataReader r, string col) => SN(r, col) is { } s ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
    private static DateOnly Date(SqliteDataReader r, string col) => DateOnly.ParseExact(S(r, col), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly? DateN(SqliteDataReader r, string col) => SN(r, col) is { } s ? DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    private static int Int(SqliteDataReader r, string col) => r.GetInt32(r.GetOrdinal(col));
    private static long Long(SqliteDataReader r, string col) => r.GetInt64(r.GetOrdinal(col));

    private static readonly JsonSerializerOptions JsonOpts = new();
    private static string J<T>(T v) => JsonSerializer.Serialize(v, JsonOpts);
    private static T FromJ<T>(string s) where T : new() => JsonSerializer.Deserialize<T>(s, JsonOpts) ?? new T();

    // ---------- imported data ----------

    public void ReplaceStatementMonths(IReadOnlyCollection<string> months, IEnumerable<StatementLine> lines)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var m in months)
            Exec(c, tx, "DELETE FROM StatementLines WHERE ShopId=$s AND Month=$m", ("$s", ShopId), ("$m", m));
        foreach (var l in lines)
            Exec(c, tx, """
                INSERT INTO StatementLines(ShopId,Month,Date,Type,Title,Info,Currency,Amount,FeesAndTaxes,Net,OrderId,ListingId,LabelId,AdjustmentId,SourceFile)
                VALUES($s,$mo,$d,$t,$ti,$i,$c,$a,$f,$n,$o,$l,$lb,$ad,$src)
                """,
                ("$s", ShopId), ("$mo", l.Month), ("$d", D(l.Date)), ("$t", l.Type), ("$ti", l.Title), ("$i", l.Info),
                ("$c", l.Currency), ("$a", M(l.Amount)), ("$f", M(l.FeesAndTaxes)), ("$n", M(l.Net)),
                ("$o", l.OrderId), ("$l", l.ListingId), ("$lb", l.LabelId), ("$ad", l.AdjustmentId), ("$src", l.SourceFile));
        tx.Commit();
    }

    public List<StatementLine> LoadStatement()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM StatementLines WHERE ShopId=$s ORDER BY Date, Id", r => new StatementLine
        {
            Id = Long(r, "Id"), ShopId = S(r, "ShopId"), Date = Date(r, "Date"), Type = S(r, "Type"), Title = S(r, "Title"),
            Info = S(r, "Info"), Currency = S(r, "Currency"), Amount = Dec(r, "Amount"), FeesAndTaxes = Dec(r, "FeesAndTaxes"),
            Net = Dec(r, "Net"), OrderId = SN(r, "OrderId"), ListingId = SN(r, "ListingId"), LabelId = SN(r, "LabelId"),
            AdjustmentId = SN(r, "AdjustmentId"), SourceFile = S(r, "SourceFile"),
        }, ("$s", ShopId));
    }

    /// <summary>Inserts or replaces orders by Order ID.  A manually entered extra shipping cost is kept.</summary>
    public void UpsertOrders(IEnumerable<Order> orders)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var o in orders)
            Exec(c, tx, """
                INSERT INTO Orders(ShopId,OrderId,Source,SaleDate,DateShipped,NumberOfItems,Currency,OrderValue,DiscountAmount,
                  ShippingDiscount,Shipping,SalesTax,OrderTotal,Status,CardProcessingFees,OrderNet,AdjustedOrderTotal,
                  AdjustedCardProcessingFees,AdjustedNetOrderAmount,OrderType)
                VALUES($s,$id,$src,$sd,$ds,$n,$cur,$ov,$da,$sdc,$sh,$tax,$tot,$st,$cpf,$net,$aot,$acpf,$anet,$ot)
                ON CONFLICT(ShopId,OrderId) DO UPDATE SET Source=excluded.Source, SaleDate=excluded.SaleDate,
                  DateShipped=excluded.DateShipped, NumberOfItems=excluded.NumberOfItems, Currency=excluded.Currency,
                  OrderValue=excluded.OrderValue, DiscountAmount=excluded.DiscountAmount, ShippingDiscount=excluded.ShippingDiscount,
                  Shipping=excluded.Shipping, SalesTax=excluded.SalesTax, OrderTotal=excluded.OrderTotal, Status=excluded.Status,
                  CardProcessingFees=excluded.CardProcessingFees, OrderNet=excluded.OrderNet,
                  AdjustedOrderTotal=excluded.AdjustedOrderTotal, AdjustedCardProcessingFees=excluded.AdjustedCardProcessingFees,
                  AdjustedNetOrderAmount=excluded.AdjustedNetOrderAmount, OrderType=excluded.OrderType
                """,
                ("$s", ShopId), ("$id", o.OrderId), ("$src", o.Source), ("$sd", D(o.SaleDate)), ("$ds", D(o.DateShipped)),
                ("$n", o.NumberOfItems), ("$cur", o.Currency), ("$ov", M(o.OrderValue)), ("$da", M(o.DiscountAmount)),
                ("$sdc", M(o.ShippingDiscount)), ("$sh", M(o.Shipping)), ("$tax", M(o.SalesTax)), ("$tot", M(o.OrderTotal)),
                ("$st", o.Status), ("$cpf", M(o.CardProcessingFees)), ("$net", M(o.OrderNet)), ("$aot", M(o.AdjustedOrderTotal)),
                ("$acpf", M(o.AdjustedCardProcessingFees)), ("$anet", M(o.AdjustedNetOrderAmount)), ("$ot", o.OrderType));
        tx.Commit();
    }

    /// <summary>Removes orders and their sold lines (an order cancelled after it was synced).</summary>
    public void DeleteOrders(IEnumerable<string> orderIds)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var id in orderIds)
        {
            Exec(c, tx, "DELETE FROM OrderItems WHERE ShopId=$s AND OrderId=$o", ("$s", ShopId), ("$o", id));
            Exec(c, tx, "DELETE FROM Orders WHERE ShopId=$s AND OrderId=$o", ("$s", ShopId), ("$o", id));
        }
        tx.Commit();
    }

    public void SetExtraShippingCost(string orderId, decimal amount)
    {
        using var c = Open();
        Exec(c, null, "UPDATE Orders SET ExtraShippingCost=$a WHERE ShopId=$s AND OrderId=$o", ("$a", M(amount)), ("$s", ShopId), ("$o", orderId));
    }

    public List<Order> LoadOrders()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM Orders WHERE ShopId=$s", r => new Order
        {
            ShopId = S(r, "ShopId"), OrderId = S(r, "OrderId"), Source = S(r, "Source"), SaleDate = Date(r, "SaleDate"),
            DateShipped = DateN(r, "DateShipped"), NumberOfItems = Int(r, "NumberOfItems"), Currency = S(r, "Currency"),
            OrderValue = Dec(r, "OrderValue"), DiscountAmount = Dec(r, "DiscountAmount"), ShippingDiscount = Dec(r, "ShippingDiscount"),
            Shipping = Dec(r, "Shipping"), SalesTax = Dec(r, "SalesTax"), OrderTotal = Dec(r, "OrderTotal"), Status = S(r, "Status"),
            CardProcessingFees = Dec(r, "CardProcessingFees"), OrderNet = Dec(r, "OrderNet"),
            AdjustedOrderTotal = DecN(r, "AdjustedOrderTotal"), AdjustedCardProcessingFees = DecN(r, "AdjustedCardProcessingFees"),
            AdjustedNetOrderAmount = DecN(r, "AdjustedNetOrderAmount"), OrderType = S(r, "OrderType"),
            ExtraShippingCost = Dec(r, "ExtraShippingCost"),
        }, ("$s", ShopId));
    }

    public void UpsertItems(IEnumerable<OrderItem> items)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var i in items)
            Exec(c, tx, """
                INSERT OR REPLACE INTO OrderItems(ShopId,TransactionId,OrderId,ListingId,SaleDate,DateShipped,ItemName,Quantity,Price,
                  DiscountAmount,ShippingDiscount,OrderShipping,ItemTotal,Currency,Variations,Sku,CouponCode)
                VALUES($s,$t,$o,$l,$sd,$ds,$n,$q,$p,$da,$sdc,$os,$it,$c,$v,$sku,$cc)
                """,
                ("$s", ShopId), ("$t", i.TransactionId), ("$o", i.OrderId), ("$l", i.ListingId), ("$sd", D(i.SaleDate)),
                ("$ds", D(i.DateShipped)), ("$n", i.ItemName), ("$q", i.Quantity), ("$p", M(i.Price)), ("$da", M(i.DiscountAmount)),
                ("$sdc", M(i.ShippingDiscount)), ("$os", M(i.OrderShipping)), ("$it", M(i.ItemTotal)), ("$c", i.Currency),
                ("$v", i.Variations), ("$sku", i.Sku), ("$cc", i.CouponCode));
        tx.Commit();
    }

    /// <summary>Removes sold lines by their transaction number (lines the Etsy API says are no longer on their order).</summary>
    public void DeleteItems(IReadOnlyCollection<string> transactionIds)
    {
        if (transactionIds.Count == 0) return;
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var id in transactionIds)
            Exec(c, tx, "DELETE FROM OrderItems WHERE ShopId=$s AND TransactionId=$t", ("$s", ShopId), ("$t", id));
        tx.Commit();
    }

    /// <summary>Removes a free-text variation (personalization) from every stored sale line.</summary>
    public int StripVariationName(string name)
    {
        var items = LoadItems();
        var changed = items.Where(i => Core.Variations.Parse(i.Variations).Any(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var i in changed) i.Variations = Core.Variations.Strip(i.Variations, [name]);
        UpsertItems(changed);
        using (var c = Open()) Exec(c, null, "VACUUM");  // so the removed text doesn't linger in free pages
        return changed.Count;
    }

    public List<OrderItem> LoadItems()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM OrderItems WHERE ShopId=$s", r => new OrderItem
        {
            ShopId = S(r, "ShopId"), TransactionId = S(r, "TransactionId"), OrderId = S(r, "OrderId"), ListingId = S(r, "ListingId"),
            SaleDate = Date(r, "SaleDate"), DateShipped = DateN(r, "DateShipped"), ItemName = S(r, "ItemName"),
            Quantity = Int(r, "Quantity"), Price = Dec(r, "Price"), DiscountAmount = Dec(r, "DiscountAmount"),
            ShippingDiscount = Dec(r, "ShippingDiscount"), OrderShipping = Dec(r, "OrderShipping"), ItemTotal = Dec(r, "ItemTotal"),
            Currency = S(r, "Currency"), Variations = S(r, "Variations"), Sku = S(r, "Sku"), CouponCode = S(r, "CouponCode"),
        }, ("$s", ShopId));
    }

    /// <summary>Replaces every stored listing (the demo shop and tests; imports use the two methods below).</summary>
    public void ReplaceListings(IEnumerable<Listing> listings)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM Listings WHERE ShopId=$s", ("$s", ShopId));
        foreach (var l in listings) { l.ShopId = ShopId; InsertListing(c, tx, l); }
        tx.Commit();
    }

    /// <summary>
    /// A sync from the Etsy API: the API's listings replace the ones it gave before.  A listing
    /// known only from the listings file is dropped when the API now has one with the same
    /// title (the API's has the listing number and a price for every option), and kept otherwise.
    /// </summary>
    public void ReplaceApiListings(IReadOnlyCollection<Listing> listings)
    {
        var titles = listings.Select(l => ListingKey.ForTitle(l.Title)).ToHashSet();
        using var c = Open();
        var superseded = Query(c, "SELECT ListingKey FROM Listings WHERE ShopId=$s AND ListingId IS NULL", r => r.GetString(0), ("$s", ShopId)).Where(titles.Contains).ToList();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM Listings WHERE ShopId=$s AND ListingId IS NOT NULL", ("$s", ShopId));
        foreach (var key in superseded)
            Exec(c, tx, "DELETE FROM Listings WHERE ShopId=$s AND ListingKey=$k", ("$s", ShopId), ("$k", key));
        foreach (var l in listings) { l.ShopId = ShopId; InsertListing(c, tx, l); }
        tx.Commit();
    }

    /// <summary>
    /// The "currently for sale" listings file: it replaces what an earlier listings file gave.
    /// Listings the Etsy API already supplied are left as they are, because the file has no
    /// listing number and only one price per listing.  Returns how many rows of the file were
    /// left out for that reason.
    /// </summary>
    public int ReplaceFileListings(IReadOnlyCollection<Listing> listings)
    {
        using var c = Open();
        var apiTitles = Query(c, "SELECT Title FROM Listings WHERE ShopId=$s AND ListingId IS NOT NULL", r => r.GetString(0), ("$s", ShopId))
            .Select(ListingKey.ForTitle).ToHashSet();
        using var tx = c.BeginTransaction();
        Exec(c, tx, "DELETE FROM Listings WHERE ShopId=$s AND ListingId IS NULL", ("$s", ShopId));
        var skipped = 0;
        foreach (var l in listings)
        {
            if (apiTitles.Contains(l.Key)) { skipped++; continue; }
            l.ShopId = ShopId;
            InsertListing(c, tx, l);
        }
        tx.Commit();
        return skipped;
    }

    public List<Listing> LoadListings()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM Listings WHERE ShopId=$s ORDER BY Title", ReadListing, ("$s", ShopId));
    }

    // ---------- Etsy API ledger ----------

    public void UpsertLedger(IEnumerable<LedgerEntry> entries)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var e in entries)
            Exec(c, tx, """
                INSERT OR REPLACE INTO LedgerEntries(ShopId,EntryId,Month,Date,Amount,Currency,Description,LedgerType,ReferenceType,ReferenceId,ParentEntryId,SequenceNumber)
                VALUES($s,$id,$m,$d,$a,$c,$desc,$lt,$rt,$rid,$p,$seq)
                """,
                ("$s", ShopId), ("$id", e.EntryId), ("$m", e.Month), ("$d", D(e.Date)), ("$a", M(e.Amount)), ("$c", e.Currency),
                ("$desc", e.Description), ("$lt", e.LedgerType), ("$rt", e.ReferenceType), ("$rid", e.ReferenceId),
                ("$p", e.ParentEntryId), ("$seq", e.SequenceNumber));
        tx.Commit();
    }

    public List<LedgerEntry> LoadLedger(string? month = null)
    {
        using var c = Open();
        return Query(c, "SELECT * FROM LedgerEntries WHERE ShopId=$s AND ($m IS NULL OR Month=$m) ORDER BY Date, SequenceNumber, EntryId", r => new LedgerEntry
        {
            ShopId = S(r, "ShopId"), EntryId = Long(r, "EntryId"), Date = Date(r, "Date"), Amount = Dec(r, "Amount"),
            Currency = S(r, "Currency"), Description = S(r, "Description"), LedgerType = S(r, "LedgerType"),
            ReferenceType = S(r, "ReferenceType"), ReferenceId = S(r, "ReferenceId"),
            ParentEntryId = r.IsDBNull(r.GetOrdinal("ParentEntryId")) ? null : Long(r, "ParentEntryId"),
            SequenceNumber = Long(r, "SequenceNumber"),
        }, ("$s", ShopId), ("$m", month));
    }

    /// <summary>Months that have lines from a statement CSV (these are never overwritten by the API).</summary>
    public HashSet<string> CsvStatementMonths()
    {
        using var c = Open();
        return Query(c, "SELECT DISTINCT Month FROM StatementLines WHERE ShopId=$s AND SourceFile<>$api", r => r.GetString(0),
            ("$s", ShopId), ("$api", LedgerLines.ApiSource)).ToHashSet();
    }

    /// <summary>
    /// Rebuilds the API months of the statement from the stored ledger.  A month that has a
    /// statement CSV is left alone: the CSV always wins.  Returns the months written.
    /// </summary>
    public List<string> RebuildApiStatementMonths()
    {
        var csvMonths = CsvStatementMonths();
        var lines = LoadLedger().Where(e => !csvMonths.Contains(e.Month)).Select(LedgerLines.ToStatementLine).ToList();
        RemoveApiStatementLines();
        var months = lines.Select(l => l.Month).Distinct().OrderBy(m => m).ToList();
        if (lines.Count > 0) ReplaceStatementMonths(months, lines);
        return months;
    }

    /// <summary>Removes every statement line that came from the API (when the user turns that off).</summary>
    public void RemoveApiStatementLines()
    {
        using var c = Open();
        Exec(c, null, "DELETE FROM StatementLines WHERE ShopId=$s AND SourceFile=$api", ("$s", ShopId), ("$api", LedgerLines.ApiSource));
    }

    public void LogImport(string fileName, string kind, int rows, string summary)
    {
        using var c = Open();
        Exec(c, null, "INSERT INTO Imports(ShopId,ImportedAt,FileName,Kind,Rows,Summary) VALUES($s,$at,$f,$k,$r,$sum)",
            ("$s", ShopId), ("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("$f", fileName), ("$k", kind), ("$r", rows), ("$sum", summary));
    }

    // ---------- user-maintained cost model ----------
    // Each list is saved whole (it is small).  New items (Id 0) get the next free Id.

    public List<Material> LoadMaterials()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM Materials WHERE ShopId=$s ORDER BY Name", r => new Material
        { Id = Long(r, "Id"), Name = S(r, "Name"), Unit = S(r, "Unit"), Prices = FromJ<List<MaterialPrice>>(S(r, "PricesJson")) }, ("$s", ShopId));
    }

    public void SaveMaterials(List<Material> items)
    {
        AssignIds(items, m => m.Id, (m, id) => m.Id = id);
        SaveAll("Materials", items, (c, tx, m) => Exec(c, tx, "INSERT INTO Materials VALUES($id,$s,$n,$u,$p)",
            ("$id", m.Id), ("$s", ShopId), ("$n", m.Name), ("$u", m.Unit), ("$p", J(m.Prices))));
    }

    public List<Part> LoadParts()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM Parts WHERE ShopId=$s ORDER BY Name", r => new Part
        {
            Id = Long(r, "Id"), Name = S(r, "Name"),
            Versions = FromJ<List<PartVersion>>(S(r, "VersionsJson")), Notes = S(r, "Notes"),
        }, ("$s", ShopId));
    }

    public void SaveParts(List<Part> items)
    {
        AssignIds(items, p => p.Id, (p, id) => p.Id = id);
        SaveAll("Parts", items, (c, tx, p) => Exec(c, tx, "INSERT INTO Parts VALUES($id,$s,$n,$v,$no)",
            ("$id", p.Id), ("$s", ShopId), ("$n", p.Name), ("$v", J(p.Versions)), ("$no", p.Notes)));
    }

    public List<MappingRule> LoadRules()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM MappingRules WHERE ShopId=$s ORDER BY ListingId, SortOrder, Id", r => new MappingRule
        {
            Id = Long(r, "Id"), ListingId = S(r, "ListingId"), Name = S(r, "Name"),
            Conditions = new(FromJ<Dictionary<string, string>>(S(r, "ConditionsJson")), StringComparer.OrdinalIgnoreCase),
            Parts = FromJ<List<RulePart>>(S(r, "PartsJson")), FixedCost = Dec(r, "FixedCost"), SortOrder = Int(r, "SortOrder"),
        }, ("$s", ShopId));
    }

    public void SaveRules(List<MappingRule> items)
    {
        AssignIds(items, r => r.Id, (r, id) => r.Id = id);
        SaveAll("MappingRules", items, (c, tx, r) => Exec(c, tx, "INSERT INTO MappingRules VALUES($id,$s,$l,$n,$c,$p,$f,$o)",
            ("$id", r.Id), ("$s", ShopId), ("$l", r.ListingId), ("$n", r.Name), ("$c", J(r.Conditions)), ("$p", J(r.Parts)),
            ("$f", M(r.FixedCost)), ("$o", r.SortOrder)));
    }

    public List<VariationAlias> LoadAliases()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM VariationAliases WHERE ShopId=$s ORDER BY ListingId, VariationName", r => new VariationAlias
        {
            Id = Long(r, "Id"), ListingId = S(r, "ListingId"), VariationName = S(r, "VariationName"),
            OldValue = S(r, "OldValue"), NewValue = S(r, "NewValue"),
        }, ("$s", ShopId));
    }

    public void SaveAliases(List<VariationAlias> items)
    {
        AssignIds(items, a => a.Id, (a, id) => a.Id = id);
        SaveAll("VariationAliases", items, (c, tx, a) => Exec(c, tx, "INSERT INTO VariationAliases VALUES($id,$s,$l,$v,$o,$n)",
            ("$id", a.Id), ("$s", ShopId), ("$l", a.ListingId), ("$v", a.VariationName), ("$o", a.OldValue), ("$n", a.NewValue)));
    }

    // ---------- expense log (Phase 3) ----------

    public List<Expense> LoadExpenses()
    {
        using var c = Open();
        return Query(c, "SELECT * FROM Expenses WHERE ShopId=$s ORDER BY Date, Id", r => new Expense
        {
            Id = Long(r, "Id"), ShopId = S(r, "ShopId"), Date = Date(r, "Date"), Amount = Dec(r, "Amount"),
            Category = S(r, "Category"), Vendor = S(r, "Vendor"), Note = S(r, "Note"),
        }, ("$s", ShopId));
    }

    public void SaveExpenses(List<Expense> items)
    {
        AssignIds(items, e => e.Id, (e, id) => e.Id = id);
        SaveAll("Expenses", items, (c, tx, e) => Exec(c, tx, "INSERT INTO Expenses VALUES($id,$s,$d,$a,$c,$v,$n)",
            ("$id", e.Id), ("$s", ShopId), ("$d", D(e.Date)), ("$a", M(e.Amount)), ("$c", e.Category), ("$v", e.Vendor), ("$n", e.Note)));
    }

    public List<ExpenseCategory> LoadExpenseCategories() =>
        LoadDated<List<ExpenseCategory>>("expenseCategories") is { Count: > 0 } c ? c : ScheduleC.DefaultCategories();
    public void SaveExpenseCategories(List<ExpenseCategory> v) => SaveDated("expenseCategories", v);

    public Dictionary<string, decimal> LoadListingPackaging() => LoadDated<Dictionary<string, decimal>>("listingPackaging") ?? [];
    public void SaveListingPackaging(Dictionary<string, decimal> v) => SaveDated("listingPackaging", v);

    /// <summary>Listings the user marked as costing nothing to make.</summary>
    public HashSet<string> LoadZeroCostListings() => [.. LoadDated<List<string>>("zeroCostListings") ?? []];
    public void SaveZeroCostListings(HashSet<string> v) => SaveDated("zeroCostListings", v.Order().ToList());

    /// <summary>
    /// A listing known only from the listings file is costed under a stand-in made from its
    /// title.  Once it sells (or the API supplies it) its real listing number is known, and
    /// its rules, retired options, packaging and "costs nothing" mark move to that number.
    /// Returns true if anything moved.
    /// </summary>
    public bool AdoptListingNumbers()
    {
        var rules = LoadRules();
        var aliases = LoadAliases();
        var packaging = LoadListingPackaging();
        var free = LoadZeroCostListings();
        var standIns = rules.Select(r => r.ListingId).Concat(aliases.Select(a => a.ListingId)).Concat(packaging.Keys).Concat(free)
            .Where(ListingKey.IsTitle).ToHashSet();
        if (standIns.Count == 0) return false;

        // Newest sale of each title, then API listings: either gives the real number.
        var real = new Dictionary<string, string>();
        foreach (var l in LoadListings().Where(l => !string.IsNullOrEmpty(l.ListingId)))
            real[ListingKey.ForTitle(l.Title)] = l.ListingId!;
        foreach (var g in LoadItems().Where(i => !i.ListingId.StartsWith(OtherItem.IdPrefix, StringComparison.Ordinal)).GroupBy(i => ListingKey.ForTitle(i.ItemName)))
            real[g.Key] = g.OrderByDescending(i => i.SaleDate).First().ListingId;
        var moves = standIns.Where(real.ContainsKey).ToDictionary(k => k, k => real[k]);
        if (MoveListingCosts(moves) == 0) return false;
        Log.Write($"{moves.Count} listing(s) now have an Etsy listing number; their cost rules were moved to it.");
        return true;
    }

    /// <summary>
    /// Moves everything typed in for a listing (cost rules, retired options, packaging, the
    /// "costs nothing" mark) from one listing ID to another.  A move is skipped when the
    /// target already has cost rules of its own, so two sets are never merged by accident.
    /// Returns how many listings were moved.
    /// </summary>
    public int MoveListingCosts(IReadOnlyDictionary<string, string> moves)
    {
        if (moves.Count == 0) return 0;
        var rules = LoadRules();
        var aliases = LoadAliases();
        var packaging = LoadListingPackaging();
        var free = LoadZeroCostListings();
        var taken = rules.Select(r => r.ListingId).ToHashSet();
        var doing = moves.Where(m => m.Key != m.Value && !taken.Contains(m.Value)).ToDictionary(m => m.Key, m => m.Value);
        if (doing.Count == 0) return 0;

        foreach (var r in rules) if (doing.TryGetValue(r.ListingId, out var id)) r.ListingId = id;
        foreach (var a in aliases) if (doing.TryGetValue(a.ListingId, out var id)) a.ListingId = id;
        foreach (var (from, to) in doing)
        {
            if (packaging.Remove(from, out var p)) packaging.TryAdd(to, p);
            if (free.Remove(from)) free.Add(to);
        }
        SaveRules(rules);
        SaveAliases(aliases);
        SaveListingPackaging(packaging);
        SaveZeroCostListings(free);
        return doing.Count;
    }

    public List<FeeRates> LoadFeeRates() => LoadDated<List<FeeRates>>("fees") is { Count: > 0 } f ? f : [new FeeRates()];
    public void SaveFeeRates(List<FeeRates> v) => SaveDated("fees", v);
    public List<CostSettings> LoadCostSettings() => LoadDated<List<CostSettings>>("costs") is { Count: > 0 } f ? f : [new CostSettings()];
    public void SaveCostSettings(List<CostSettings> v) => SaveDated("costs", v);

    private T? LoadDated<T>(string kind) where T : class
    {
        using var c = Open();
        var json = Query(c, "SELECT Json FROM DatedSettings WHERE ShopId=$s AND Kind=$k", r => r.GetString(0), ("$s", ShopId), ("$k", kind)).FirstOrDefault();
        return json is null ? null : JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private void SaveDated<T>(string kind, T value)
    {
        using var c = Open();
        Exec(c, null, "INSERT OR REPLACE INTO DatedSettings VALUES($s,$k,$j)", ("$s", ShopId), ("$k", kind), ("$j", J(value)));
    }

    private static void AssignIds<T>(List<T> items, Func<T, long> get, Action<T, long> set)
    {
        var next = items.Count == 0 ? 1 : items.Max(get) + 1;
        foreach (var i in items.Where(i => get(i) == 0)) set(i, next++);
    }

    private void SaveAll<T>(string table, List<T> items, Action<SqliteConnection, SqliteTransaction, T> insert)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Exec(c, tx, $"DELETE FROM {table} WHERE ShopId=$s", ("$s", ShopId));
        foreach (var i in items) insert(c, tx, i);
        tx.Commit();
    }

    /// <summary>Loads everything the reports need.</summary>
    public ShopData LoadShopData(AppSettings settings, IEnumerable<CategoryRule> userCategoryRules)
    {
        AdoptListingNumbers();
        return LoadShopDataCore(settings, userCategoryRules);
    }

    private ShopData LoadShopDataCore(AppSettings settings, IEnumerable<CategoryRule> userCategoryRules) => new()
    {
        Statement = LoadStatement(),
        Orders = LoadOrders(),
        Items = LoadItems(),
        FeeRates = LoadFeeRates(),
        Categorizer = new Categorizer(userCategoryRules),
        Costs = new CostModel
        {
            Materials = LoadMaterials(),
            Parts = LoadParts(),
            Rules = LoadRules(),
            Aliases = LoadAliases(),
            CostSettings = LoadCostSettings(),
            PackagingByListing = LoadListingPackaging(),
            ZeroCostListings = LoadZeroCostListings(),
            UseWeightsAtSaleDate = settings.UseWeightsAtSaleDate,
        },
    };

    /// <summary>Settings → "Delete all data": empties every table.</summary>
    public void DeleteAllData()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var t in new[] { "StatementLines", "Orders", "OrderItems", "Listings", "LedgerEntries", "Expenses", "Imports", "Materials", "Parts", "MappingRules", "VariationAliases", "DatedSettings" })
            Exec(c, tx, $"DELETE FROM {t}");
        tx.Commit();
        Exec(c, null, "VACUUM");   // so the deleted rows don't linger in the file's free pages
    }
}

/// <summary>The data folder was last used by a newer version of the app than this one.</summary>
public sealed class DatabaseTooNewException(int found, int supported) : Exception(
    $"This data was last used by a newer version of Crafting Margins, or by a test build from before the first release (schema {found}; this version reads up to schema {supported}). " +
    "Use the newer version of the app, or point this version at a different data folder.")
{
    public int Found { get; } = found;
    public int Supported { get; } = supported;
}
