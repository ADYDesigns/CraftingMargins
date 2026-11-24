// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.App.ViewModels;

/// <summary>The main window.  Holds one view model per tab and reloads them all after any change.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public AppServices Services { get; }
    public HomeViewModel Home { get; }
    public PnlViewModel Pnl { get; } = new();
    public OrdersViewModel Orders { get; }
    public AttentionViewModel Attention { get; }
    public ProductsViewModel Products { get; }
    public SettingsViewModel Settings { get; }
    public EtsyApiViewModel EtsyApi { get; }
    public ReportsViewModel Reports { get; }
    public PricingViewModel Pricing { get; }
    public TaxViewModel Tax { get; }

    // Tab order in MainWindow.axaml.
    public const int TabHome = 0, TabPnl = 1, TabOrders = 2, TabReports = 3, TabPricing = 4, TabProducts = 5,
        TabExpenses = 6, TabTaxes = 7, TabSettings = 8, TabAbout = 9;

    // Sections of the Settings tab.
    public const int SettingsShop = 0, SettingsProgram = 1, SettingsEtsyApi = 2;
    [ObservableProperty] private int _settingsTab;

    /// <summary>Opens Settings at one of its sections.</summary>
    public void GoToSettings(int section) { SettingsTab = section; SelectedTab = TabSettings; }

    [ObservableProperty] private ReleaseInfo? _newRelease;

    public string Attribution => AppServices.Attribution;
    public string Title => $"Crafting Margins {AppServices.Version}";

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string _status = "";

    public ShopData Data { get; private set; } = new();

    public MainViewModel(AppServices services)
    {
        Services = services;
        Dialogs.DemoMode = services.IsDemo;
        Orders = new OrdersViewModel(this);
        Attention = new AttentionViewModel(this);
        Products = new ProductsViewModel(this);
        Settings = new SettingsViewModel(this);
        EtsyApi = new EtsyApiViewModel(this);
        Reports = new ReportsViewModel(this);
        Pricing = new PricingViewModel(this);
        Tax = new TaxViewModel(this);
        HomeViewModel? home = null;
        Home = home = new HomeViewModel
        {
            Navigate = (tab, sub) =>
            {
                if (tab == TabProducts && sub >= 0) Products.SelectedSubTab = sub;
                SelectedTab = tab;
            },
            ShowSuggestion = s =>
            {
                switch (s.Page)
                {
                    case SuggestionPage.Pricing: Pricing.SelectedSubTab = 0; SelectedTab = TabPricing; break;
                    case SuggestionPage.ReportsAds: Reports.SelectedSubTab = 1; SelectedTab = TabReports; break;
                    case SuggestionPage.ReportsDiscounts: Reports.SelectedSubTab = 4; SelectedTab = TabReports; break;
                }
            },
            HideSuggestion = s =>
            {
                Services.Settings.HiddenSuggestions[s.Key] = s.Fingerprint;
                Services.SaveSettings();
            },
            HideChecklistAction = () =>
            {
                Services.Settings.HideChecklist = true;
                Services.SaveSettings();
                if (home is not null) home.ShowChecklist = false;
            },
        };
        Refresh();
        if (!services.IsDemo && App.Instance?.DataFolderMissing is { } missing)
            Status = $"Your data folder {missing} wasn't found (is the drive plugged in?), so the default folder is open. Settings → Data folder to fix it.";
    }

    partial void OnSelectedTabChanged(int value)
    {
        if (value == TabPricing) _ = Pricing.OnShown();
    }

    /// <summary>One request to GitHub on start (Settings can turn it off).  Never downloads anything.</summary>
    public async Task CheckForUpdate()
    {
        if (!Services.Settings.CheckForUpdates || Services.IsDemo) return;
        try
        {
            var r = await UpdateCheck.NewerReleaseAsync(AppServices.Http, AppServices.GitHubRepo, AppServices.Version);
            if (r is not null && r.Version != Services.Settings.SkippedVersion) NewRelease = r;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Write("Update check failed: " + ex.Message);
        }
    }

    /// <summary>The made-up demo shop is showing (banner, no Etsy API, no backups).</summary>
    public bool IsDemo => Services.IsDemo;

    /// <summary>For the About page.</summary>
    public string DataFolderText => Services.Paths.DataFolder;
    public string BackupFolderText => Services.BackupFolder;
    public bool BackupsElsewhere => !string.IsNullOrWhiteSpace(Services.Settings.BackupFolder);

    [RelayCommand] private void LeaveDemo() => App.Instance?.LeaveDemo();
    [RelayCommand] private async Task OpenDemo() { if (await ConfirmDiscardEdits()) App.Instance?.EnterDemo(); }

    public void NotifyBackupFolderChanged()
    {
        OnPropertyChanged(nameof(BackupFolderText));
        OnPropertyChanged(nameof(BackupsElsewhere));
    }

    // ----- Unsaved changes -----

    /// <summary>Every part of the app with its own Save button.</summary>
    public IEnumerable<EditState> Edits =>
        [Products.RulesEdit, Products.PartsEdit, Products.MaterialsEdit, Products.AliasesEdit, Orders.OtherSalesEdit, Tax.ExpensesEdit, Tax.CategoriesEdit, Settings.Edit];

    /// <summary>Names of the parts with unsaved changes, e.g. "cost rules, expenses".</summary>
    public string UnsavedNames()
    {
        foreach (var e in Edits) e.Check();
        return string.Join(", ", Edits.Where(e => e.IsDirty).Select(e => e.Name));
    }

    /// <summary>Before something that throws unsaved edits away: asks, unless there are none.</summary>
    public async Task<bool> ConfirmDiscardEdits()
    {
        var names = UnsavedNames();
        return names.Length == 0 ||
            await Dialogs.Confirm("Unsaved changes", $"You have unsaved changes to: {names}.\n\nCarry on and lose them?", "Lose the changes", "Go back");
    }

    [RelayCommand]
    private void OpenRelease()
    {
        if (NewRelease is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = NewRelease.Url, UseShellExecute = true }); }
        catch (Exception ex) { Status = "Couldn't open the browser: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenSupport()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AppServices.SupportUrl, UseShellExecute = true }); }
        catch (Exception ex) { Status = "Couldn't open the browser: " + ex.Message; }
    }

    [RelayCommand]
    private void SkipRelease()
    {
        if (NewRelease is null) return;
        Services.Settings.SkippedVersion = NewRelease.Version;
        Services.SaveSettings();
        NewRelease = null;
    }

    /// <summary>Everything a reload reads and works out.  Built off the screen's thread, then shown.</summary>
    private sealed record Snapshot(ShopData Data, List<MonthlyPnL> Months, List<OrderProfit> Orders, List<Listing> Listings,
        List<PriceRow> Pricing, List<Suggestion> Suggestions);

    private Snapshot BuildSnapshot()
    {
        var data = Services.LoadData();
        var months = PnLBuilder.Build(data);
        var orders = OrderReport.Build(data);
        var listings = Services.Db.LoadListings();
        var floor = Services.Settings.ProfitFloor;
        var pricing = PricingCheck.Build(data, listings, orders, new PricingOptions { Floor = floor });
        var suggestions = Suggestions.Build(data, orders, pricing, floor, DateOnly.FromDateTime(DateTime.Today));
        return new Snapshot(data, months, orders, listings, pricing, suggestions);
    }

    private void Show(Snapshot s)
    {
        Data = s.Data;
        Pnl.Load(s.Months);
        Orders.Load(s.Orders, Data);
        Attention.Load(Data, s.Months, s.Listings);
        Reports.Load(Data, s.Orders, s.Months);
        Pricing.Load(Data, s.Listings, s.Orders, s.Pricing);
        Tax.Load(s.Months, s.Orders, Data);
        Home.Load(Data, s.Months, Attention, Pricing.BelowFloorCount);
        Home.DropHint = Services.IsDemo ? "Demo shop: leave the demo to import your own Etsy files" : "Drop new Etsy CSV files here to bring your numbers up to date";
        Home.LoadSuggestions(s.Suggestions, Services.Settings.HiddenSuggestions);
        Home.LoadChecklist(Data, s.Listings.Count, Attention.Unmapped.Sum(u => u.Units), Services.Settings.HideChecklist);
        Products.Load(Data, s.Listings);
        Settings.Load();
        EtsyApi.Load();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DataFolderText));
        OnPropertyChanged(nameof(BackupFolderText));
        OnPropertyChanged(nameof(BackupsElsewhere));
        var last = Data.CostCutoff;
        Status = (last is null ? "No statements imported yet." : $"Statements loaded through {last:MMM d, yyyy}.  {Data.Orders.Count} orders.") +
            (Services.Settings.LastApiSync is DateTime synced ? $"  Last synced with Etsy {synced:MMM d, h:mm tt}." : "");
        Status += UnsavedNote();
    }

    private string UnsavedNote()
    {
        var unsaved = string.Join(", ", Edits.Where(e => e.IsDirty).Select(e => e.Name));
        return unsaved.Length > 0 ? $"  Unsaved changes kept: {unsaved}." : "";
    }

    /// <summary>The app is reading files or working numbers out in the background.</summary>
    [ObservableProperty] private bool _isWorking;

    /// <summary>Reloads everything from the database and rebuilds every report, waiting for it (used once, at start).</summary>
    public void Refresh()
    {
        try { Show(BuildSnapshot()); }
        catch (Exception ex)
        {
            Log.Write("Refresh failed: " + ex);
            Status = "Something went wrong loading the data: " + ex.Message;
        }
    }

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// Reloads everything from the database and rebuilds every report.  The reading and the
    /// arithmetic happen in the background so the window stays responsive.  Parts of a screen
    /// with unsaved changes keep them unless <paramref name="discardEdits"/> is set (after
    /// restoring a backup, say, when what's on screen no longer matches the data at all).
    /// </summary>
    /// <param name="status">Shown in the status line once the reload has finished.</param>
    public async Task RefreshAsync(string? status = null, bool discardEdits = false)
    {
        await _refreshGate.WaitAsync();
        IsWorking = true;
        try
        {
            var snapshot = await Task.Run(BuildSnapshot);
            if (discardEdits)
            {
                Products.ForgetPendingRules();
                foreach (var e in Edits) e.MarkClean();
            }
            Show(snapshot);
            if (status is not null) Status = status + UnsavedNote();
        }
        catch (Exception ex)
        {
            Log.Write("Refresh failed: " + ex);
            Status = "Something went wrong loading the data: " + ex.Message;
        }
        finally
        {
            IsWorking = false;
            _refreshGate.Release();
        }
    }

    private bool _importing;

    public async Task ImportFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (Services.IsDemo)
        {
            // Real files dropped into the throwaway demo would be lost when it resets.
            Status = "This is the demo shop. Leave the demo (button at the top) to import your own Etsy files.";
            return;
        }
        if (_importing) { Status = "Still importing the last files; drop these again when it's done."; return; }
        _importing = true;
        IsWorking = true;
        Status = paths.Count == 1 ? $"Importing {Path.GetFileName(paths[0])}…" : $"Importing {paths.Count} files…";
        try
        {
            var importer = new CsvImporter(Services.Db, Services.Settings, new Categorizer(Services.UserCategoryRules()));
            var results = await Task.Run(() => importer.ImportFiles(paths));
            Home.ShowImportResults(results);
            var folder = Path.GetDirectoryName(paths[0]);
            if (folder is not null) { Services.Settings.LastImportFolder = folder; Services.SaveSettings(); }
            await RefreshAsync();
            SelectedTab = TabHome;
        }
        catch (Exception ex)
        {
            Log.Write("Import failed: " + ex);
            Status = "The import didn't finish: " + ex.Message;
        }
        finally
        {
            _importing = false;
            IsWorking = false;
        }
    }

    public void GoToRulesFor(string listingId)
    {
        Products.SelectListing(listingId);
        Products.SelectedSubTab = 0;
        SelectedTab = TabProducts;
    }

    [RelayCommand]
    private void GoToTab(string index) => SelectedTab = int.Parse(index);

    [RelayCommand]
    private Task Reload() => RefreshAsync();
}
