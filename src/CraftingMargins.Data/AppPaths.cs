// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftingMargins.Data;

/// <summary>Where the app keeps its files.  A Mac/Linux port only needs a different implementation.</summary>
public interface IAppPaths
{
    string DataFolder { get; }
    string DatabaseFile => Path.Combine(DataFolder, "Crafting Margins.db");
    string SettingsFile => Path.Combine(DataFolder, "settings.json");
    string CategoryRulesFile => Path.Combine(DataFolder, "categories.json");
    string BackupFolder => Path.Combine(DataFolder, "Backups");
    string LogFolder => Path.Combine(DataFolder, "Logs");
    string SecretsFolder => Path.Combine(DataFolder, "Secrets");
}

/// <summary>Documents\Crafting Margins.</summary>
public sealed class DocumentsAppPaths : IAppPaths
{
    public string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crafting Margins");
}

/// <summary>Any folder (used by tests and for a portable setup).</summary>
public sealed class FolderAppPaths(string folder) : IAppPaths
{
    public string DataFolder { get; } = folder;
}

public enum ThemeMode { System, Light, Dark }
public enum AccentColor { Blue, Teal }

/// <summary>Simple settings kept in settings.json.  Dated cost and fee settings live in the database.</summary>
public sealed class AppSettings
{
    public string ShopName { get; set; } = "";
    /// <summary>
    /// What the shop makes, chosen on the first-run screen.  It sets starting points only: the
    /// unit new materials start in, the names of the first expense categories, the examples in
    /// the hints, and whether machine time is tracked.  Everything it sets can be changed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Core.Craft Craft { get; set; } = Core.Craft.Printing3D;
    /// <summary>
    /// Products take machine time worth costing (a 3D printer, laser or kiln running).  On: parts
    /// have a "machine minutes" column, a part without any is flagged, and there are profit-per-hour
    /// figures.  Off for crafts with no machine time, so nothing nags about it.
    /// </summary>
    public bool TrackMachineTime { get; set; } = true;
    /// <summary>
    /// Track the maker's own hands-on time per part ("your minutes"), and optionally count it as
    /// a cost at an hourly rate.  Off by default.
    /// </summary>
    public bool TrackOwnTime { get; set; }
    /// <summary>Every sale must net at least this much in the worst case (Offsite Ads sale).</summary>
    public decimal ProfitFloor { get; set; } = 5m;
    /// <summary>How many of the newest backups to keep: 0 to <see cref="MaxBackupsToKeep"/>.  0 = no backup is made when the app closes.</summary>
    public int BackupsToKeep { get; set; } = 3;
    public const int MaxBackupsToKeep = 10;
    /// <summary>Backups go here instead of Backups\ in the data folder (e.g. a OneDrive or USB folder).  Blank = default.</summary>
    public string BackupFolder { get; set; } = "";
    /// <summary>What the data looked like at the last backup (see Backups.StateOf), so closing without changes doesn't make another.</summary>
    public string LastBackupState { get; set; } = "";
    public bool UseWeightsAtSaleDate { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Version of the first-run terms the user accepted (0 = not yet).</summary>
    public int AcceptedTermsVersion { get; set; }
    /// <summary>Free-text variations (personalization) dropped at import so they are never stored.</summary>
    public List<string> IgnoredVariationNames { get; set; } = ["Personalization"];
    /// <summary>Variation names the user said are real options, not personalization (stops the warning).</summary>
    public List<string> KeptVariationNames { get; set; } = [];
    public string LastImportFolder { get; set; } = "";
    /// <summary>Tax summary: count filament, hardware and packaging as used per sale, or as bought (expense log).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Core.SuppliesBasis SuppliesBasis { get; set; } = Core.SuppliesBasis.Used;
    public string LastExportFolder { get; set; } = "";
    /// <summary>Follow Windows, or always light / always dark.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AccentColor AccentColor { get; set; } = AccentColor.Blue;
    /// <summary>
    /// Show an outline around the table cell that has keyboard focus.  On by default (it helps
    /// anyone who works through tables with the keyboard); off gives the cleaner look.
    /// </summary>
    public bool ShowTableFocusOutline { get; set; } = true;
    /// <summary>The gross amount on each year's Etsy 1099-K, typed in by the user, keyed by year.</summary>
    public Dictionary<string, decimal> Form1099K { get; set; } = [];
    /// <summary>Suggestions the user hid on Home: key → what they were about.  They come back when that changes.</summary>
    public Dictionary<string, string> HiddenSuggestions { get; set; } = [];
    /// <summary>The getting-started checklist on Home was dismissed.</summary>
    public bool HideChecklist { get; set; }
    /// <summary>A newer release the user said not to mention again.</summary>
    public string SkippedVersion { get; set; } = "";

    // ----- Etsy API (Phase 2).  The shared secret and tokens are NOT here: they're DPAPI-encrypted in Secrets\. -----
    /// <summary>The Seller App's keystring (Etsy calls it the client ID; it isn't secret).</summary>
    public string EtsyKeystring { get; set; } = "";
    /// <summary>Must match a callback URL registered on the app at etsy.com/developers/your-apps, exactly.</summary>
    public string EtsyRedirectUri { get; set; } = Etsy.EtsyOAuth.DefaultRedirectUri;
    public long? EtsyShopId { get; set; }
    public long? EtsyUserId { get; set; }
    /// <summary>
    /// The Etsy shop whose data this folder holds, set the first time the API is used here and
    /// kept when the connection is forgotten.  A different shop is then refused, so two shops
    /// can't be mixed in one data folder.  Cleared by "Delete all data".
    /// </summary>
    public long? DataEtsyShopId { get; set; }
    /// <summary>First day the API sync reaches back to (yyyy-MM-dd).  Blank = January 1 of this year.</summary>
    public string ApiSyncFrom { get; set; } = "";
    public DateTime? LastApiSync { get; set; }
    /// <summary>Newest receipt update seen (epoch seconds), so the next sync only asks for changes.</summary>
    public long ReceiptsModifiedSince { get; set; }
    /// <summary>Fill months that have no statement CSV from the API ledger.</summary>
    public bool UseApiLedger { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static AppSettings Load(IAppPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Opts) ?? new();
                s.BackupsToKeep = Math.Clamp(s.BackupsToKeep, 0, MaxBackupsToKeep);   // earlier versions allowed up to 100
                return s;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"settings.json could not be read, using defaults: {ex.Message}");
            // Keep the unreadable file so nothing in it is lost when the defaults are saved over it.
            try { File.Copy(paths.SettingsFile, paths.SettingsFile + ".unreadable", overwrite: true); } catch { /* best effort */ }
        }
        return new AppSettings();
    }

    /// <summary>
    /// After restoring a backup: takes the settings that describe the shop (the ones that
    /// change what the numbers say) from the settings stored in that backup.  Settings about
    /// this computer (appearance, folders, the Etsy connection, accepted terms) are left alone.
    /// </summary>
    public bool TakeShopSettingsFrom(string json)
    {
        try
        {
            if (JsonSerializer.Deserialize<AppSettings>(json, Opts) is not { } old) return false;
            ShopName = old.ShopName;
            ProfitFloor = old.ProfitFloor;
            UseWeightsAtSaleDate = old.UseWeightsAtSaleDate;
            IgnoredVariationNames = old.IgnoredVariationNames;
            KeptVariationNames = old.KeptVariationNames;
            SuppliesBasis = old.SuppliesBasis;
            Form1099K = old.Form1099K;
            UseApiLedger = old.UseApiLedger;
            return true;
        }
        catch (JsonException ex)
        {
            Log.Write("The settings stored in the backup couldn't be read: " + ex.Message);
            return false;
        }
    }

    public void Save(IAppPaths paths)
    {
        Directory.CreateDirectory(paths.DataFolder);
        var tmp = paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, paths.SettingsFile, overwrite: true);
    }
}

/// <summary>Plain text log in Logs\, one file per day, kept 30 days.  Never log money values or buyer data.</summary>
public static class Log
{
    private static string? _folder;
    private static readonly object Gate = new();

    public static void Init(IAppPaths paths)
    {
        _folder = paths.LogFolder;
        Directory.CreateDirectory(_folder);
        foreach (var f in Directory.GetFiles(_folder, "*.log"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30))
                try { File.Delete(f); } catch { /* in use; next time */ }
    }

    public static void Write(string message)
    {
        if (_folder is null) return;
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(_folder, "Crafting Margins " + DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".log"),
                    $"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
