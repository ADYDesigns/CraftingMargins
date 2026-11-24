// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>CSV import and storage, using made-up files in a temp folder.</summary>
public sealed class DataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private readonly FolderAppPaths _paths;
    private readonly Database _db;
    private readonly CsvImporter _importer;

    public DataTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = new FolderAppPaths(_dir);
        _db = new Database(Path.Combine(_dir, "test.db"));
        _importer = new CsvImporter(_db, new AppSettings());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteCsv(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private const string StatementHeader = "Date,Type,Title,Info,Currency,Amount,\"Fees & Taxes\",Net,\"Tax Details\"\n";

    [Fact]
    public void Statement_import_replaces_the_month()
    {
        var first = WriteCsv("etsy_statement_2026_10.csv", StatementHeader +
            "\"October 5, 2026\",Sale,\"Payment for Order #500\",,USD,$43.25,--,$43.25,--\n" +
            "\"October 5, 2026\",Fee,\"Processing fee\",\"Order #500\",USD,--,-$1.55,-$1.55,--\n");
        Assert.Equal(CsvKind.Statement, _importer.ImportFile(first).Kind);
        Assert.Equal(2, _db.LoadStatement().Count);

        // Re-downloaded later with one more line: must replace, not append.
        var grown = WriteCsv("statement copy.csv", StatementHeader +
            "\"October 5, 2026\",Sale,\"Payment for Order #500\",,USD,$43.25,--,$43.25,--\n" +
            "\"October 5, 2026\",Fee,\"Processing fee\",\"Order #500\",USD,--,-$1.55,-$1.55,--\n" +
            "\"October 9, 2026\",Fee,\"Listing fee\",\"Listing #9\",USD,--,-$0.20,-$0.20,--\n");
        _importer.ImportFile(grown);
        var lines = _db.LoadStatement();
        Assert.Equal(3, lines.Count);
        Assert.Equal("500", lines[0].OrderId);
        Assert.Equal(43.25m, lines[0].Net);
    }

    [Fact]
    public void Order_files_import_without_buyer_details()
    {
        var orders = WriteCsv("EtsySoldOrders2026.csv",
            "\"Sale Date\",\"Order ID\",\"Buyer User ID\",\"Full Name\",\"First Name\",\"Last Name\",\"Number of Items\",\"Payment Method\",\"Date Shipped\",\"Street 1\",\"Street 2\",\"Ship City\",\"Ship State\",\"Ship Zipcode\",\"Ship Country\",Currency,\"Order Value\",\"Coupon Code\",\"Coupon Details\",\"Discount Amount\",\"Shipping Discount\",Shipping,\"Sales Tax\",\"Order Total\",Status,\"Card Processing Fees\",\"Order Net\",\"Adjusted Order Total\",\"Adjusted Card Processing Fees\",\"Adjusted Net Order Amount\",Buyer,\"Order Type\",\"Payment Type\",\"InPerson Discount\",\"InPerson Location\",SKU\n" +
            "10/05/26,500,99999,Zed Testbuyer,Zed,Testbuyer,2,Card,10/06/2026,\"1 Fake Street\",,Faketown,CO,80000,United States,USD,32.00,,,0,0,7.14,4.11,43.25,Completed,1.55,41.70,,,,zedbuyer,online,online_cc,,,\n");
        var items = WriteCsv("EtsySoldOrderItems2026.csv",
            "\"Sale Date\",\"Item Name\",Buyer,Quantity,Price,\"Coupon Code\",\"Coupon Details\",\"Discount Amount\",\"Shipping Discount\",\"Order Shipping\",\"Order Sales Tax\",\"Item Total\",Currency,\"Transaction ID\",\"Listing ID\",\"Date Paid\",\"Date Shipped\",\"Ship Name\",\"Ship Address1\",\"Ship Address2\",\"Ship City\",\"Ship State\",\"Ship Zipcode\",\"Ship Country\",\"Order ID\",Variations,\"Order Type\",\"Listings Type\",\"Payment Type\",\"InPerson Discount\",\"InPerson Location\",\"VAT Paid by Buyer\",SKU\n" +
            "10/05/26,\"Sign &quot;Custom&quot;\",zedbuyer,2,16.00,,,0.00,0.00,7.14,0,32,USD,7001,L1,10/05/26,10/06/2026,\"Zed Testbuyer\",\"1 Fake Street\",,Faketown,CO,80000,\"United States\",500,\"Size:Large,Personalization:Zed's Garage\",online,listing,online_cc,,,,\n");

        var results = _importer.ImportFiles([orders, items]);
        Assert.All(results, r => Assert.True(r.Ok, r.Summary));

        var o = Assert.Single(_db.LoadOrders());
        Assert.Equal(new DateOnly(2026, 10, 5), o.SaleDate);
        Assert.Equal(new DateOnly(2026, 10, 6), o.DateShipped);
        Assert.Equal(1.55m, o.CardProcessingFees);
        var i = Assert.Single(_db.LoadItems());
        Assert.Equal("Sign \"Custom\"", i.ItemName);
        Assert.Equal("Size:Large", i.Variations);

        // Nothing about the buyer may be in the database file.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var bytes = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(_db.FilePath));
        foreach (var secret in new[] { "Zed", "Testbuyer", "Fake Street", "Faketown", "zedbuyer", "99999", "80000" })
            Assert.DoesNotContain(secret, bytes);
    }

    [Fact]
    public void Manual_extra_shipping_survives_reimport()
    {
        _db.UpsertOrders([new Order { OrderId = "1", SaleDate = new(2026, 1, 1) }]);
        _db.SetExtraShippingCost("1", 4.50m);
        _db.UpsertOrders([new Order { OrderId = "1", SaleDate = new(2026, 1, 1), Status = "Completed" }]);
        Assert.Equal(4.50m, Assert.Single(_db.LoadOrders()).ExtraShippingCost);
    }

    [Fact]
    public void Unrecognized_file_is_rejected()
    {
        var r = _importer.ImportFile(WriteCsv("random.csv", "a,b,c\n1,2,3\n"));
        Assert.False(r.Ok);
        Assert.Equal(CsvKind.Unknown, r.Kind);
    }

    [Fact]
    public void Cost_model_round_trips()
    {
        _db.SaveMaterials([new Material { Name = "PLA", Unit = "oz", Prices = [new(new(2026, 1, 1), 0.015m)] }]);
        var mat = Assert.Single(_db.LoadMaterials());
        Assert.Equal("oz", mat.Unit);                 // the unit is stored with the material
        _db.SaveParts([new Part { Name = "Board", Versions = [new(new(2026, 1, 1), [new PartLine(mat.Id, 151m)], 285m, 12m)] }]);
        var part = Assert.Single(_db.LoadParts());
        _db.SaveRules([new MappingRule { ListingId = "L1", Name = "r", Conditions = new() { ["Size"] = "Large" }, Parts = [new(part.Id, 2, "Sets")], FixedCost = 1.25m }]);
        var rule = Assert.Single(_db.LoadRules());
        Assert.Equal("Large", rule.Conditions["size"]);
        Assert.Equal(1.25m, rule.FixedCost);
        Assert.Equal(new PartLine(mat.Id, 151m), Assert.Single(part.Versions[0].Lines));
        Assert.Equal((285m, 12m), (part.Versions[0].MachineMinutes, part.Versions[0].OwnMinutes));
        _db.SaveFeeRates([new FeeRates { OffsiteAdsRate = 0.12m }]);
        Assert.Equal(0.12m, Assert.Single(_db.LoadFeeRates()).OffsiteAdsRate);
    }

    [Fact]
    public void Backups_keep_exactly_n()
    {
        _db.UpsertOrders([new Order { OrderId = "1", SaleDate = new(2026, 1, 1) }]);
        for (int n = 0; n < 5; n++)
            Backups.MakeBackup(_db, ((IAppPaths)_paths).BackupFolder, keep: 3, now: new DateTime(2026, 11, 1, 10, 0, n));
        Assert.Equal(3, Backups.List(((IAppPaths)_paths).BackupFolder).Count);
        Assert.EndsWith("100004.db", Backups.List(((IAppPaths)_paths).BackupFolder)[0]);
    }
}
