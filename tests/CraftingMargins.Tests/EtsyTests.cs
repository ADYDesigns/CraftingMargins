// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Net;
using System.Text;
using CraftingMargins.Core;
using CraftingMargins.Data;
using CraftingMargins.Data.Etsy;

namespace CraftingMargins.Tests;

/// <summary>A fake Etsy: answers requests from a routing function and records what was sent.</summary>
internal sealed class FakeEtsy(Func<HttpRequestMessage, string, HttpResponseMessage> route) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Calls.Add((request, body));
        return route(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Etsy API pieces, tested with made-up responses only (no network).</summary>
public sealed class EtsyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private readonly Database _db;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    public EtsyTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "test.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static long Epoch(int y, int m, int d, int h = 12) => new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static EtsyTokens FreshTokens() => new()
    {
        AccessToken = "12345678.access", RefreshToken = "12345678.refresh",
        ExpiresAtUtc = DateTime.UtcNow.AddHours(1), RefreshIssuedUtc = DateTime.UtcNow,
    };

    private static EtsyClient Client(FakeEtsy fake, EtsyTokens? tokens = null, List<EtsyTokens>? saved = null) =>
        new(new HttpClient(fake), "keystr", "secret", tokens ?? FreshTokens(),
            t => { saved?.Add(t); return Task.CompletedTask; }, (_, _) => Task.CompletedTask);

    // ---------- OAuth ----------

    [Fact]
    public void Pkce_challenge_matches_the_RFC_7636_example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", EtsyOAuth.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var p = EtsyOAuth.CreatePkce();
        Assert.InRange(p.Verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9._~-]+$", p.Verifier);
    }

    [Fact]
    public void Authorize_url_asks_for_read_only_scopes_with_PKCE()
    {
        var url = EtsyOAuth.BuildAuthorizeUrl("keystr", "http://localhost:3003/callback", "st", "ch");
        Assert.StartsWith("https://www.etsy.com/oauth/connect?response_type=code&client_id=keystr", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A3003%2Fcallback", url);
        Assert.Contains("scope=transactions_r%20listings_r%20shops_r", url);
        Assert.Contains("code_challenge=ch&code_challenge_method=S256", url);
        Assert.DoesNotContain("_w", url);   // nothing that writes
    }

    [Fact]
    public void Callback_parsing_checks_state_and_errors()
    {
        var ok = EtsyOAuth.ParseCallback("https://localhost:3003/callback?code=abc%2B1&state=xyz");
        Assert.Equal("abc+1", EtsyOAuth.CodeFrom(ok, "xyz"));
        Assert.Throws<EtsyAuthException>(() => EtsyOAuth.CodeFrom(ok, "other"));
        var denied = EtsyOAuth.ParseCallback("?error=access_denied&state=xyz");
        Assert.Contains("not granted", Assert.Throws<EtsyAuthException>(() => EtsyOAuth.CodeFrom(denied, "xyz")).Message);
        Assert.True(LoopbackListener.CanListen("http://localhost:3003/callback"));
        Assert.False(LoopbackListener.CanListen("https://localhost:3003/callback"));
        Assert.False(LoopbackListener.CanListen("http://example.com/callback"));
    }

    [Fact]
    public async Task Loopback_listener_catches_the_browser_redirect()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var redirect = $"http://localhost:{port}/callback";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var wait = LoopbackListener.WaitForCallbackAsync(redirect, cts.Token);
        Assert.False(wait.IsFaulted, wait.Exception?.ToString());
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"http://localhost:{port}/callback/favicon.ico")).StatusCode);   // stray request ignored
        var page = await http.GetStringAsync($"{redirect}?code=abc&state=xyz");
        Assert.Contains("close this tab", page);
        var url = await wait;
        Assert.Equal("abc", EtsyOAuth.CodeFrom(EtsyOAuth.ParseCallback(url), "xyz"));
    }

    [Fact]
    public async Task Code_exchange_posts_the_verifier_and_reads_the_user_id()
    {
        var fake = new FakeEtsy((_, _) => FakeEtsy.Json("""{"access_token":"987.tok","token_type":"Bearer","expires_in":3600,"refresh_token":"987.ref"}"""));
        var t = await EtsyOAuth.ExchangeCodeAsync(new HttpClient(fake), "keystr", "http://localhost:3003/callback", "the-code", "the-verifier");
        Assert.Equal(987, t.UserId);
        Assert.Equal("987.ref", t.RefreshToken);
        var (req, body) = fake.Calls.Single();
        Assert.Equal(EtsyOAuth.TokenUrl, req.RequestUri!.ToString());
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code_verifier=the-verifier", body);
        Assert.Contains("client_id=keystr", body);
    }

    [Fact]
    public async Task Rejected_code_gives_a_readable_error()
    {
        var fake = new FakeEtsy((_, _) => FakeEtsy.Json("""{"error":"invalid_grant","error_description":"code was already redeemed"}""", HttpStatusCode.BadRequest));
        var ex = await Assert.ThrowsAsync<EtsyAuthException>(() => EtsyOAuth.ExchangeCodeAsync(new HttpClient(fake), "k", "r", "c", "v"));
        Assert.Contains("already redeemed", ex.Message);
    }

    // ---------- client ----------

    [Fact]
    public async Task Client_sends_both_keys_and_pages_through_results()
    {
        var fake = new FakeEtsy((req, _) =>
        {
            var offset = req.RequestUri!.Query.Contains("offset=100") ? 100 : 0;
            var n = offset == 0 ? 100 : 30;
            var rows = string.Join(",", Enumerable.Range(offset, n).Select(i => $"{{\"listing_id\":{i + 1},\"title\":\"L{i}\"}}"));
            return FakeEtsy.Json($"{{\"count\":130,\"results\":[{rows}]}}");
        });
        var all = await Client(fake).GetAllAsync<EtsyListing>("shops/1/listings", [new("state", "active")]);
        Assert.Equal(130, all.Count);
        Assert.Equal(2, fake.Calls.Count);
        var first = fake.Calls[0].Request;
        Assert.Equal("keystr:secret", first.Headers.GetValues("x-api-key").Single());
        Assert.Equal("Bearer 12345678.access", first.Headers.Authorization!.ToString());
        Assert.Contains("state=active&limit=100&offset=0", first.RequestUri!.Query);
        Assert.StartsWith("https://api.etsy.com/v3/application/shops/1/listings", first.RequestUri.ToString());
    }

    [Fact]
    public async Task Client_waits_out_429_and_renews_an_expired_token()
    {
        int apiCalls = 0;
        var fake = new FakeEtsy((req, _) =>
        {
            if (req.RequestUri!.ToString() == EtsyOAuth.TokenUrl)
                return FakeEtsy.Json("""{"access_token":"12345678.new","expires_in":3600,"refresh_token":"12345678.ref2"}""");
            apiCalls++;
            if (apiCalls == 1)
            {
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.Add("retry-after", "0");
                return r;
            }
            if (req.Headers.Authorization!.Parameter != "12345678.new") return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return FakeEtsy.Json("""{"user_id":12345678,"shop_id":555}""");
        });
        var saved = new List<EtsyTokens>();
        var me = await Client(fake, saved: saved).GetAsync<EtsySelf>("users/me");
        Assert.Equal(555, me.ShopId);
        Assert.Equal("12345678.new", saved.Single().AccessToken);
        Assert.Equal("12345678.ref2", saved.Single().RefreshToken);
        Assert.Contains(fake.Calls, c => c.Body.Contains("grant_type=refresh_token") && c.Body.Contains("refresh_token=12345678.refresh"));
    }

    // ---------- mapping ----------

    private static string ReceiptJson(long id, string status = "completed", bool shipped = true, string variations = """[{"formatted_name":"Color","formatted_value":"Red"},{"formatted_name":"Personalization","formatted_value":"For Jamie"}]""") => $$"""
        {"receipt_id":{{id}},"status":"{{status}}","is_paid":true,"is_shipped":{{(shipped ? "true" : "false")}},
         "name":"Buyer Name","buyer_email":"buyer@example.com","first_line":"1 Main St","city":"Town","zip":"00000",
         "message_from_buyer":"hello","buyer_user_id":42,
         "created_timestamp":{{Epoch(2026, 10, 3)}},"updated_timestamp":{{Epoch(2026, 10, 6)}},
         "grandtotal":{"amount":3622,"divisor":100,"currency_code":"USD"},
         "subtotal":{"amount":2800,"divisor":100,"currency_code":"USD"},
         "total_price":{"amount":3000,"divisor":100,"currency_code":"USD"},
         "total_shipping_cost":{"amount":522,"divisor":100,"currency_code":"USD"},
         "total_tax_cost":{"amount":300,"divisor":100,"currency_code":"USD"},
         "discount_amt":{"amount":200,"divisor":100,"currency_code":"USD"},
         "shipments":{{(shipped ? $"[{{\"shipment_notification_timestamp\":{Epoch(2026, 10, 5)}}}]" : "[]")}},
         "transactions":[{"transaction_id":{{id * 10}},"receipt_id":{{id}},"listing_id":777,"title":"Skull &amp; Bones","quantity":2,
           "price":{"amount":1500,"divisor":100,"currency_code":"USD"},"sku":"SK-1","created_timestamp":{{Epoch(2026, 10, 3)}},
           "shipped_timestamp":{{(shipped ? Epoch(2026, 10, 5) : 0)}},"variations":{{variations}}}]}
        """;

    [Fact]
    public void Ledger_entry_reads_a_reference_id_sent_as_a_number_or_as_text()
    {
        // Etsy documents reference_id as text but sends a number (seen on the first live sync).
        const string json = """
            {"count":2,"results":[
              {"entry_id":1,"sequence_number":1,"amount":-20,"currency":"USD","description":"listing","created_timestamp":1764000000,
               "ledger_type":"listing","reference_type":"listing","reference_id":4123456789,"parent_entry_id":null},
              {"entry_id":2,"sequence_number":2,"amount":1500,"currency":"USD","description":"sale","created_timestamp":1764000000,
               "ledger_type":"transaction","reference_type":"receipt","reference_id":"3987654321","parent_entry_id":1}]}
            """;
        var page = System.Text.Json.JsonSerializer.Deserialize<EtsyPage<EtsyLedgerEntry>>(json, EtsyJson.Options)!;
        Assert.Equal("4123456789", page.Results[0].ReferenceId);
        Assert.Equal("3987654321", page.Results[1].ReferenceId);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<EtsyLedgerEntry>("""{"reference_id":null}""", EtsyJson.Options)!.ReferenceId);
    }

    [Fact]
    public void Receipt_maps_like_the_order_export_and_drops_buyer_data_and_personalization()
    {
        var r = System.Text.Json.JsonSerializer.Deserialize<EtsyReceipt>(ReceiptJson(1001), EtsyJson.Options)!;
        var pay = new EtsyPayment { AmountFees = new EtsyMoney { Amount = 134, Divisor = 100 } };
        var o = EtsyMapper.ToOrder(r, [pay], Utc);
        Assert.Equal("1001", o.OrderId);
        Assert.Equal(new DateOnly(2026, 10, 3), o.SaleDate);
        Assert.Equal(new DateOnly(2026, 10, 5), o.DateShipped);
        Assert.Equal(30.00m, o.OrderValue);
        Assert.Equal(2.00m, o.DiscountAmount);
        Assert.Equal(5.22m, o.Shipping);
        Assert.Equal(3.00m, o.SalesTax);
        Assert.Equal(36.22m, o.OrderTotal);
        Assert.Equal(1.34m, o.CardProcessingFees);
        Assert.Equal(34.88m, o.OrderNet);
        Assert.Equal("Completed", o.Status);
        Assert.Null(o.AdjustedOrderTotal);

        var item = Assert.Single(EtsyMapper.ToItems(r, ["Personalization"], Utc));
        Assert.Equal("10010", item.TransactionId);
        Assert.Equal("777", item.ListingId);
        Assert.Equal("Skull & Bones", item.ItemName);
        Assert.Equal(15.00m, item.Price);
        Assert.Equal(30.00m, item.ItemTotal);
        Assert.Equal("Color:Red", item.Variations);   // personalization gone
        // Even with an empty ignore list, anything Etsy calls personalization is dropped.
        Assert.Equal("Color:Red", EtsyMapper.ToItems(r, [], Utc).Single().Variations);
        // The DTO has no buyer fields at all.
        Assert.DoesNotContain(typeof(EtsyReceipt).GetProperties(), p => p.Name.Contains("Buyer") || p.Name is "Name" or "City" or "Zip" or "FirstLine");
    }

    [Fact]
    public void Listing_maps_variations_and_the_price_of_every_option()
    {
        var json = """
            {"listing_id":777,"title":"Skull &amp; Bones","state":"active","quantity":10,"num_favorers":12,
             "materials":["PLA"],"skus":["SK-1"],"price":{"amount":1500,"divisor":100,"currency_code":"USD"},
             "inventory":{"products":[
               {"sku":"SK-1","is_deleted":false,"property_values":[{"property_name":"Color","values":["Red"]},{"property_name":"Size","values":["Small"]}],
                "offerings":[{"quantity":5,"is_enabled":true,"is_deleted":false,"price":{"amount":1500,"divisor":100}}]},
               {"sku":"SK-2","is_deleted":false,"property_values":[{"property_name":"Color","values":["Blue"]},{"property_name":"Size","values":["Large"]}],
                "offerings":[{"quantity":5,"is_enabled":true,"is_deleted":false,"price":{"amount":2250,"divisor":100}}]},
               {"sku":"OLD","is_deleted":true,"property_values":[{"property_name":"Color","values":["Gone"]}],"offerings":[]}]}}
            """;
        var l = EtsyMapper.ToListing(System.Text.Json.JsonSerializer.Deserialize<EtsyListing>(json, EtsyJson.Options)!);
        Assert.Equal("777", l.ListingId);
        Assert.Equal("Skull & Bones", l.Title);
        Assert.Equal(12, l.NumFavorers);
        Assert.Equal(["Red", "Blue"], l.Variations.Single(v => v.Name == "Color").Values);
        Assert.Equal(2, l.Offerings.Count);
        Assert.Equal(22.50m, l.Offerings.Single(o => o.Options["Size"] == "Large").Price);
    }

    [Fact]
    public void Ledger_entry_becomes_a_statement_line_in_dollars()
    {
        var e = EtsyMapper.ToLedger(new EtsyLedgerEntry
        {
            EntryId = 5, Amount = -195, CreatedTimestamp = Epoch(2026, 10, 3), LedgerType = "transaction",
            Description = "Transaction fee: Skull", ReferenceType = "receipt", ReferenceId = "1001",
        }, Utc);
        Assert.Equal(-1.95m, e.Amount);
        var line = LedgerLines.ToStatementLine(e);
        Assert.Equal("API: transaction", line.Type);
        Assert.Equal("1001", line.OrderId);
        Assert.Equal(line.Amount + line.FeesAndTaxes, line.Net);
        Assert.Equal(LedgerLines.ApiSource, line.SourceFile);
    }

    // ---------- ledger compare ----------

    private static StatementLine Csv(int day, string type, string title, string info, decimal net)
    {
        var l = new StatementLine { Date = new DateOnly(2026, 10, day), Type = type, Title = title, Info = info, Amount = net, Net = net, SourceFile = "etsy_statement_2026_10.csv" };
        StatementIds.Fill(l);
        return l;
    }

    private static LedgerEntry Api(long id, int day, string type, string desc, decimal amount, string refType = "", string refId = "") =>
        new() { EntryId = id, SequenceNumber = id, Date = new DateOnly(2026, 10, day), LedgerType = type, Description = desc, Amount = amount, ReferenceType = refType, ReferenceId = refId };

    [Fact]
    public void Ledger_compare_learns_categories_from_the_statement()
    {
        var csv = new List<StatementLine>
        {
            Csv(3, "Sale", "Payment for Order #1001", "", 36.22m),
            Csv(3, "Fee", "Transaction fee: Skull", "Order #1001", -1.95m),
            Csv(3, "Fee", "Transaction fee: Shipping", "Order #1001", -0.34m),
            Csv(3, "Fee", "Processing fee", "Order #1001", -1.34m),
            Csv(5, "Shipping", "USPS shipping label", "Label #9", -5.22m),
            Csv(4, "Fee", "Listing fee", "Listing #777", -0.20m),
            Csv(20, "Deposit", "$27.17 sent to your bank account", "", 0m),
        };
        var api = new List<LedgerEntry>
        {
            Api(1, 3, "payment", "Payment", 36.22m, "receipt", "1001"),
            Api(2, 3, "transaction", "Transaction fee", -1.95m, "receipt", "1001"),
            Api(3, 3, "transaction", "Transaction fee", -0.34m, "receipt", "1001"),
            Api(4, 4, "processing_fee", "Processing fee", -1.34m, "receipt", "1001"),   // a day off
            Api(5, 5, "shipping_labels", "Shipping label", -5.22m, "shipping_label", "9"),
            Api(6, 4, "listing", "Listing fee", -0.20m, "listing", "777"),
        };
        var r = LedgerCompare.Compare("2026-10", csv, api, new Categorizer());
        Assert.Equal(6, r.Matched);
        Assert.Single(r.Unmatched);                     // the $0 deposit line
        Assert.True(r.Passed, string.Join("; ", r.Types.Select(t => t.Note)));
        Assert.Equal(r.StatementTotal, r.LedgerTotal);
        Assert.Contains(r.ProposedRules, x => x.Type == "API: payment" && x.Category == Category.GrossSales);
        Assert.Contains(r.ProposedRules, x => x.Type == "API: shipping_labels" && x.Category == Category.ShippingLabels);
        // The learned rules make the API lines categorize like the CSV.
        var learned = new Categorizer(r.ProposedRules);
        Assert.Equal(Category.ProcessingFees, learned.Categorize(LedgerLines.ToStatementLine(api[3])));
    }

    [Fact]
    public void Ledger_compare_splits_a_mixed_type_by_description_and_never_guesses()
    {
        var csv = new List<StatementLine>
        {
            Csv(3, "Fee", "Transaction fee: Skull", "Order #1", -1.00m),
            Csv(3, "Marketing", "Offsite Ads fee", "Order #1", -2.00m),
            Csv(6, "Mystery", "Something new", "", -3.00m),
        };
        var api = new List<LedgerEntry>
        {
            Api(1, 3, "seller_fee", "Transaction fee", -1.00m),
            Api(2, 3, "seller_fee", "Offsite ads fee", -2.00m),
            Api(3, 6, "odd", "Odd", -3.00m),
            Api(4, 7, "lonely", "Only on the API", -4.00m),
        };
        var r = LedgerCompare.Compare("2026-10", csv, api, new Categorizer());
        Assert.False(r.Passed);
        Assert.Contains(r.ProposedRules, x => x.Type == "API: seller_fee" && x.TitleContains == "Offsite ads fee" && x.Category == Category.OffsiteAds);
        Assert.Contains(r.Types, t => t.LedgerType == "odd" && !t.Resolved);
        Assert.Contains(r.Types, t => t.LedgerType == "lonely" && t.Note == "No matching statement line");
        Assert.DoesNotContain(r.ProposedRules, x => x.Type is "API: odd" or "API: lonely");
    }

    // ---------- storage ----------

    [Fact]
    public void Api_months_never_overwrite_a_statement_csv_month()
    {
        _db.ReplaceStatementMonths(["2026-10"], [Csv(3, "Sale", "Payment for Order #1001", "", 36.22m)]);
        _db.UpsertLedger([Api(1, 3, "payment", "Payment", 36.22m), new LedgerEntry { EntryId = 2, Date = new DateOnly(2026, 11, 2), LedgerType = "payment", Amount = 10m }]);
        Assert.Equal(["2026-11"], _db.RebuildApiStatementMonths());
        var lines = _db.LoadStatement();
        Assert.Equal(2, lines.Count);
        Assert.Equal("etsy_statement_2026_10.csv", lines.Single(l => l.Month == "2026-10").SourceFile);
        Assert.Equal(LedgerLines.ApiSource, lines.Single(l => l.Month == "2026-11").SourceFile);

        // Importing November's CSV later replaces the API lines; a rebuild leaves it alone.
        _db.ReplaceStatementMonths(["2026-11"], [new StatementLine { Date = new DateOnly(2026, 11, 2), Type = "Sale", Title = "Payment for Order #2", Amount = 10, Net = 10, SourceFile = "etsy_statement_2026_11.csv" }]);
        Assert.Empty(_db.RebuildApiStatementMonths());
        Assert.DoesNotContain(_db.LoadStatement(), l => l.SourceFile == LedgerLines.ApiSource);
        // Upserting the same entry twice doesn't duplicate it.
        _db.UpsertLedger([Api(1, 3, "payment", "Payment", 36.22m)]);
        Assert.Equal(2, _db.LoadLedger().Count);
    }

    [Fact]
    public void Listings_keep_api_fields()
    {
        _db.ReplaceListings([new Listing { Title = "A", ListingId = "9", State = "active", NumFavorers = 3, Offerings = [new(new() { ["Size"] = "S" }, 12.5m, 1, true)] }]);
        var l = Assert.Single(_db.LoadListings());
        Assert.Equal("9", l.ListingId);
        Assert.Equal(3, l.NumFavorers);
        Assert.Equal(12.5m, l.Offerings.Single().Price);
    }

    [Fact]
    public void Dpapi_store_round_trips_and_the_file_is_not_plain_text()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new DpapiSecretStore(Path.Combine(_dir, "Secrets"));
        store.Set("etsy-shared-secret", "very-secret-value");
        Assert.Equal("very-secret-value", store.Get("etsy-shared-secret"));
        var raw = File.ReadAllBytes(Path.Combine(_dir, "Secrets", "etsy-shared-secret.bin"));
        Assert.DoesNotContain("very-secret", Encoding.UTF8.GetString(raw));
        store.Delete("etsy-shared-secret");
        Assert.Null(store.Get("etsy-shared-secret"));
    }

    // ---------- whole sync ----------

    [Fact]
    public async Task Sync_fills_orders_listings_and_ledger_and_removes_cancelled_orders()
    {
        var settings = new AppSettings { EtsyShopId = 555, ApiSyncFrom = "2026-10-01", UseApiLedger = true };
        _db.UpsertOrders([new Order { OrderId = "1002", SaleDate = new DateOnly(2026, 10, 1), ShippingDiscount = 1.5m }]);
        _db.UpsertOrders([new Order { OrderId = "1003", SaleDate = new DateOnly(2026, 10, 2) }]);

        var fake = new FakeEtsy((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/receipts"))
                return FakeEtsy.Json($"{{\"count\":3,\"results\":[{ReceiptJson(1001)},{ReceiptJson(1002, shipped: false)},{ReceiptJson(1003, status: "canceled")}]}}");
            if (path.EndsWith("/1001/payments"))
                return FakeEtsy.Json("""{"count":1,"results":[{"payment_id":1,"receipt_id":1001,"amount_fees":{"amount":134,"divisor":100}}]}""");
            if (path.EndsWith("/listings"))
                return FakeEtsy.Json("""{"count":1,"results":[{"listing_id":777,"title":"Skull","state":"active","quantity":3,"price":{"amount":1500,"divisor":100}}]}""");
            if (path.EndsWith("/ledger-entries"))
                return req.RequestUri.Query.Contains($"min_created={Epoch(2026, 10, 1, 0)}")
                    ? FakeEtsy.Json($"{{\"count\":1,\"results\":[{{\"entry_id\":1,\"amount\":3622,\"currency\":\"USD\",\"description\":\"Payment\",\"created_timestamp\":{Epoch(2026, 10, 3)},\"ledger_type\":\"payment\",\"reference_type\":\"receipt\",\"reference_id\":\"1001\"}}]}}")
                    : FakeEtsy.Json("""{"count":0,"results":[]}""");
            return FakeEtsy.Json("{}", HttpStatusCode.NotFound);
        });

        var result = await new EtsySync(Client(fake), _db, settings, Utc).SyncAsync();

        var orders = _db.LoadOrders().ToDictionary(o => o.OrderId);
        Assert.Equal(["1001", "1002"], orders.Keys.Order());
        Assert.Equal(1.34m, orders["1001"].CardProcessingFees);
        Assert.Equal(1.5m, orders["1002"].ShippingDiscount);        // kept from the CSV
        Assert.Null(orders["1002"].DateShipped);
        Assert.Equal(1, result.Cancelled);
        Assert.Equal(2, _db.LoadItems().Count);
        Assert.Equal("777", _db.LoadListings().Single().ListingId);
        Assert.Contains("2026-10", result.ApiMonths);
        Assert.Equal(36.22m, _db.LoadStatement().Single().Net);
        Assert.Equal(Epoch(2026, 10, 6), settings.ReceiptsModifiedSince);
        Assert.NotNull(settings.LastApiSync);
        // Payments are asked for on unshipped orders too (Etsy charges the card fee at the sale);
        // "not found" for one just means no fee yet.  Nothing is ever written to Etsy.
        Assert.Contains(fake.Calls, c => c.Request.RequestUri!.AbsolutePath.EndsWith("/1002/payments"));
        Assert.All(fake.Calls, c => Assert.Equal(HttpMethod.Get, c.Request.Method));

        // The next sync only asks for receipts changed since (minus a day of overlap).
        fake.Calls.Clear();
        await new EtsySync(Client(fake), _db, settings, Utc).SyncAsync();
        Assert.Contains($"min_last_modified={Epoch(2026, 10, 5)}", fake.Calls.First(c => c.Request.RequestUri!.AbsolutePath.EndsWith("/receipts")).Request.RequestUri!.Query);
    }

    private static FakeEtsy OneOrderShop() => new((req, _) =>
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("/receipts")) return FakeEtsy.Json($"{{\"count\":1,\"results\":[{ReceiptJson(1001)}]}}");
        if (path.EndsWith("/payments")) return FakeEtsy.Json("""{"count":0,"results":[]}""");
        if (path.EndsWith("/users/me")) return FakeEtsy.Json("""{"user_id":9,"shop_id":777}""");
        return FakeEtsy.Json("""{"count":0,"results":[]}""");
    });

    [Fact]
    public async Task Sync_removes_a_line_that_is_no_longer_on_its_order_and_leaves_other_orders_alone()
    {
        var settings = new AppSettings { EtsyShopId = 555, ApiSyncFrom = "2026-10-01" };
        var day = new DateOnly(2026, 10, 3);
        _db.UpsertItems(
        [
            new OrderItem { TransactionId = "old-line", OrderId = "1001", ListingId = "777", ItemName = "Taken off the order", SaleDate = day, Quantity = 1, Price = 5m },
            new OrderItem { TransactionId = "csv-only", OrderId = "2002", ListingId = "777", ItemName = "An order the API didn't mention", SaleDate = day, Quantity = 1, Price = 5m },
        ]);

        await new EtsySync(Client(OneOrderShop()), _db, settings, Utc).SyncAsync();

        var items = _db.LoadItems().Select(i => i.TransactionId).Order().ToList();
        Assert.Equal(["10010", "csv-only"], items);     // 1001 now has exactly the line Etsy lists; 2002 is untouched
    }

    [Fact]
    public async Task A_second_shop_is_refused_once_the_folder_holds_one_shops_data()
    {
        var settings = new AppSettings { EtsyShopId = 555, ApiSyncFrom = "2026-10-01" };
        await new EtsySync(Client(OneOrderShop()), _db, settings, Utc).SyncAsync();
        Assert.Equal(555, settings.DataEtsyShopId);                     // the folder now belongs to shop 555

        // Disconnect, then connect keys that turn out to be for shop 777.
        settings.EtsyShopId = null;
        var ex = await Assert.ThrowsAsync<EtsyAuthException>(() => new EtsySync(Client(OneOrderShop()), _db, settings, Utc).SyncAsync());
        Assert.Contains("555", ex.Message);
        Assert.Contains("777", ex.Message);
        Assert.Null(settings.EtsyShopId);                               // not left connected to the wrong shop
        Assert.Equal(["1001"], _db.LoadOrders().Select(o => o.OrderId));

        // The same shop again is fine, and an emptied folder takes any shop.
        settings.EtsyShopId = 555;
        await new EtsySync(Client(OneOrderShop()), _db, settings, Utc).SyncAsync();
        settings.DataEtsyShopId = null;
        settings.EtsyShopId = null;
        await new EtsySync(Client(OneOrderShop()), _db, settings, Utc).SyncAsync();
        Assert.Equal(777, settings.DataEtsyShopId);
    }

    [Fact]
    public async Task Connection_keeps_secret_out_of_settings_and_completes_sign_in()
    {
        var settings = new AppSettings();
        var secrets = new MemorySecretStore();
        var fake = new FakeEtsy((req, _) => req.RequestUri!.ToString() == EtsyOAuth.TokenUrl
            ? FakeEtsy.Json("""{"access_token":"12345678.tok","expires_in":3600,"refresh_token":"12345678.ref"}""")
            : FakeEtsy.Json("""{"user_id":12345678,"shop_id":555}"""));
        var conn = new EtsyConnection(settings, secrets, new HttpClient(fake));
        conn.SaveKeys(" keystr ", "s3cret");
        Assert.Equal("keystr", settings.EtsyKeystring);
        Assert.DoesNotContain("s3cret", System.Text.Json.JsonSerializer.Serialize(settings));

        var pending = conn.Begin();
        await Assert.ThrowsAsync<EtsyAuthException>(() => conn.CompleteAsync(pending, "http://localhost:3003/callback?code=c&state=wrong"));
        await conn.CompleteAsync(pending, $"http://localhost:3003/callback?code=c&state={Uri.EscapeDataString(pending.State)}");
        Assert.True(conn.IsConnected);
        Assert.Equal(555, settings.EtsyShopId);
        Assert.Equal(12345678, settings.EtsyUserId);

        conn.Forget();
        Assert.False(conn.IsConnected);
        Assert.Null(settings.EtsyShopId);
        Assert.True(conn.HasKeys);
    }
}
