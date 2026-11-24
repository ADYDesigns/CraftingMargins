// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;

namespace CraftingMargins.Data.Etsy;

/// <summary>A sign-in that has been started in the browser and is waiting for Etsy's answer.</summary>
public sealed record PendingAuth(string AuthorizeUrl, string State, string Verifier, string RedirectUri);

/// <summary>
/// The user's own Etsy Seller App ("bring your own key") and its tokens.  The keystring lives in
/// settings; the shared secret and tokens only in the encrypted secret store.
/// </summary>
public sealed class EtsyConnection(AppSettings settings, ISecretStore secrets, HttpClient http)
{
    private const string SecretName = "etsy-shared-secret";
    private const string TokensName = "etsy-tokens";

    public bool HasKeys => settings.EtsyKeystring.Length > 0 && secrets.Get(SecretName) is { Length: > 0 };
    public bool HasSharedSecret => secrets.Get(SecretName) is { Length: > 0 };
    public EtsyTokens? Tokens => secrets.Get(TokensName) is { } json ? JsonSerializer.Deserialize<EtsyTokens>(json) : null;
    public bool IsConnected => HasKeys && Tokens is { RefreshToken.Length: > 0 };

    /// <summary>Refresh tokens last about 90 days; after that the user has to connect again.</summary>
    public DateTime? RenewBy => Tokens is { } t ? t.RefreshIssuedUtc.AddDays(90).ToLocalTime() : null;

    public void SaveKeys(string keystring, string? sharedSecret)
    {
        var k = keystring.Trim();
        if (k != settings.EtsyKeystring) Forget();   // a different app: old tokens and shop no longer apply
        settings.EtsyKeystring = k;
        if (!string.IsNullOrWhiteSpace(sharedSecret)) secrets.Set(SecretName, sharedSecret.Trim());
    }

    public PendingAuth Begin()
    {
        if (!HasKeys) throw new EtsyAuthException("Enter the keystring and shared secret first.");
        var pkce = EtsyOAuth.CreatePkce();
        var state = EtsyOAuth.NewState();
        var redirect = settings.EtsyRedirectUri.Trim();
        return new PendingAuth(EtsyOAuth.BuildAuthorizeUrl(settings.EtsyKeystring, redirect, state, pkce.Challenge), state, pkce.Verifier, redirect);
    }

    /// <summary>Finishes the sign-in from the address Etsy sent the browser to, then looks up the shop.</summary>
    public async Task CompleteAsync(PendingAuth pending, string callbackUrl, CancellationToken ct = default)
    {
        var code = EtsyOAuth.CodeFrom(EtsyOAuth.ParseCallback(callbackUrl), pending.State);
        var tokens = await EtsyOAuth.ExchangeCodeAsync(http, settings.EtsyKeystring, pending.RedirectUri, code, pending.Verifier, ct);
        SaveTokens(tokens);
        settings.EtsyShopId = null;
        settings.EtsyUserId = tokens.UserId;
        try { await EtsySync.LookUpShopAsync(CreateClient(), settings, ct); }
        catch (EtsyAuthException)
        {
            // These keys are for a different shop than the one this data folder holds: don't stay connected to it.
            Forget();
            throw;
        }
        Log.Write($"Connected to Etsy shop {settings.EtsyShopId}.");
    }

    public EtsyClient CreateClient(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var secret = secrets.Get(SecretName) ?? throw new EtsyAuthException("The shared secret is missing. Enter it again on the Etsy API tab.");
        var tokens = Tokens ?? throw new EtsyAuthException("Not connected to Etsy yet.");
        return new EtsyClient(http, settings.EtsyKeystring, secret, tokens, t => { SaveTokens(t); return Task.CompletedTask; }, delay);
    }

    public void SaveTokens(EtsyTokens t) => secrets.Set(TokensName, JsonSerializer.Serialize(t));

    /// <summary>Disconnect: forgets the tokens and shop.  The keys stay so connecting again is one click.</summary>
    public void Forget()
    {
        secrets.Delete(TokensName);
        settings.EtsyShopId = null;
        settings.EtsyUserId = null;
        settings.ReceiptsModifiedSince = 0;
        settings.LastApiSync = null;
    }

    /// <summary>Removes the keys too.</summary>
    public void RemoveKeys()
    {
        Forget();
        secrets.Delete(SecretName);
        settings.EtsyKeystring = "";
    }
}
