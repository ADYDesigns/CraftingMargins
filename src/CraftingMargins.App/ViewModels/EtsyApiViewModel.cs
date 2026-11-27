// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;
using CraftingMargins.Data;
using CraftingMargins.Data.Etsy;

namespace CraftingMargins.App.ViewModels;

/// <summary>The Etsy API tab: your own Seller App's keys, connecting, syncing, and Ledger compare.</summary>
public sealed partial class EtsyApiViewModel(MainViewModel main) : ObservableObject
{
    public const string DeveloperPortalUrl = "https://www.etsy.com/developers/your-apps";

    [ObservableProperty] private string _keystring = "";
    [ObservableProperty] private string _sharedSecret = "";
    [ObservableProperty] private string _sharedSecretHint = "";
    [ObservableProperty] private string _redirectUri = EtsyOAuth.DefaultRedirectUri;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionText = "";
    [ObservableProperty] private bool _waitingForBrowser;
    [ObservableProperty] private string _pastedAddress = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _syncFrom = "";
    [ObservableProperty] private bool _useApiLedger;
    [ObservableProperty] private string _lastSyncText = "";
    [ObservableProperty] private string? _compareMonth;
    [ObservableProperty] private string _compareSummary = "";
    [ObservableProperty] private bool _compareRan;
    [ObservableProperty] private bool _comparePassed;
    [ObservableProperty] private bool _canApplyRules;
    [ObservableProperty] private bool _hasUnmatched;

    public ObservableCollection<string> CompareMonths { get; } = [];
    public ObservableCollection<LedgerTypeRow> CompareTypes { get; } = [];
    public ObservableCollection<UnmatchedLine> CompareUnmatched { get; } = [];
    public string ScopesText => string.Join(", ", EtsyOAuth.Scopes);

    private PendingAuth? _pending;
    private CancellationTokenSource? _connectCts;
    private LedgerCompareResult? _lastCompare;
    private bool _loading;
    /// <summary>What Load last put in the boxes, to tell an untouched box from one being typed in.</summary>
    private (string Key, string Redirect, string SyncFrom) _shown = ("", EtsyOAuth.DefaultRedirectUri, "");
    private bool _forceFields = true;
    /// <summary>The wait for Etsy's answer was ended on purpose (Cancel, or the address was pasted), not by the 10-minute limit.</summary>
    private bool _stoppedWaiting;

    private AppServices S => main.Services;

    /// <summary>How the shared secret is protected on this system (differs between Windows and macOS).</summary>
    public string SecretNote => S.SecretProtectionText + " It's never put in the database, backups or logs.";

    public void Load()
    {
        _loading = true;
        var s = S.Settings;
        // A box the user has typed in (and not saved yet) keeps what they typed when the rest of
        // the app reloads, e.g. when a background sync finishes.  forceFields is set after a save.
        var syncFrom = s.ApiSyncFrom.Length > 0 ? s.ApiSyncFrom : Input.Date(new DateOnly(DateTime.Today.Year, 1, 1));
        if (_forceFields || Keystring == _shown.Key) Keystring = s.EtsyKeystring;
        if (_forceFields) SharedSecret = "";
        if (_forceFields || RedirectUri == _shown.Redirect) RedirectUri = s.EtsyRedirectUri;
        if (_forceFields || SyncFrom == _shown.SyncFrom) SyncFrom = syncFrom;
        _shown = (s.EtsyKeystring, s.EtsyRedirectUri, syncFrom);
        _forceFields = false;
        SharedSecretHint = S.Etsy.HasSharedSecret ? "Saved (encrypted). Type here only to replace it." : "From your app's page on Etsy";
        UseApiLedger = s.UseApiLedger;
        IsConnected = S.Etsy.IsConnected;
        ConnectionText = IsConnected
            ? $"Connected to shop {s.EtsyShopId}." + (S.Etsy.RenewBy is DateTime r ? $"  Etsy asks you to connect again about every 90 days (next by {r:MMM d, yyyy})." : "")
            : S.Etsy.HasKeys ? "Not connected yet. Next: step 3, Connect."
            : s.EtsyKeystring.Length > 0 ? "Not set up yet. Next: step 2, paste the shared secret."
            : "Not set up. Start with step 1 below; you only do this once.";
        if (S.IsDemo) ConnectionText = "The Etsy API is switched off in the demo shop. Leave the demo to set it up for your own shop.";
        if (!_setupTouched) ShowSetup = !IsConnected;
        LastSyncText = s.LastApiSync is DateTime t ? $"Last synced {t:MMM d, yyyy h:mm tt}." : "Not synced yet.";
        var months = S.Db.CsvStatementMonths().OrderByDescending(m => m).ToList();
        var keep = CompareMonth;
        CompareMonths.Clear();
        foreach (var m in months) CompareMonths.Add(m);
        CompareMonth = keep is not null && months.Contains(keep) ? keep : months.FirstOrDefault();
        _loading = false;
    }

    /// <summary>Reload after saving here: the boxes show what was saved.</summary>
    private void Reload()
    {
        _forceFields = true;
        Load();
    }

    /// <summary>The setup steps are open until connected; after that the user's own choice sticks.</summary>
    [ObservableProperty] private bool _showSetup = true;
    private bool _setupTouched;
    partial void OnShowSetupChanged(bool value) { if (!_loading) _setupTouched = true; }

    [RelayCommand]
    private void OpenDeveloperPortal() => OpenUrl(DeveloperPortalUrl);

    [RelayCommand]
    private async Task CopyRedirectUri()
    {
        if (Dialogs.MainWindow?.Clipboard is { } clip)
        {
            await clip.SetTextAsync(RedirectUri);
            main.Status = "Callback URL copied. Paste it into your Etsy app's Callback URL box.";
        }
    }

    [RelayCommand]
    private void SaveKeys()
    {
        if (DemoBlocked()) return;
        if (string.IsNullOrWhiteSpace(Keystring)) { main.Status = "Enter the keystring."; return; }
        if (!Uri.TryCreate(RedirectUri.Trim(), UriKind.Absolute, out _)) { main.Status = "The callback URL isn't a valid address."; return; }
        S.Settings.EtsyRedirectUri = RedirectUri.Trim();
        S.Etsy.SaveKeys(Keystring, SharedSecret);
        S.SaveSettings();
        Reload();
        main.Status = S.Etsy.HasKeys ? $"Keys saved. {S.SecretProtectionText}" : "Keystring saved; the shared secret is still needed.";
    }

    [RelayCommand]
    private async Task Connect()
    {
        if (DemoBlocked()) return;
        if (Keystring.Trim() != S.Settings.EtsyKeystring || SharedSecret.Length > 0 || RedirectUri.Trim() != S.Settings.EtsyRedirectUri) SaveKeys();
        try { _pending = S.Etsy.Begin(); }
        catch (EtsyAuthException ex) { main.Status = ex.Message; return; }

        WaitingForBrowser = true;
        PastedAddress = "";
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _stoppedWaiting = false;
        var ct = cts.Token;
        OpenUrl(_pending.AuthorizeUrl);

        if (!LoopbackListener.CanListen(_pending.RedirectUri))
        {
            main.Status = "Approve on Etsy, then copy the address your browser ends up on and paste it below.";
            return;
        }
        main.Status = "Waiting for you to approve on Etsy in your browser…";
        string url;
        try { url = await LoopbackListener.WaitForCallbackAsync(_pending.RedirectUri, ct); }
        catch (OperationCanceledException)
        {
            // Cancelled or finished by pasting: nothing to say.  Timed out: say so, and leave the paste box open.
            if (ReferenceEquals(cts, _connectCts) && !_stoppedWaiting && _pending is not null && WaitingForBrowser)
                main.Status = "Stopped waiting for Etsy after 10 minutes. If you did approve, paste the address from your browser below; otherwise click Connect again.";
            return;
        }
        catch (Exception ex)
        {
            Log.Write("Loopback listener failed: " + ex.Message);
            main.Status = "The app couldn't listen for Etsy's answer. After approving, paste the address from your browser below.";
            return;
        }
        await Finish(url);
    }

    [RelayCommand]
    private async Task FinishWithPastedAddress()
    {
        if (string.IsNullOrWhiteSpace(PastedAddress)) { main.Status = "Paste the whole address from your browser's address bar."; return; }
        _stoppedWaiting = true;
        _connectCts?.Cancel();
        await Finish(PastedAddress);
    }

    private async Task Finish(string callbackUrl)
    {
        if (_pending is null) return;
        IsBusy = true;
        ProgressText = "Finishing the connection…";
        try
        {
            await S.Etsy.CompleteAsync(_pending, callbackUrl);
            S.SaveSettings();
            WaitingForBrowser = false;
            _pending = null;
            Reload();
            main.Status = $"Connected to Etsy shop {S.Settings.EtsyShopId}.  Click Sync now to pull your data.";
        }
        catch (Exception ex)
        {
            Log.Write("Connect failed: " + Describe(ex));
            main.Status = Expected(ex) ? ex.Message : "Connecting didn't finish: " + ex.Message;
        }
        finally { IsBusy = false; ProgressText = ""; }
    }

    [RelayCommand]
    private void CancelConnect()
    {
        _stoppedWaiting = true;
        _connectCts?.Cancel();
        _pending = null;
        WaitingForBrowser = false;
        main.Status = "Connection cancelled.";
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        if (!await Dialogs.Confirm("Disconnect from Etsy",
                "Forget this computer's connection to your shop?\n\nData already synced stays. Your app keys stay too, so connecting again is one click.",
                "Disconnect")) return;
        S.Etsy.Forget();
        S.SaveSettings();
        Reload();
        main.Status = "Disconnected from Etsy.";
    }

    [RelayCommand]
    private async Task RemoveKeys()
    {
        if (!await Dialogs.Confirm("Remove app keys", "Remove the keystring, shared secret and connection from this computer?", "Remove")) return;
        S.Etsy.RemoveKeys();
        S.SaveSettings();
        Reload();
        main.Status = "Etsy app keys removed.";
    }

    [RelayCommand]
    private async Task SyncNow() => await RunSync(automatic: false);

    /// <summary>On start: Etsy's terms want listings no more than 6 hours old, other data no more than 24.</summary>
    public async Task AutoSyncIfStale()
    {
        if (!S.Etsy.IsConnected) return;
        if (S.Settings.LastApiSync is DateTime t && DateTime.Now - t < TimeSpan.FromHours(6)) return;
        await RunSync(automatic: true);
    }

    private async Task RunSync(bool automatic)
    {
        if (S.IsDemo) return;
        if (IsBusy) return;
        if (!SaveSyncFrom()) return;
        IsBusy = true;
        var progress = new Progress<string>(p => ProgressText = p);
        try
        {
            var sync = new EtsySync(S.Etsy.CreateClient(), S.Db, S.Settings);
            // Off the screen's thread: saving a big first sync to the database would otherwise freeze the window.
            var result = await Task.Run(() => sync.SyncAsync(progress));
            S.SaveSettings();
            // The API has no monthly statements; the Monthly P&L uses statement CSVs unless the ledger is switched on.
            var pnlNote = !S.Settings.UseApiLedger && S.Db.CsvStatementMonths().Count == 0
                ? "  The Monthly P&L still needs your monthly statement CSVs (drop them on Home), or Ledger compare under Advanced."
                : "";
            await main.RefreshAsync((automatic ? "Synced with Etsy: " : "Sync finished: ") + result.Summary + "." + pnlNote);
        }
        catch (Exception ex)
        {
            // Anything at all, including an answer from Etsy in a shape the app didn't expect:
            // the sync stops, what was already saved stays, and the app carries on.
            try { S.SaveSettings(); }   // keeps any renewed token state and watermarks reached
            catch (Exception save) { Log.Write("Couldn't save settings after a failed sync: " + save.Message); }
            Log.Write("Sync failed: " + Describe(ex));
            // What was saved before the failure (orders, listings…) is real data: show it.
            await main.RefreshAsync("Sync didn't finish: " + ex.Message + (Expected(ex) ? "" : "  (Details are in the log.)") +
                "  Anything it pulled before that is saved and shown.");
        }
        finally { IsBusy = false; ProgressText = ""; Load(); }
    }

    private bool SaveSyncFrom()
    {
        if (!DateOnly.TryParseExact(SyncFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            main.Status = $"\"{SyncFrom}\" isn't a date (use 2026-01-01).";
            return false;
        }
        var text = Input.Date(d);
        if (text != S.Settings.ApiSyncFrom)
        {
            // Reaching further back means asking for every receipt again.
            if (S.Settings.ApiSyncFrom.Length == 0 || string.CompareOrdinal(text, S.Settings.ApiSyncFrom) < 0)
            {
                S.Settings.ReceiptsModifiedSince = 0;
                S.Settings.LastApiSync = null;
            }
            S.Settings.ApiSyncFrom = text;
            S.SaveSettings();
        }
        return true;
    }

    partial void OnUseApiLedgerChanged(bool value)
    {
        if (_loading || value == S.Settings.UseApiLedger) return;
        S.Settings.UseApiLedger = value;
        S.SaveSettings();
        var months = value ? S.Db.RebuildApiStatementMonths() : [];
        if (!value) S.Db.RemoveApiStatementLines();
        _ = main.RefreshAsync(value
            ? months.Count > 0 ? $"Months filled from the API ledger: {string.Join(", ", months)}." : "On. Months without a statement CSV will be filled from the API after the next sync."
            : "Off. Only statement CSVs are used for the P&L.");
    }

    /// <summary>Saves the "sync from" date on its own (it is also saved whenever a sync starts).</summary>
    [RelayCommand]
    private void SaveSyncSettings()
    {
        if (DemoBlocked()) return;
        if (SaveSyncFrom()) { _shown = _shown with { SyncFrom = S.Settings.ApiSyncFrom }; SyncFrom = S.Settings.ApiSyncFrom; main.Status = $"Sync will reach back to {S.Settings.ApiSyncFrom}."; }
    }

    [RelayCommand]
    private async Task RunCompare()
    {
        if (DemoBlocked()) return;
        if (CompareMonth is not { } month) { main.Status = "Import a statement CSV first, then compare that month."; return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var client = S.Etsy.CreateClient();
            var shop = await EtsySync.LookUpShopAsync(client, S.Settings);
            var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            await new EtsySync(client, S.Db, S.Settings).SyncLedgerMonthAsync(shop, first, new Progress<string>(p => ProgressText = p));
            S.SaveSettings();

            var categorizer = new Categorizer(S.UserCategoryRules());
            var r = LedgerCompare.Compare(month, S.Db.LoadStatement(), S.Db.LoadLedger(month), categorizer);
            ShowCompare(r);
        }
        catch (Exception ex)
        {
            Log.Write("Ledger compare failed: " + Describe(ex));
            main.Status = "Ledger compare didn't finish: " + ex.Message;
        }
        finally { IsBusy = false; ProgressText = ""; }
    }

    private void ShowCompare(LedgerCompareResult r)
    {
        _lastCompare = r;
        CompareTypes.Clear();
        foreach (var t in r.Types) CompareTypes.Add(t);
        CompareUnmatched.Clear();
        foreach (var u in r.Unmatched.Where(u => u.Net != 0)) CompareUnmatched.Add(u);
        HasUnmatched = CompareUnmatched.Count > 0;
        CompareRan = true;
        ComparePassed = r.Passed;
        CanApplyRules = r.ProposedRules.Count > 0;
        CompareSummary = r.LedgerEntries == 0
            ? $"Etsy returned no ledger entries for {r.Month}."
            : $"{r.Month}: {r.Matched} of {r.LedgerEntries} API entries matched a statement line ({r.StatementLines} lines in the CSV).  " +
              $"Statement total {Money.Format(r.StatementTotal)}, API total {Money.Format(r.LedgerTotal)}.  " +
              (r.Passed ? "They agree: the API can stand in for months that have no statement CSV."
                        : "They don't fully agree yet; see the rows marked Unknown and the unmatched lines.");
        main.Status = CompareSummary;
    }

    [RelayCommand]
    private async Task ApplyRules()
    {
        if (_lastCompare is not { ProposedRules.Count: > 0 } r) return;
        var rules = CategoryRulesStore.Load(S.Paths);
        foreach (var p in r.ProposedRules)
        {
            rules.RemoveAll(x => x.Type == p.Type && x.TitleContains == p.TitleContains);
            rules.Insert(0, p);
        }
        CategoryRulesStore.Save(S.Paths, rules);
        if (S.Settings.UseApiLedger) S.Db.RebuildApiStatementMonths();
        CanApplyRules = false;
        await main.RefreshAsync($"Saved {r.ProposedRules.Count} API category rule(s) to categories.json.");
    }

    /// <summary>Errors with a message meant for the user; anything else is logged in full.</summary>
    private static bool Expected(Exception ex) => ex is EtsyAuthException or EtsyApiException or HttpRequestException or TaskCanceledException;
    private static string Describe(Exception ex) => Expected(ex) ? ex.Message : ex.ToString();

    private bool DemoBlocked()
    {
        if (!S.IsDemo) return false;
        main.Status = "The Etsy API is switched off in the demo shop.";
        return true;
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { main.Status = "Couldn't open the browser: " + ex.Message; }
    }
}
