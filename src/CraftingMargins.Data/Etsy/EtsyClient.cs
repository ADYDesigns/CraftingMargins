// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CraftingMargins.Data.Etsy;

public sealed class EtsyApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Calls Etsy Open API v3 for one shop.  Sends x-api-key (keystring:shared secret) and the
/// bearer token on every call, renews the token when it runs out, follows Etsy's rate-limit
/// headers, and waits out HTTP 429.  Read-only: it has no method that changes anything on Etsy.
/// </summary>
public sealed class EtsyClient
{
    public const string BaseUrl = "https://api.etsy.com/v3/application/";
    public const int PageSize = 100;

    private readonly HttpClient _http;
    private readonly string _keystring;
    private readonly string _sharedSecret;
    private readonly Func<EtsyTokens, Task> _saveTokens;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EtsyTokens Tokens { get; private set; }
    public int CallCount { get; private set; }
    /// <summary>From the last response's x-remaining-today header, if Etsy sent one.</summary>
    public int? RemainingToday { get; private set; }

    public EtsyClient(HttpClient http, string keystring, string sharedSecret, EtsyTokens tokens,
                      Func<EtsyTokens, Task> saveTokens, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _keystring = keystring;
        _sharedSecret = sharedSecret;
        Tokens = tokens;
        _saveTokens = saveTokens;
        _delay = delay ?? Task.Delay;
    }

    public string ApiKeyHeader => $"{_keystring}:{_sharedSecret}";

    public async Task<T> GetAsync<T>(string path, IEnumerable<KeyValuePair<string, string>>? query = null, CancellationToken ct = default)
    {
        var url = BaseUrl + path.TrimStart('/');
        var q = query?.ToList() ?? [];
        if (q.Count > 0) url += "?" + string.Join("&", q.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        bool refreshed = false;
        for (int attempt = 0; ; attempt++)
        {
            if (Tokens.AccessExpired(DateTime.UtcNow) && !refreshed)
            {
                await RenewAsync(ct);
                refreshed = true;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", ApiKeyHeader);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.AccessToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);
            CallCount++;
            ReadRateHeaders(resp);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt < 4)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Log.Write($"Etsy API rate limit reached; waiting {wait.TotalSeconds:0} s.");
                await _delay(wait, ct);
                continue;
            }
            // A passing fault on Etsy's side (500, 502, 503, 504): try again a couple of times before giving up.
            if ((int)resp.StatusCode >= 500 && attempt < 2)
            {
                await _delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
                continue;
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                await RenewAsync(ct);
                refreshed = true;
                continue;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new EtsyApiException(Explain(resp.StatusCode, body, path), resp.StatusCode);

            // Stay under the per-second limit.
            if (resp.Headers.TryGetValues("x-remaining-this-second", out var rs) &&
                int.TryParse(rs.FirstOrDefault(), out var left) && left <= 0)
                await _delay(TimeSpan.FromSeconds(1), ct);

            return JsonSerializer.Deserialize<T>(body, EtsyJson.Options)
                   ?? throw new EtsyApiException($"Etsy sent an empty answer for {path}.");
        }
    }

    /// <summary>Reads every page of a list endpoint (limit 100, offset paging).</summary>
    public async Task<List<T>> GetAllAsync<T>(string path, IEnumerable<KeyValuePair<string, string>>? query = null,
                                              IProgress<string>? progress = null, string? what = null, CancellationToken ct = default)
    {
        var all = new List<T>();
        var baseQuery = query?.ToList() ?? [];
        for (int offset = 0; ; offset += PageSize)
        {
            var q = new List<KeyValuePair<string, string>>(baseQuery)
            {
                new("limit", PageSize.ToString(CultureInfo.InvariantCulture)),
                new("offset", offset.ToString(CultureInfo.InvariantCulture)),
            };
            var page = await GetAsync<EtsyPage<T>>(path, q, ct);
            all.AddRange(page.Results);
            if (what is not null) progress?.Report($"{what}: {all.Count}" + (page.Count > 0 ? $" of {page.Count}" : ""));
            if (page.Results.Count < PageSize || (page.Count > 0 && all.Count >= page.Count)) return all;
        }
    }

    private async Task RenewAsync(CancellationToken ct)
    {
        Tokens = await EtsyOAuth.RefreshAsync(_http, _keystring, Tokens, ct);
        await _saveTokens(Tokens);
        Log.Write("Etsy API token renewed.");
    }

    private void ReadRateHeaders(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("x-remaining-today", out var v) && int.TryParse(v.FirstOrDefault(), out var n))
            RemainingToday = n;
    }

    private static string Explain(HttpStatusCode code, string body, string path)
    {
        string? msg = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e)) msg = e.GetString();
        }
        catch (JsonException) { }
        var detail = msg is { Length: > 0 } ? $": {msg}" : "";
        return code switch
        {
            HttpStatusCode.Unauthorized => "Etsy no longer accepts this connection. Connect again on the Etsy API tab" + detail,
            HttpStatusCode.Forbidden => "Etsy refused the request (check that the keystring and shared secret are right, and that the app is approved)" + detail,
            HttpStatusCode.NotFound => $"Etsy couldn't find {path}" + detail,
            HttpStatusCode.TooManyRequests => "Etsy's daily request limit is used up; try again tomorrow" + detail,
            _ => $"Etsy API error {(int)code} for {path}" + detail,
        };
    }
}
