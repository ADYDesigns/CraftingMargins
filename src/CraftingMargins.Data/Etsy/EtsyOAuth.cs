// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftingMargins.Data.Etsy;

/// <summary>Access + refresh token pair.  Both start with the numeric user ID and a dot.</summary>
public sealed class EtsyTokens
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>When this refresh token was issued; Etsy's last about 90 days.</summary>
    public DateTime RefreshIssuedUtc { get; set; }

    public long? UserId => AccessToken.IndexOf('.') is > 0 and var dot && long.TryParse(AccessToken[..dot], out var id) ? id : null;
    public bool AccessExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc.AddMinutes(-2);
}

public sealed record OAuthCallback(string? Code, string? State, string? Error, string? ErrorDescription);

public sealed class EtsyAuthException(string message) : Exception(message);

/// <summary>
/// OAuth 2.0 authorization code flow with PKCE (developers.etsy.com/documentation/essentials/authentication).
/// Read-only scopes only.
/// </summary>
public static class EtsyOAuth
{
    public const string AuthorizeUrl = "https://www.etsy.com/oauth/connect";
    public const string TokenUrl = "https://api.etsy.com/v3/public/oauth/token";
    public const string DefaultRedirectUri = "http://localhost:3003/callback";

    /// <summary>transactions_r: receipts, payments, ledger.  listings_r: listings and prices.  shops_r: find your shop ID.</summary>
    public static readonly string[] Scopes = ["transactions_r", "listings_r", "shops_r"];

    public sealed record Pkce(string Verifier, string Challenge);

    public static Pkce CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, ChallengeFor(verifier));
    }

    public static string ChallengeFor(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    public static string BuildAuthorizeUrl(string keystring, string redirectUri, string state, string challenge) =>
        AuthorizeUrl + "?" + string.Join("&",
            $"response_type=code",
            $"client_id={Uri.EscapeDataString(keystring)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(string.Join(' ', Scopes))}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(challenge)}",
            "code_challenge_method=S256");

    /// <summary>Reads code/state/error from the address Etsy sent the browser to (full URL or just the query).</summary>
    public static OAuthCallback ParseCallback(string urlOrQuery)
    {
        var text = urlOrQuery.Trim();
        var q = text.IndexOf('?');
        var query = q >= 0 ? text[(q + 1)..] : text;
        var hash = query.IndexOf('#');
        if (hash >= 0) query = query[..hash];
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var k = WebUtility.UrlDecode(eq >= 0 ? part[..eq] : part);
            var v = eq >= 0 ? WebUtility.UrlDecode(part[(eq + 1)..]) : "";
            values[k] = v;
        }
        return new(values.GetValueOrDefault("code"), values.GetValueOrDefault("state"),
                   values.GetValueOrDefault("error"), values.GetValueOrDefault("error_description"));
    }

    /// <summary>Checks the callback and returns the code, or throws with a message fit for the user.</summary>
    public static string CodeFrom(OAuthCallback cb, string expectedState)
    {
        if (cb.Error is { Length: > 0 })
            throw new EtsyAuthException(cb.Error == "access_denied"
                ? "Etsy says access was not granted."
                : $"Etsy returned an error: {cb.ErrorDescription ?? cb.Error}");
        if (cb.State != expectedState)
            throw new EtsyAuthException("The sign-in answer didn't match this request (state mismatch). Start the connection again.");
        if (string.IsNullOrEmpty(cb.Code))
            throw new EtsyAuthException("That address has no authorization code in it.");
        return cb.Code;
    }

    public static Task<EtsyTokens> ExchangeCodeAsync(HttpClient http, string keystring, string redirectUri, string code, string verifier, CancellationToken ct = default) =>
        PostTokenAsync(http, new()
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = keystring,
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        }, null, ct);

    public static Task<EtsyTokens> RefreshAsync(HttpClient http, string keystring, EtsyTokens old, CancellationToken ct = default) =>
        PostTokenAsync(http, new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = keystring,
            ["refresh_token"] = old.RefreshToken,
        }, old, ct);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
    }

    private static async Task<EtsyTokens> PostTokenAsync(HttpClient http, Dictionary<string, string> form, EtsyTokens? old, CancellationToken ct)
    {
        using var resp = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        TokenResponse? t = null;
        try { t = JsonSerializer.Deserialize<TokenResponse>(body); } catch (JsonException) { }
        if (!resp.IsSuccessStatusCode || t?.AccessToken is not { Length: > 0 })
        {
            var why = t?.ErrorDescription ?? t?.Error ?? $"HTTP {(int)resp.StatusCode}";
            throw new EtsyAuthException(old is null
                ? $"Etsy didn't accept the sign-in: {why}"
                : $"Etsy didn't renew the connection ({why}). Connect again on the Etsy API tab.");
        }
        var now = DateTime.UtcNow;
        return new EtsyTokens
        {
            AccessToken = t.AccessToken,
            RefreshToken = t.RefreshToken is { Length: > 0 } r ? r : old?.RefreshToken ?? "",
            ExpiresAtUtc = now.AddSeconds(t.ExpiresIn > 0 ? t.ExpiresIn : 3600),
            RefreshIssuedUtc = t.RefreshToken is { Length: > 0 } ? now : old?.RefreshIssuedUtc ?? now,
        };
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Waits for the browser to come back to http://localhost:PORT/... after the user approves.
/// Only works for an http:// localhost callback; for anything else the user pastes the address instead.
/// </summary>
public static class LoopbackListener
{
    public static bool CanListen(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp && u.IsLoopback;

    public static async Task<string> WaitForCallbackAsync(string redirectUri, CancellationToken ct)
    {
        var u = new Uri(redirectUri);
        using var listener = new HttpListener();
        var path = u.AbsolutePath.EndsWith('/') ? u.AbsolutePath : u.AbsolutePath + "/";
        listener.Prefixes.Add($"http://localhost:{u.Port}{path}");
        listener.Prefixes.Add($"http://127.0.0.1:{u.Port}{path}");
        listener.Start();
        using var reg = ct.Register(() => { try { listener.Stop(); } catch { } });
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }

            var url = ctx.Request.Url?.ToString() ?? "";
            var cb = EtsyOAuth.ParseCallback(url);
            var done = cb.Code is not null || cb.Error is not null;
            var html = done
                ? "<html><body style=\"font-family:sans-serif;margin:40px\"><h2>Crafting Margins</h2><p>You can close this tab and go back to the app.</p></body></html>"
                : "<html><body>Waiting for Etsy…</body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, ct);
            ctx.Response.Close();
            if (done) return url;   // ignore stray requests like /favicon.ico
        }
    }
}
