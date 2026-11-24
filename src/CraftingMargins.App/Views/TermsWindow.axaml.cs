// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using Avalonia.Controls;
using CraftingMargins.Core;

namespace CraftingMargins.App.Views;

/// <summary>First-run screen: privacy, disclaimers, a few settings.  Closing without accepting quits.</summary>
public partial class TermsWindow : Window
{
    /// <summary>"Documents\Crafting Margins" on Windows, "Documents/Crafting Margins" on a Mac.</summary>
    public static string DefaultFolderText => Path.Combine("Documents", "Crafting Margins");

    private static string SecretsText =>
        OperatingSystem.IsWindows() ? "are encrypted for your Windows account"
        : OperatingSystem.IsMacOS() ? "are kept in your macOS keychain"
        : "are kept in memory only and never written to disk";

    public static string PrivacyStatement =>
        $"Everything you import is stored only in the app's data folder on this computer ({DefaultFolderText}, unless you move it in Settings). The app keeps order numbers, dates, items, " +
        "variations and money amounts. It never stores buyer names, user IDs, emails or addresses, even though Etsy's exports include them, " +
        "and you can remove personalization text too; data pulled through the Etsy API is treated the same way. Nothing is sent anywhere, except " +
        "read-only requests to Etsy for your own shop's data (only if you connect the Etsy API) and a check of GitHub for a newer version, which " +
        "you can turn off. Orders and ledger history are kept because the monthly and yearly reports need them. Your Etsy app's shared secret " +
        $"and connection {SecretsText}. Settings → Delete all data empties the database; it tells you where your backups are so you can delete those too, " +
        "and the Etsy connection is removed on the Etsy API tab.";

    /// <summary>The user chose to look at the demo shop before accepting.</summary>
    public bool DemoRequested { get; private set; }

    /// <summary>For the XAML designer only.</summary>
    public TermsWindow() { InitializeComponent(); }

    public TermsWindow(AppServices services)
    {
        InitializeComponent();
        Opened += (_, _) => WindowFit.FitToScreen(this);
        PrivacyText.Text = PrivacyStatement;
        FolderText.Text = $"Everything is kept in your {DefaultFolderText} folder. Buyer names, emails and addresses are never saved.";
        AttributionText.Text = AppServices.Attribution;
        ShopNameBox.Text = services.Settings.ShopName;
        CraftBox.ItemsSource = Crafts.All.Select(c => c.Name).ToList();
        CraftBox.SelectedIndex = Array.FindIndex(Crafts.All, c => c.Craft == services.Settings.Craft);

        QuitButton.Click += (_, _) => Close();
        DemoButton.Click += (_, _) => { DemoRequested = true; Close(); };
        AcceptButton.Click += (_, _) =>
        {
            var fees = new FeeRates();
            var list = new List<FeeRates> { fees };
            if (Ads12.IsChecked == true)
            {
                if (Ads12Date.SelectedDate is not { } picked)
                {
                    ErrorText.Text = "Pick the date the 12% Offsite Ads rate started (or choose 15%).";
                    return;
                }
                list.Add(fees with { EffectiveFrom = DateOnly.FromDateTime(picked.Date), OffsiteAdsRate = 0.12m });
            }
            services.Settings.ShopName = ShopNameBox.Text?.Trim() ?? "";
            services.Settings.ProfitFloor = FloorBox.Value ?? 5m;
            // The craft sets starting points: whether machine time is tracked, and the names of the first expense categories.
            var craft = CraftBox.SelectedIndex >= 0 ? Crafts.All[CraftBox.SelectedIndex].Craft : Craft.Other;
            services.Settings.Craft = craft;
            services.Settings.TrackMachineTime = Crafts.UsesMachineTime(craft);
            if (services.Db.LoadExpenses().Count == 0) services.Db.SaveExpenseCategories(ScheduleC.DefaultCategories(craft));
            services.Settings.AcceptedTermsVersion = AppServices.TermsVersion;
            services.SaveSettings();
            services.Db.SaveFeeRates(list);
            Close();
        };
    }
}
