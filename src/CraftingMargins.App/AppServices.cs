// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;
using CraftingMargins.Data;
using CraftingMargins.Data.Etsy;

namespace CraftingMargins.App;

/// <summary>Everything the screens share: data folder, database, settings.</summary>
public sealed class AppServices
{
    public const int TermsVersion = 1;

    public const string Attribution =
        "The term 'Etsy' is a trademark of Etsy, Inc. This Application uses Etsy's API, but is not endorsed or certified by Etsy.";

    /// <summary>Where questions and bug reports go (the About tab links to it).</summary>
    public const string SupportUrl = "https://github.com/ADYDesigns/CraftingMargins/issues";

    /// <summary>"owner/name" of the GitHub repository, for the update check.  Empty = no check is made.</summary>
    public const string GitHubRepo = "ADYDesigns/CraftingMargins";

    public IAppPaths Paths { get; }
    public Database Db { get; }
    public AppSettings Settings { get; }
    public ISecretStore Secrets { get; }
    public EtsyConnection Etsy { get; }

    /// <summary>One HttpClient for the app's lifetime (the Etsy API is the only thing it talks to).</summary>
    public static readonly HttpClient Http = CreateHttp();

    /// <summary>The made-up demo shop: its own folder, no Etsy API, no backups, no update check.</summary>
    public bool IsDemo { get; }

    public AppServices(IAppPaths paths, bool isDemo = false)
    {
        Paths = paths;
        IsDemo = isDemo;
        Directory.CreateDirectory(paths.DataFolder);
        if (!isDemo) Log.Init(paths);   // the demo keeps logging to the real log folder
        Db = new Database(paths.DatabaseFile);
        Settings = AppSettings.Load(paths);
        Secrets = isDemo ? new MemorySecretStore()
            : OperatingSystem.IsWindows() ? new DpapiSecretStore(paths.SecretsFolder)
            : OperatingSystem.IsMacOS() ? new MacKeychainSecretStore(Path.GetFullPath(paths.DataFolder))
            : new MemorySecretStore();
        Etsy = new EtsyConnection(Settings, Secrets, Http);
    }

    /// <summary>How the Etsy shared secret is protected on this system, for messages.</summary>
    public string SecretProtectionText => Secrets.Description;

    /// <summary>
    /// A fresh demo shop in Demo\ inside the data folder, rebuilt each time so its dates are
    /// current.  The real database is never opened for writing by it.
    /// </summary>
    public static AppServices CreateDemo(AppServices real)
    {
        var folder = Path.Combine(real.Paths.DataFolder, "Demo");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);   // only ever the app's own demo folder
        var demo = new AppServices(new FolderAppPaths(folder), isDemo: true);
        DemoShop.Build(demo.Db, demo.Settings, DateOnly.FromDateTime(DateTime.Today));
        demo.Settings.AcceptedTermsVersion = TermsVersion;
        demo.Settings.ThemeMode = real.Settings.ThemeMode;
        demo.Settings.AccentColor = real.Settings.AccentColor;
        demo.Settings.ShowTableFocusOutline = real.Settings.ShowTableFocusOutline;
        demo.SaveSettings();
        Log.Write("Opened the demo shop.");
        return demo;
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"CraftingMargins/{Version}");
        return h;
    }

    public List<CategoryRule> UserCategoryRules() => CategoryRulesStore.Load(Paths);

    public ShopData LoadData() => Db.LoadShopData(Settings, UserCategoryRules());

    public void SaveSettings() => Settings.Save(Paths);

    /// <summary>Where backups go: the user's chosen folder (e.g. OneDrive), or Backups\ in the data folder.</summary>
    public string BackupFolder =>
        string.IsNullOrWhiteSpace(Settings.BackupFolder) ? Paths.BackupFolder : Settings.BackupFolder;

    public static string Version =>
        typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
