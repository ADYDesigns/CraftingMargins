// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;
using Microsoft.Data.Sqlite;

namespace CraftingMargins.Tests;

/// <summary>Money text, fee guards and dated versions.</summary>
public class MoneyAndFeeFixTests
{
    [Theory]
    [InlineData("$-1.04", -1.04)]
    [InlineData("-$1.04", -1.04)]
    [InlineData("($1.04)", -1.04)]
    [InlineData("US$1.04", 1.04)]
    [InlineData("1.04 USD", 1.04)]
    [InlineData("-US$1,234.50", -1234.50)]
    [InlineData("−$2.00", -2.00)]
    [InlineData("+$3.00", 3.00)]
    public void Money_parse_reads_us_dollar_forms_with_the_sign_anywhere(string text, double expected) =>
        Assert.Equal((decimal)expected, Money.Parse(text));

    [Theory]
    [InlineData("€1,04")]
    [InlineData("£5.00")]
    [InlineData("1.234,50")]
    [InlineData("1,04")]
    [InlineData("12,5")]
    public void Money_parse_refuses_other_currencies_rather_than_misreading_them(string text) =>
        Assert.Throws<FormatException>(() => Money.Parse(text));

    [Fact]
    public void Min_price_does_not_divide_by_zero_when_fee_rates_reach_100_percent()
    {
        var huge = PricingEstimate.MinPriceForTarget(new FeeRates { OffsiteAdsRate = 0.905m }, 5, 0, 1);
        Assert.True(huge > 1_000_000m);
        // And the whole pricing check survives it.
        var q = PriceCalculator.Quote(new FeeRates { OffsiteAdsRate = 0.95m }, 20, 0, null, 3, 5);
        Assert.True(q.MinPrice > 1_000_000m);
    }

    [Fact]
    public void An_ad_sale_whose_fee_was_credited_back_still_counts_as_an_ad_sale()
    {
        var d = new DateOnly(2026, 10, 1);
        StatementLine L(DateOnly day, string type, string title, decimal net) =>
            new() { Date = day, Type = type, Title = title, Amount = net, Net = net, OrderId = "A" };
        var data = new ShopData
        {
            Orders = [new Order { OrderId = "A", SaleDate = d }],
            Statement =
            [
                L(d, "Sale", "Payment for Order #A", 20m),
                L(d, "Marketing", "Fee for sale made through Offsite Ads", -3m),
                L(d.AddDays(3), "Marketing", "Credit for Offsite Ads fee", 3m),
            ],
        };
        var p = OrderReport.Build(data).Single();
        Assert.Equal(0m, p.OffsiteAds);          // it cost nothing in the end...
        Assert.True(p.AdAttributed);             // ...but the ad still brought the sale
        Assert.Equal(1, AdReport.Build(data, [p], Period.All)[^1].AdOrders);

        data.Statement.RemoveRange(1, 2);
        Assert.False(OrderReport.Build(data).Single().AdAttributed);
    }

    [Fact]
    public void Money_is_shown_the_way_the_computer_writes_numbers_and_stored_the_same_everywhere()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1.234,56", Money.Format(1234.56m));
            Assert.Equal("-$1,04", Money.Format(-1.04m));
            Assert.Equal(1234.56m, Money.Parse("$1,234.56"));                    // Etsy's files are read the same on any PC

            // A calendar that doesn't count years from 1 AD must not leak into the month keys the data is stored under.
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("th-TH");
            var line = new StatementLine { Date = new DateOnly(2026, 10, 5) };
            Assert.Equal("2026-10", line.Month);
            var order = new Order { OrderId = "M-1", Source = Order.OtherSource, SaleDate = new DateOnly(2026, 10, 5), OrderValue = 10m };
            Assert.Equal("2026-10", PnLBuilder.Build(new ShopData { Orders = [order] }).Single().Month);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("de-DE", "1.234,50")]
    [InlineData("th-TH", "1,234.50")]
    [InlineData("fr-FR", null)]
    public void Numbers_follow_the_region_but_month_names_stay_english(string region, string? number)
    {
        var c = CraftingMargins.App.Input.NumbersFrom(System.Globalization.CultureInfo.GetCultureInfo(region));
        Assert.Equal("Oct 5, 2026", new DateOnly(2026, 10, 5).ToString("MMM d, yyyy", c));      // English name, ordinary year
        Assert.Equal("October 2026", new DateOnly(2026, 10, 5).ToString("MMMM yyyy", c));
        if (number is not null) Assert.Equal(number, 1234.5m.ToString("N2", c));
        Assert.Equal(System.Globalization.CultureInfo.GetCultureInfo(region).NumberFormat.NumberDecimalSeparator, c.NumberFormat.NumberDecimalSeparator);
    }

    [Fact]
    public void A_typed_value_with_a_comma_and_a_colon_is_not_split_when_the_listings_options_are_known()
    {
        const string raw = "Size:Large,Engraving:Est. 2020, time: 5pm,Color:Red";
        // With nothing known the text alone can't tell: "time" looks like an option (the old behaviour).
        Assert.Equal(4, Variations.Parse(raw).Count);

        var pairs = Variations.Parse(raw, ["Size", "Engraving", "Color"]);
        Assert.Equal(["Size", "Engraving", "Color"], pairs.Select(p => p.Key));
        Assert.Equal("Est. 2020, time: 5pm", pairs[1].Value);

        // What gets stored reads back to exactly the same options, with nothing known.
        var stored = Variations.Format(pairs);
        var again = Variations.Parse(stored);
        Assert.Equal(pairs, again);
        Assert.Equal(stored, Variations.Format(again));
        Assert.Equal("Size: Large, Engraving: Est. 2020, time: 5pm, Color: Red", Variations.Readable(stored));

        // A rule on the real options still matches.
        var rule = new MappingRule { Conditions = new() { ["Size"] = "Large", ["Color"] = "Red" } };
        Assert.True(rule.Matches(again));
    }

    [Fact]
    public void Known_option_names_are_only_trusted_when_they_fit_the_text()
    {
        // The list is for some other listing (not even the first option is in it): leave the text as Etsy wrote it.
        Assert.Equal(2, Variations.Parse("Size:Large,Color:Red", ["Material", "Finish"]).Count);
        // Ordinary text is untouched by the stored form: no escapes appear unless they are needed.
        Assert.Equal("Size:Large (10\" x 7.5\"),Gauges:1/4\", 1/2\"", Variations.Format(Variations.Parse("Size:Large (10&quot; x 7.5&quot;),Gauges:1/4&quot;, 1/2&quot;")));
        Assert.Equal("Time:5:30pm", Variations.Format(Variations.Parse("Time:5:30pm")));
    }

    [Fact]
    public void A_version_dated_in_the_future_is_not_used_yet()
    {
        var today = new DateOnly(2026, 11, 24);
        PartVersion[] versions = [PartVersion.Of(new DateOnly(2026, 1, 1), 1, 10, 60), PartVersion.Of(new DateOnly(2027, 1, 1), 1, 99, 600)];
        Assert.Equal(10, Effective.Latest(versions, v => v.EffectiveFrom, today).Lines[0].Amount);
        Assert.Equal(99, Effective.Latest(versions, v => v.EffectiveFrom, new DateOnly(2027, 1, 1)).Lines[0].Amount);
    }
}

/// <summary>Listings: kept by listing number, unsold ones can be costed, the listings file and the API don't overwrite each other.</summary>
public sealed class ListingFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private readonly FolderAppPaths _paths;
    private readonly Database _db;

    public ListingFixTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = new FolderAppPaths(_dir);
        _db = new Database(((IAppPaths)_paths).DatabaseFile);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Listing Api(string id, string title, params (string Size, decimal Price)[] sizes) => new()
    {
        ListingId = id, Title = title, State = "active",
        Offerings = [.. sizes.Select(s => new ListingOffering(new() { ["Size"] = s.Size }, s.Price, 5, true))],
    };

    [Fact]
    public void Two_api_listings_with_the_same_title_are_both_kept()
    {
        _db.ReplaceApiListings([Api("1", "Wall Sign", ("Small", 10m)), Api("2", "Wall Sign", ("Small", 12m))]);
        Assert.Equal(["1", "2"], _db.LoadListings().Select(l => l.ListingId).Order());
    }

    [Fact]
    public void A_listings_file_does_not_wipe_what_the_api_supplied()
    {
        _db.ReplaceApiListings([Api("1", "Wall Sign", ("Small", 10m), ("Large", 20m))]);
        var skipped = _db.ReplaceFileListings([new Listing { Title = "Wall Sign", Price = 10m }, new Listing { Title = "Desk Stand", Price = 8m }]);
        Assert.Equal(1, skipped);
        var all = _db.LoadListings();
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Single(l => l.ListingId == "1").Offerings.Count);          // per-option prices still there
        Assert.True(ListingKey.IsTitle(all.Single(l => l.Title == "Desk Stand").Key));

        // The next sync brings Desk Stand with its number: the file's copy gives way to it.
        _db.ReplaceApiListings([Api("1", "Wall Sign", ("Small", 10m)), Api("3", "Desk Stand", ("One", 8m))]);
        Assert.Equal(["1", "3"], _db.LoadListings().Select(l => l.Key).Order());
    }

    [Fact]
    public void The_release_schema_is_1_and_a_database_from_a_test_build_is_refused_untouched()
    {
        Assert.Equal(1, Database.SchemaVersion);
        Assert.Equal(1, Database.StoredSchemaVersion(_db.FilePath));       // a new database is stamped with it
        // Reopening a database of the current schema is routine and changes nothing.
        _db.SaveMaterials([new Material { Name = "PLA", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
        SqliteConnection.ClearAllPools();
        Assert.Single(new Database(_db.FilePath).LoadMaterials());

        // Test builds before the first release stamped 2, 3 and 4.  They are refused, and the file is left exactly as it was.
        using (var c = _db.Open()) Database.Exec(c, null, "PRAGMA user_version = 3;");
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(_db.FilePath);
        var ex = Assert.Throws<DatabaseTooNewException>(() => new Database(_db.FilePath));
        Assert.Contains("test build", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(_db.FilePath));
    }

    [Fact]
    public void A_database_from_a_newer_version_is_refused_not_guessed_at()
    {
        using (var c = _db.Open()) Database.Exec(c, null, $"PRAGMA user_version = {Database.SchemaVersion + 1};");
        SqliteConnection.ClearAllPools();
        var ex = Assert.Throws<DatabaseTooNewException>(() => new Database(_db.FilePath));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void An_unsold_listing_from_the_listings_file_gets_pricing_rows_and_can_be_costed()
    {
        var listing = new Listing { Title = "Brand New Shelf", Price = 30m, Variations = [new("Size", ["Small", "Large"]), new("Color", ["Oak", "Walnut"])] };
        var id = listing.Key;
        var data = new ShopData { Costs = new CostModel { Rules = [new MappingRule { ListingId = id, FixedCost = 4m }] } };
        var rows = PricingCheck.Build(data, [listing], [], new PricingOptions { Floor = 5m, Today = new DateOnly(2026, 11, 24) });
        Assert.Equal(4, rows.Count);                                    // every combination
        Assert.All(rows, r => Assert.Equal(id, r.ListingId));
        Assert.All(rows, r => Assert.Equal(4m, r.Cost));
        Assert.Contains(rows, r => r.Options == "Size:Small,Color:Walnut");

        // Without a rule it shows as needing one, instead of not showing at all.
        var bare = PricingCheck.Build(new ShopData(), [listing], [], new PricingOptions());
        Assert.Equal(4, bare.Count);
        Assert.All(bare, r => Assert.Equal("No rules for this listing", r.Problem));
    }

    [Fact]
    public void Rules_made_before_the_first_sale_move_to_the_listing_number_once_it_sells()
    {
        var standIn = ListingKey.ForTitle("Brand New Shelf");
        _db.SaveRules([new MappingRule { ListingId = standIn, Name = "Shelf", FixedCost = 4m }]);
        _db.SaveListingPackaging(new() { [standIn] = 0.85m });
        _db.SaveZeroCostListings([standIn]);
        Assert.False(_db.AdoptListingNumbers());                         // nothing sold yet

        _db.UpsertItems([new OrderItem { TransactionId = "t1", OrderId = "o1", ListingId = "4242", ItemName = "Brand New Shelf", SaleDate = new DateOnly(2026, 11, 20), Quantity = 1, Price = 30m }]);
        var data = _db.LoadShopData(new AppSettings(), []);
        Assert.Equal("4242", Assert.Single(data.Costs.Rules).ListingId);
        Assert.Equal(0.85m, data.Costs.PackagingByListing["4242"]);
        Assert.Contains("4242", data.Costs.ZeroCostListings);
        Assert.True(data.Costs.CostOf("4242", "", 1, new DateOnly(2026, 11, 20)).Mapped);
    }

    [Fact]
    public void Importing_sold_items_uses_the_listings_option_names_to_keep_a_typed_value_whole()
    {
        _db.ReplaceApiListings([new Listing
        {
            ListingId = "55", Title = "Family Sign", State = "active",
            Variations = [new("Size", ["Small", "Large"]), new("Wording", [])],
        }]);
        var file = Path.Combine(_dir, "items.csv");
        File.WriteAllText(file,
            "Sale Date,Item Name,Quantity,Price,Transaction ID,Listing ID,Order ID,Variations\n" +
            "10/05/26,Family Sign,1,30.00,t1,55,o1,\"Size:Large,Wording:Est. 2020, time: 5pm\"\n" +
            "10/06/26,Something Else,1,10.00,t2,99,o2,\"Size:Large,Color:Red\"\n");
        var result = new CsvImporter(_db, new AppSettings()).ImportFile(file);
        Assert.True(result.Ok, result.Summary);

        var items = _db.LoadItems().ToDictionary(i => i.TransactionId);
        var sign = Variations.Parse(items["t1"].Variations);
        Assert.Equal(["Size", "Wording"], sign.Select(p => p.Key));          // no made-up "time" option
        Assert.Equal("Est. 2020, time: 5pm", sign[1].Value);
        // A listing the app knows nothing about is read as before.
        Assert.Equal("Size:Large,Color:Red", items["t2"].Variations);
    }

    private const string StatementHeader = "Date,Type,Title,Info,Currency,Amount,\"Fees & Taxes\",Net,\"Tax Details\"\n";
    private const string Sale500 = "\"October 5, 2026\",Sale,\"Payment for Order #500\",,USD,$43.25,--,$43.25,--\n";
    private const string Fee500 = "\"October 5, 2026\",Fee,\"Processing fee\",\"Order #500\",USD,--,-$1.55,-$1.55,--\n";
    private const string Listing9 = "\"October 9, 2026\",Fee,\"Listing fee\",\"Listing #9\",USD,--,-$0.20,-$0.20,--\n";

    private ImportResult ImportStatement(string name, string rows)
    {
        var file = Path.Combine(_dir, name);
        File.WriteAllText(file, StatementHeader + rows);
        return new CsvImporter(_db, new AppSettings()).ImportFile(file);
    }

    [Fact]
    public void An_older_shorter_statement_does_not_replace_a_fuller_month()
    {
        Assert.True(ImportStatement("full.csv", Sale500 + Fee500 + Listing9).Ok);
        var older = ImportStatement("older.csv", Sale500 + Fee500);
        Assert.True(older.Ok);                                            // not an error: it just adds nothing
        Assert.Contains("left as it is", older.Summary);
        Assert.Equal(3, _db.LoadStatement().Count);

        // The usual case still works: the month downloaded again with more lines replaces it.
        var grown = ImportStatement("grown.csv", Sale500 + Fee500 + Listing9 + "\"October 20, 2026\",Fee,\"Listing fee\",\"Listing #9\",USD,--,-$0.20,-$0.20,--\n");
        Assert.Contains("month replaced", grown.Summary);
        Assert.Equal(4, _db.LoadStatement().Count);
    }

    [Fact]
    public void A_stray_line_from_the_month_next_door_does_not_wipe_that_month()
    {
        Assert.True(ImportStatement("october.csv", Sale500 + Fee500 + Listing9).Ok);
        // November's file, with one line dated in October that October doesn't have.
        var november = ImportStatement("november.csv",
            "\"November 3, 2026\",Sale,\"Payment for Order #600\",,USD,$20.00,--,$20.00,--\n" +
            "\"October 31, 2026\",Fee,\"Listing fee\",\"Listing #7\",USD,--,-$0.20,-$0.20,--\n");
        Assert.False(november.Ok);                                        // flagged, because lines were held back
        Assert.Contains("2026-10 was NOT changed", november.Summary);
        var months = _db.LoadStatement().ToLookup(l => l.Month);
        Assert.Equal(3, months["2026-10"].Count());                       // October is intact
        Assert.Single(months["2026-11"]);                                 // November went in
    }

    [Fact]
    public void A_statement_file_replaces_lines_the_api_filled_in_however_many_there_were()
    {
        var d = new DateOnly(2026, 10, 5);
        _db.ReplaceStatementMonths(["2026-10"], Enumerable.Range(1, 5).Select(n => new StatementLine { Date = d, Type = "API: x", Title = $"Line {n}", SourceFile = LedgerLines.ApiSource }));
        Assert.True(ImportStatement("october.csv", Sale500 + Fee500).Ok);
        Assert.Equal(2, _db.LoadStatement().Count);                       // the file always wins over API lines
    }

    [Fact]
    public void Typed_in_costs_can_be_exported_and_brought_back_into_an_empty_data_folder()
    {
        var day = new DateOnly(2026, 1, 1);
        _db.SaveMaterials(
        [
            new Material { Name = "Soy wax", Unit = "oz", Prices = [new(day, 0.20m), new(day.AddMonths(6), 0.22m)] },
            new Material { Name = "Wick", Unit = Units.Each, Prices = [new(day, 0.12m)] },
        ]);
        var mats = _db.LoadMaterials().ToDictionary(m => m.Name, m => m.Id);
        _db.SaveParts([new Part { Name = "8 oz candle", Notes = "Poured, not dipped", Versions = [new(day, [new(mats["Soy wax"], 7m), new(mats["Wick"], 1m)], 0, 15)] }]);
        var part = _db.LoadParts().Single().Id;
        _db.SaveRules(
        [
            new MappingRule { ListingId = "900", Name = "Candle", Conditions = new() { ["Size"] = "8 oz", ["Scent"] = "Fig, black tea: classic" }, Parts = [new(part, 2, "Pack of")] },
            new MappingRule { ListingId = "901", Name = "Gift card", FixedCost = 0.35m },
        ]);

        var folder = Path.Combine(_dir, "export");
        var files = CostsFile.Export(_db, folder);
        Assert.Equal(3, files.Count);

        // A fresh data folder, with the three files dropped on it in the "wrong" order.
        var fresh = new Database(Path.Combine(_dir, "fresh.db"));
        var results = new CsvImporter(fresh, new AppSettings()).ImportFiles(files.AsEnumerable().Reverse());
        Assert.All(results, r => Assert.True(r.Ok, r.Summary));
        Assert.Equal([CsvKind.Materials, CsvKind.Parts, CsvKind.Rules], results.Select(r => r.Kind));   // read in the order they depend on each other

        var wax = fresh.LoadMaterials().Single(m => m.Name == "Soy wax");
        Assert.Equal(("oz", 2), (wax.Unit, wax.Prices.Count));
        Assert.True(fresh.LoadMaterials().Single(m => m.Name == "Wick").IsComponent);
        var candle = fresh.LoadParts().Single();
        Assert.Equal(("8 oz candle", "Poured, not dipped", 15m), (candle.Name, candle.Notes, candle.Versions[0].OwnMinutes));
        Assert.Equal([7m, 1m], candle.Versions[0].Lines.Select(l => l.Amount));
        var rules = fresh.LoadRules();
        var rule = rules.Single(r => r.ListingId == "900");
        Assert.Equal("Fig, black tea: classic", rule.Conditions["Scent"]);                             // a value with a comma and a colon survives
        Assert.Equal(new RulePart(candle.Id, 2, "Pack of"), rule.Parts.Single());
        Assert.Equal(0.35m, rules.Single(r => r.ListingId == "901").FixedCost);

        // The costs come out the same as in the original.
        var sold = Variations.Format([new("Size", "8 oz"), new("Scent", "Fig, black tea: classic"), new("Pack of", "3")]);
        var a = _db.LoadShopData(new AppSettings(), []).Costs.CostOf("900", sold, 1, day.AddMonths(7));
        var b = fresh.LoadShopData(new AppSettings(), []).Costs.CostOf("900", sold, 1, day.AddMonths(7));
        Assert.True(a.Mapped);
        Assert.Equal(2 * 3 * 7m * 0.22m, a.Material);       // 2 candles × pack of 3 × 7 oz at the later price
        Assert.Equal(a, b);
    }

    [Fact]
    public void Importing_a_costs_file_changes_only_what_the_file_mentions_and_refuses_what_it_cannot_place()
    {
        var day = new DateOnly(2026, 1, 1);
        _db.SaveMaterials([new Material { Name = "PLA", Prices = [new(day, 0.02m)] }, new Material { Name = "PETG", Prices = [new(day, 0.03m)] }]);

        var file = Path.Combine(_dir, "materials.csv");
        File.WriteAllText(file, "Material,Unit,Cost per unit,Price from\nPLA,g,$0.025,2026-06-01\nMagnet,each,0.17,\n");
        var r = new CsvImporter(_db, new AppSettings()).ImportFile(file);
        Assert.True(r.Ok, r.Summary);
        var after = _db.LoadMaterials().ToDictionary(m => m.Name);
        Assert.Equal(2, after["PLA"].Prices.Count);                          // the old price is kept as history
        Assert.Equal(0.03m, after["PETG"].Prices.Single().CostPerUnit);      // not in the file: untouched
        Assert.True(after["Magnet"].IsComponent);

        var parts = Path.Combine(_dir, "parts.csv");
        File.WriteAllText(parts, "Part,Amounts from,Machine minutes,Your minutes,Material,Amount,Notes\nBracket,2026-01-01,30,0,Unobtainium,12,\n");
        var bad = new CsvImporter(_db, new AppSettings()).ImportFiles([parts]).Single();
        Assert.False(bad.Ok);
        Assert.Contains("Unobtainium", bad.Summary);
        Assert.Empty(_db.LoadParts());                                       // nothing half-imported
    }

    [Fact]
    public void Only_one_copy_of_the_app_can_hold_a_data_folder()
    {
        // A mutex belongs to a thread, so the "second copy" has to be another thread.
        CraftingMargins.App.InstanceLock? TakeOnAnotherThread(string folder)
        {
            CraftingMargins.App.InstanceLock? taken = null;
            var t = new Thread(() => { taken = CraftingMargins.App.InstanceLock.TryTake(folder); taken?.Dispose(); });
            t.Start();
            t.Join();
            return taken;
        }

        using (var first = CraftingMargins.App.InstanceLock.TryTake(_dir))
        {
            Assert.NotNull(first);
            Assert.Null(TakeOnAnotherThread(_dir));                                           // same folder: refused
            Assert.Null(TakeOnAnotherThread(_dir + Path.DirectorySeparatorChar));             // however the path is written
            Assert.NotNull(TakeOnAnotherThread(Path.Combine(_dir, "Demo")));                  // another folder: fine
        }
        Assert.NotNull(TakeOnAnotherThread(_dir));                                            // released when the first copy lets go
    }

    [Fact]
    public void A_listing_marked_as_costing_nothing_is_not_flagged()
    {
        var model = new CostModel { Rules = [new MappingRule { ListingId = "L", FixedCost = 0 }], ZeroCostListings = ["L"] };
        var c = model.CostOf("L", "", 1, new DateOnly(2026, 1, 1));
        Assert.False(model.NeedsCost("L", c));
        Assert.True(new CostModel { Rules = model.Rules }.NeedsCost("L", c));     // not marked: still flagged
        Assert.True(model.NeedsCost("Other", LineCost.Unmapped("No rules for this listing")));
    }
}

/// <summary>Backups: only when something changed, older generations kept, restore brings everything back.</summary>
public sealed class BackupFixTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private readonly IAppPaths _paths;
    private readonly Database _db;
    private readonly AppSettings _settings = new();

    public BackupFixTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = new FolderAppPaths(_dir);
        _db = new Database(_paths.DatabaseFile);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static DateTime At(int day, int second = 0) => new DateTime(2026, 11, 1, 9, 0, 0).AddDays(day - 1).AddSeconds(second);

    [Fact]
    public void Closing_without_changes_makes_no_backup_and_a_change_makes_one()
    {
        _db.SaveMaterials([new Material { Name = "PLA", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
        Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(1)));
        // Open, look, close: three times.  The one good backup is still the only one.
        for (int n = 1; n <= 3; n++)
        {
            _ = _db.LoadShopData(_settings, []);
            Assert.Null(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(1, n)));
        }
        Assert.Single(Backups.List(_paths.BackupFolder));

        _db.SaveMaterials([new Material { Name = "PETG", Prices = [new(new DateOnly(2026, 1, 1), 0.03m)] }]);
        Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(2)));
        Assert.Equal(2, Backups.List(_paths.BackupFolder).Count);
    }

    [Fact]
    public void A_change_to_the_category_rules_alone_also_counts()
    {
        Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(1)));
        CategoryRulesStore.Add(_paths, "Fee", "New kind of fee", Category.ListingFees);
        Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(1, 5)));
    }

    [Fact]
    public void The_newest_backups_are_kept_up_to_the_number_chosen()
    {
        _settings.BackupsToKeep = 3;
        for (int day = 1; day <= 6; day++)
        {
            _db.SaveMaterials([new Material { Name = $"Change {day}", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
            Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(day)));
        }
        var kept = Backups.List(_paths.BackupFolder);
        Assert.Equal(3, kept.Count);
        Assert.Contains("2026-11-06", kept[0]);
        Assert.Contains("2026-11-04", kept[2]);
    }

    [Fact]
    public void Zero_backups_to_keep_turns_the_closing_backup_off_and_leaves_old_backups_alone()
    {
        _db.SaveMaterials([new Material { Name = "PLA", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
        Assert.NotNull(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(1)));

        _settings.BackupsToKeep = 0;
        _db.SaveMaterials([new Material { Name = "PETG", Prices = [new(new DateOnly(2026, 1, 1), 0.03m)] }]);
        Assert.Null(Backups.MakeBackupIfChanged(_db, _paths, _settings, _paths.BackupFolder, At(2)));
        Assert.Single(Backups.List(_paths.BackupFolder));                        // the earlier one is still there

        // "Back up now" still works when asked for.
        Assert.NotNull(Backups.MakeBackup(_db, _paths.BackupFolder, _settings.BackupsToKeep, At(3), _paths));
    }

    [Fact]
    public void A_backup_count_saved_by_an_older_version_is_brought_into_range()
    {
        File.WriteAllText(_paths.SettingsFile, """{"BackupsToKeep": 50}""");
        Assert.Equal(AppSettings.MaxBackupsToKeep, AppSettings.Load(_paths).BackupsToKeep);
        Assert.Equal(3, new AppSettings().BackupsToKeep);
    }

    [Fact]
    public void Restore_brings_back_the_data_the_category_rules_and_the_shop_settings()
    {
        _db.SaveMaterials([new Material { Name = "PLA", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
        CategoryRulesStore.Add(_paths, "Fee", "Old rule", Category.ListingFees);
        _settings.ShopName = "Before";
        _settings.ProfitFloor = 7m;
        _settings.Save(_paths);
        var backup = Backups.MakeBackup(_db, _paths.BackupFolder, 3, At(1), _paths)!;

        _db.SaveMaterials([]);
        CategoryRulesStore.Save(_paths, []);
        _settings.ShopName = "After";
        _settings.ProfitFloor = 1m;
        _settings.EtsyKeystring = "stays";

        var json = Backups.Restore(_db, _paths.BackupFolder, backup, 3, _paths);
        Assert.Equal("PLA", Assert.Single(_db.LoadMaterials()).Name);
        Assert.Equal("Old rule", Assert.Single(CategoryRulesStore.Load(_paths)).TitleContains);
        Assert.NotNull(json);
        Assert.True(_settings.TakeShopSettingsFrom(json!));
        Assert.Equal(("Before", 7m, "stays"), (_settings.ShopName, _settings.ProfitFloor, _settings.EtsyKeystring));
        // The table that carries those files exists in backup files only.
        using var c = _db.Open();
        Assert.Empty(Database.Query(c, "SELECT name FROM sqlite_master WHERE name='BackupFiles'", r => r.GetString(0)));
    }

    [Fact]
    public void Restoring_a_backup_from_a_newer_version_is_refused_and_changes_nothing()
    {
        _db.SaveMaterials([new Material { Name = "Current", Prices = [new(new DateOnly(2026, 1, 1), 0.02m)] }]);
        var backup = Backups.MakeBackup(_db, _paths.BackupFolder, 3, At(1), _paths)!;
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
        {
            c.Open();
            Database.Exec(c, null, $"PRAGMA user_version = {Database.SchemaVersion + 5};");
        }
        Assert.Throws<DatabaseTooNewException>(() => Backups.Restore(_db, _paths.BackupFolder, backup, 3, _paths));
        Assert.Equal("Current", Assert.Single(_db.LoadMaterials()).Name);
    }
}
