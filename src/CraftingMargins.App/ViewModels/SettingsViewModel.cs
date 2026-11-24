// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.App.ViewModels;

/// <summary>One dated version of the shop-wide cost settings.  Percent shown as 5 for 5%.</summary>
public sealed partial class CostVersionRow : ObservableObject
{
    [ObservableProperty] private string _effectiveFrom = "2000-01-01";
    [ObservableProperty] private decimal _machineHourRate;
    /// <summary>What an hour of the maker's own time is counted at ($0 = not counted as a cost).</summary>
    [ObservableProperty] private decimal _ownHourRate;
    [ObservableProperty] private decimal _wastePercent;
    [ObservableProperty] private decimal _packagingPerOrder;
    [ObservableProperty] private decimal _packagingPerUnit;
}

/// <summary>One dated version of the Etsy fee rates.  Percents shown as 6.5 for 6.5%.</summary>
public sealed partial class FeeVersionRow : ObservableObject
{
    [ObservableProperty] private string _effectiveFrom = "2000-01-01";
    [ObservableProperty] private decimal _transactionPercent = 6.5m;
    [ObservableProperty] private decimal _processingPercent = 3m;
    [ObservableProperty] private decimal _processingFixed = 0.25m;
    [ObservableProperty] private decimal _listingFee = 0.20m;
    [ObservableProperty] private decimal _offsiteAdsPercent = 15m;
    [ObservableProperty] private decimal _offsiteAdsCap = 100m;
}

/// <summary>A backup in the restore list, shown as "Backup from Nov 19, 2026, 9:00 PM".</summary>
public sealed record BackupItem(string File, string Text)
{
    public override string ToString() => Text;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel main;

    /// <summary>Unsaved changes to anything the "Save settings" button saves.</summary>
    public EditState Edit { get; }

    public SettingsViewModel(MainViewModel main)
    {
        this.main = main;
        Edit = new("settings", () => EditState.Lines(
        [
            EditState.Line(ShopName, ProfitFloor, BackupsToKeep, UseWeightsAtSaleDate, CheckForUpdates, IgnoredVariations, CraftIndex, TrackMachineTime, TrackOwnTime),
            .. CostVersions.Select(c => EditState.Line(c.EffectiveFrom, c.MachineHourRate, c.OwnHourRate, c.WastePercent, c.PackagingPerOrder, c.PackagingPerUnit)),
            "fees",
            .. FeeVersions.Select(f => EditState.Line(f.EffectiveFrom, f.TransactionPercent, f.ProcessingPercent, f.ProcessingFixed, f.ListingFee, f.OffsiteAdsPercent, f.OffsiteAdsCap)),
        ]));
        Edit.Watch(CostVersions);
        Edit.Watch(FeeVersions);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShopName) or nameof(ProfitFloor) or nameof(BackupsToKeep) or nameof(UseWeightsAtSaleDate) or nameof(CheckForUpdates) or nameof(IgnoredVariations) or nameof(CraftIndex) or nameof(TrackMachineTime) or nameof(TrackOwnTime))
                Edit.Check();
        };
    }

    [ObservableProperty] private string _shopName = "";
    [ObservableProperty] private decimal _profitFloor;
    [ObservableProperty] private int _backupsToKeep;
    [ObservableProperty] private bool _useWeightsAtSaleDate;
    [ObservableProperty] private bool _checkForUpdates;
    [ObservableProperty] private string _ignoredVariations = "";
    /// <summary>What the shop makes (index into <see cref="CraftNames"/>).</summary>
    [ObservableProperty] private int _craftIndex;
    public string[] CraftNames { get; } = [.. Crafts.All.Select(c => c.Name)];
    /// <summary>Products take machine time worth costing.</summary>
    [ObservableProperty] private bool _trackMachineTime;
    /// <summary>Track the maker's own time per part.</summary>
    [ObservableProperty] private bool _trackOwnTime;
    [ObservableProperty] private CostVersionRow? _selectedCost;
    [ObservableProperty] private FeeVersionRow? _selectedFee;
    [ObservableProperty] private BackupItem? _selectedBackup;

    // Appearance: applied and saved as soon as it changes.
    public string[] ThemeModes { get; } = [OperatingSystem.IsWindows() ? "Follow Windows" : OperatingSystem.IsMacOS() ? "Follow macOS" : "Follow the system", "Light", "Dark"];
    public string[] AccentColors { get; } = ["Blue", "Teal"];
    [ObservableProperty] private int _themeModeIndex;
    [ObservableProperty] private int _accentIndex;
    private bool _loadingAppearance;

    /// <summary>Show an outline around the table cell that has keyboard focus (applied and saved at once, like the rest of Appearance).</summary>
    [ObservableProperty] private bool _showTableFocusOutline;

    partial void OnThemeModeIndexChanged(int value) => ApplyAppearance();
    partial void OnAccentIndexChanged(int value) => ApplyAppearance();
    partial void OnShowTableFocusOutlineChanged(bool value)
    {
        if (_loadingAppearance) return;
        main.Services.Settings.ShowTableFocusOutline = value;
        main.Services.SaveSettings();
        if (Avalonia.Application.Current is { } app) AppTheme.ShowTableFocusOutline(app, value);
    }

    private void ApplyAppearance()
    {
        if (_loadingAppearance || ThemeModeIndex < 0 || AccentIndex < 0) return;
        var s = main.Services.Settings;
        s.ThemeMode = (ThemeMode)ThemeModeIndex;
        s.AccentColor = (AccentColor)AccentIndex;
        main.Services.SaveSettings();
        if (Avalonia.Application.Current is { } app) AppTheme.Apply(app, s.ThemeMode, s.AccentColor);
    }

    public ObservableCollection<CostVersionRow> CostVersions { get; } = [];
    public ObservableCollection<FeeVersionRow> FeeVersions { get; } = [];
    public ObservableCollection<BackupItem> BackupFiles { get; } = [];
    public string DataFolder => main.Services.Paths.DataFolder;

    public void Load()
    {
        var s = main.Services.Settings;
        _loadingAppearance = true;
        ThemeModeIndex = (int)s.ThemeMode;
        AccentIndex = (int)s.AccentColor;
        ShowTableFocusOutline = s.ShowTableFocusOutline;
        _loadingAppearance = false;
        LoadBackups();

        Edit.Check();
        if (Edit.IsDirty) return;   // keep what's being typed; Save settings (or closing) settles it
        using var loading = Edit.Loading();
        ShopName = s.ShopName;
        ProfitFloor = s.ProfitFloor;
        BackupsToKeep = s.BackupsToKeep;
        UseWeightsAtSaleDate = s.UseWeightsAtSaleDate;
        CheckForUpdates = s.CheckForUpdates;
        IgnoredVariations = string.Join(", ", s.IgnoredVariationNames);
        CraftIndex = Array.FindIndex(Crafts.All, c => c.Craft == s.Craft);
        TrackMachineTime = s.TrackMachineTime;
        TrackOwnTime = s.TrackOwnTime;

        CostVersions.Clear();
        foreach (var c in main.Services.Db.LoadCostSettings().OrderBy(c => c.EffectiveFrom))
            CostVersions.Add(new CostVersionRow
            {
                EffectiveFrom = c.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), MachineHourRate = c.MachineHourRate, OwnHourRate = c.OwnHourRate,
                WastePercent = c.WastePercent * 100, PackagingPerOrder = c.PackagingPerOrder, PackagingPerUnit = c.PackagingPerUnit,
            });
        FeeVersions.Clear();
        foreach (var f in main.Services.Db.LoadFeeRates().OrderBy(f => f.EffectiveFrom))
            FeeVersions.Add(new FeeVersionRow
            {
                EffectiveFrom = f.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), TransactionPercent = f.TransactionRate * 100,
                ProcessingPercent = f.ProcessingRate * 100, ProcessingFixed = f.ProcessingFixed, ListingFee = f.ListingFee,
                OffsiteAdsPercent = f.OffsiteAdsRate * 100, OffsiteAdsCap = f.OffsiteAdsCap,
            });
    }

    // ----- Data folder -----
    public bool DataFolderLocked => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(App.DataFolderVariable));
    public bool DataFolderChangeable => !DataFolderLocked && !main.Services.IsDemo;
    public bool DataFolderIsCustom => DataLocation.ForCurrentUser().Read() is not null && !DataFolderLocked;
    private static string DefaultDataFolder => new DocumentsAppPaths().DataFolder;

    [RelayCommand]
    private async Task MoveDataFolder()
    {
        var picked = await Dialogs.PickFolder("Where should your Crafting Margins data live?", Path.GetDirectoryName(DataFolder));
        if (picked is null) return;
        // An empty folder is used as it is; otherwise the data goes in a "Crafting Margins" folder inside it.
        var target = DataLocation.HasData(picked) || !Directory.EnumerateFileSystemEntries(picked).Any() ? picked : Path.Combine(picked, "Crafting Margins");
        await SwitchTo(target);
    }

    [RelayCommand]
    private async Task UseDefaultDataFolder() => await SwitchTo(DefaultDataFolder);

    private async Task SwitchTo(string target)
    {
        var current = DataFolder;
        if (SameFolder(target, current)) { main.Status = "Your data is already in that folder."; return; }
        if (!await main.ConfirmDiscardEdits()) return;

        var existing = DataLocation.HasData(target);
        var message = existing
            ? $"{target} already has Crafting Margins data.\n\nSwitch to that data? Your data in {current} stays exactly where it is."
            : $"Copy your data to {target} and use it from there from now on?\n\nThe copy in {current} is left exactly as it is, so nothing can be lost. Delete it yourself once you're happy.";
        if (!await Dialogs.Confirm("Data folder", message, existing ? "Switch" : "Copy and switch")) return;
        try
        {
            if (!existing)
            {
                Backups.MakeBackup(main.Services.Db, main.Services.BackupFolder, main.Services.Settings.BackupsToKeep, paths: main.Services.Paths);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var n = DataLocation.Copy(current, target);
                if (!DataLocation.HasData(target)) { main.Status = "The copy didn't work, so nothing was changed."; return; }
                Log.Write($"Data folder copied ({n} files) to {target}.");
            }
            // Open the new folder first; only once that has worked is it remembered as the folder to use.
            App.Instance?.SwitchDataFolder(target, existing
                ? $"Now using the data in {target}. Your other data is still in {current}."
                : $"Your data is now in {target}. The old copy in {current} was left as it was; delete it when you're happy.");
            DataLocation.ForCurrentUser().Write(SameFolder(target, DefaultDataFolder) ? null : target);
            Log.Write($"Data folder is now {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or DatabaseTooNewException)
        {
            Log.Write("Data folder switch failed: " + ex);
            main.Status = "Couldn't use that folder: " + ex.Message;
        }
    }

    /// <summary>The same folder, however it was typed (trailing slash; upper or lower case where the system ignores case).</summary>
    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    [ObservableProperty] private string _backupFolderText = "";
    [ObservableProperty] private bool _backupFolderIsCustom;

    [RelayCommand]
    private async Task ChooseBackupFolder()
    {
        var folder = await Dialogs.PickFolder("Where should backups go?", main.Services.BackupFolder);
        if (folder is null) return;
        if (SameFolder(folder, main.Services.Paths.DataFolder)) { main.Status = "Pick a different folder from the data folder itself."; return; }
        main.Services.Settings.BackupFolder = folder;
        main.Services.SaveSettings();
        var made = TryBackup();   // so the new folder has a backup straight away
        LoadBackups();
        main.NotifyBackupFolderChanged();
        main.Status = made is not null ? $"Backups now go to {folder}. One was made just now." : $"Backups now go to {folder}, but one couldn't be made there just now (see the log).";
    }

    [RelayCommand]
    private void DefaultBackupFolder()
    {
        main.Services.Settings.BackupFolder = "";
        main.Services.SaveSettings();
        LoadBackups();
        main.NotifyBackupFolderChanged();
        main.Status = "Backups go to the Backups folder inside the data folder again. Backups already in the other folder were left there.";
    }

    /// <summary>Makes a backup now; null (and a log line) if it couldn't be written.</summary>
    private string? TryBackup()
    {
        try
        {
            var s = main.Services;
            var file = Backups.MakeBackup(s.Db, s.BackupFolder, s.Settings.BackupsToKeep, paths: s.Paths);
            if (file is not null) { s.Settings.LastBackupState = Backups.StateOf(s.Db, s.Paths); s.SaveSettings(); }
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Write("Backup failed: " + ex.Message);
            return null;
        }
    }

    private void LoadBackups()
    {
        BackupFolderText = main.Services.BackupFolder;
        BackupFolderIsCustom = !string.IsNullOrWhiteSpace(main.Services.Settings.BackupFolder);
        BackupFiles.Clear();
        foreach (var b in Backups.List(main.Services.BackupFolder)) BackupFiles.Add(new BackupItem(b, Backups.Describe(b)));
    }

    private static string Today => Input.Date(DateOnly.FromDateTime(DateTime.Today));

    [RelayCommand]
    private void AddCostVersion()
    {
        var last = CostVersions.LastOrDefault() ?? new CostVersionRow();
        CostVersions.Add(new CostVersionRow
        {
            EffectiveFrom = Today, MachineHourRate = last.MachineHourRate, OwnHourRate = last.OwnHourRate, WastePercent = last.WastePercent,
            PackagingPerOrder = last.PackagingPerOrder, PackagingPerUnit = last.PackagingPerUnit,
        });
    }

    [RelayCommand]
    private void DeleteCostVersion()
    {
        if (SelectedCost is { } c && CostVersions.Count > 1) CostVersions.Remove(c);
    }

    [RelayCommand]
    private void AddFeeVersion()
    {
        var last = FeeVersions.LastOrDefault() ?? new FeeVersionRow();
        FeeVersions.Add(new FeeVersionRow
        {
            EffectiveFrom = Today, TransactionPercent = last.TransactionPercent, ProcessingPercent = last.ProcessingPercent,
            ProcessingFixed = last.ProcessingFixed, ListingFee = last.ListingFee, OffsiteAdsPercent = last.OffsiteAdsPercent, OffsiteAdsCap = last.OffsiteAdsCap,
        });
    }

    [RelayCommand]
    private void DeleteFeeVersion()
    {
        if (SelectedFee is { } f && FeeVersions.Count > 1) FeeVersions.Remove(f);
    }

    [RelayCommand]
    private async Task Save()
    {
        // Every number is checked before anything is saved: a negative cost or a fee above 100%
        // would quietly make every report wrong.
        if (ProfitFloor < 0) { main.Status = "The profit goal can't be negative."; return; }
        foreach (var c in CostVersions)
        {
            if (c.MachineHourRate < 0 || c.OwnHourRate < 0 || c.PackagingPerOrder < 0 || c.PackagingPerUnit < 0) { main.Status = "Costs can't be negative."; SelectedCost = c; return; }
            if (c.WastePercent is < 0 or > 100) { main.Status = "The waste allowance must be between 0 and 100 percent."; SelectedCost = c; return; }
        }
        foreach (var f in FeeVersions)
        {
            if (f.TransactionPercent < 0 || f.ProcessingPercent < 0 || f.OffsiteAdsPercent < 0 || f.ProcessingFixed < 0 || f.ListingFee < 0 || f.OffsiteAdsCap < 0)
            { main.Status = "Fees can't be negative."; SelectedFee = f; return; }
            if (f.TransactionPercent + f.ProcessingPercent + f.OffsiteAdsPercent >= 100)
            { main.Status = "The transaction, processing and Offsite Ads percentages add up to 100% or more. Enter them as percents, e.g. 6.5 for 6.5%."; SelectedFee = f; return; }
        }

        var costs = new List<CostSettings>();
        foreach (var c in CostVersions)
        {
            if (!TryDate(c.EffectiveFrom, out var d)) { main.Status = $"\"{c.EffectiveFrom}\" isn't a date (use 2026-10-31)."; return; }
            costs.Add(new CostSettings
            {
                EffectiveFrom = d, MachineHourRate = c.MachineHourRate, OwnHourRate = c.OwnHourRate, WastePercent = c.WastePercent / 100,
                PackagingPerOrder = c.PackagingPerOrder, PackagingPerUnit = c.PackagingPerUnit,
            });
        }
        var fees = new List<FeeRates>();
        foreach (var f in FeeVersions)
        {
            if (!TryDate(f.EffectiveFrom, out var d)) { main.Status = $"\"{f.EffectiveFrom}\" isn't a date (use 2026-10-31)."; return; }
            fees.Add(new FeeRates
            {
                EffectiveFrom = d, TransactionRate = f.TransactionPercent / 100, ProcessingRate = f.ProcessingPercent / 100,
                ProcessingFixed = f.ProcessingFixed, ListingFee = f.ListingFee, OffsiteAdsRate = f.OffsiteAdsPercent / 100, OffsiteAdsCap = f.OffsiteAdsCap,
            });
        }
        if (costs.GroupBy(c => c.EffectiveFrom).Any(g => g.Count() > 1) || fees.GroupBy(f => f.EffectiveFrom).Any(g => g.Count() > 1))
        {
            main.Status = "Two versions have the same start date.";
            return;
        }

        var s = main.Services.Settings;
        s.ShopName = ShopName.Trim();
        s.ProfitFloor = ProfitFloor;
        s.BackupsToKeep = Math.Clamp(BackupsToKeep, 0, AppSettings.MaxBackupsToKeep);   // 0 = backups off
        s.UseWeightsAtSaleDate = UseWeightsAtSaleDate;
        s.CheckForUpdates = CheckForUpdates;
        if (CraftIndex >= 0) s.Craft = Crafts.All[CraftIndex].Craft;
        s.TrackMachineTime = TrackMachineTime;
        s.TrackOwnTime = TrackOwnTime;
        s.IgnoredVariationNames = IgnoredVariations.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        main.Services.SaveSettings();
        main.Services.Db.SaveCostSettings(costs);
        main.Services.Db.SaveFeeRates(fees);
        Edit.MarkClean();
        await main.RefreshAsync("Settings saved.");
    }

    [RelayCommand]
    private void BackupNow()
    {
        var file = TryBackup();
        LoadBackups();
        main.Status = file is null ? "The backup couldn't be made (is the backup folder there? see the log)." : $"Backup saved: {Path.GetFileName(file)}";
    }

    [RelayCommand]
    private async Task RestoreBackup()
    {
        if (SelectedBackup is not { } b) { main.Status = "Pick a backup first."; return; }
        var unsaved = main.UnsavedNames();
        if (!await Dialogs.Confirm("Restore backup",
                $"Replace all current data with the {Lower(b.Text)}?\n\nA backup of the current data is made first." +
                (unsaved.Length > 0 ? $"\n\nYour unsaved changes ({unsaved}) will be lost." : ""), "Restore")) return;
        var s = main.Services;
        try
        {
            var settingsJson = Backups.Restore(s.Db, s.BackupFolder, b.File, s.Settings.BackupsToKeep, s.Paths);
            if (settingsJson is not null) s.Settings.TakeShopSettingsFrom(settingsJson);
            // The restored data is older than the last sync, so the next sync fetches everything again.
            s.Settings.ReceiptsModifiedSince = 0;
            s.Settings.LastApiSync = null;
            s.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or DatabaseTooNewException)
        {
            Log.Write("Restore failed: " + ex);
            await main.RefreshAsync("The backup couldn't be restored: " + ex.Message);
            return;
        }
        await main.RefreshAsync($"Restored the {Lower(b.Text)}.", discardEdits: true);
    }

    private static string Lower(string text) => text.Length > 0 ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo { FileName = DataFolder, UseShellExecute = true }); }
        catch (Exception ex) { main.Status = "Couldn't open the folder: " + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAllData()
    {
        // This empties the database and nothing else.  It makes no backup of its own (that would
        // keep a copy of what the user asked to delete) and it leaves existing backups alone, so
        // the message says plainly where those are and that they must be deleted by hand.
        var s = main.Services;
        var backups = Backups.List(s.BackupFolder).Count;
        var backupNote = backups > 0
            ? $"Your {backups} backup{(backups == 1 ? "" : "s")} will NOT be deleted, and may still hold this data. To remove it completely, delete the backup files yourself from:\n{s.BackupFolder}"
            : $"There are no backups in {s.BackupFolder}. If you keep backups anywhere else, they may still hold this data; delete those yourself.";
        if (!await Dialogs.Confirm("Delete all data",
                "Delete every imported order, statement line, listing, expense, part, material and rule from the database? This can't be undone from inside the app.\n\n" +
                backupNote + "\n\nYour settings, category rules and Etsy connection stay (remove the connection on the Etsy API tab).",
                "Delete everything")) return;
        try { await Task.Run(s.Db.DeleteAllData); }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            Log.Write("Delete all data failed: " + ex);
            await main.RefreshAsync("The data couldn't be deleted: " + ex.Message);
            return;
        }
        // So the next Etsy sync pulls everything again.
        s.Settings.ReceiptsModifiedSince = 0;
        s.Settings.DataEtsyShopId = null;   // the folder is empty again: any shop may use it
        s.Settings.LastApiSync = null;
        s.SaveSettings();
        Log.Write("All data deleted from the database at the user's request.");
        await main.RefreshAsync(backups > 0
            ? $"All data deleted from the database. {backups} backup(s) are still in {s.BackupFolder}; delete them yourself if you want the data gone completely."
            : "All data deleted from the database.", discardEdits: true);
    }

    private static bool TryDate(string text, out DateOnly d) => Input.TryDate(text, out d);
}
