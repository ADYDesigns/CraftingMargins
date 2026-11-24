// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CraftingMargins.App;
using CraftingMargins.App.ViewModels;
using CraftingMargins.App.Views;
using CraftingMargins.Data;

namespace CraftingMargins.Tests;

/// <summary>
/// Opens the real main window on the demo shop, with no display, and uses it the way a person
/// would: every tab is shown, things are edited, saved and reloaded.  These catch a screen
/// that no longer opens or a binding that broke, which the arithmetic tests can't.
///
/// Set CRAFTING_MARGINS_SCREENSHOTS to a folder to also save a picture of each tab.
/// </summary>
public sealed class UiSmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "CraftingMarginsTests", Guid.NewGuid().ToString("N"));
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>The real App (its styles and themes), drawn with Skia into memory.</summary>
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CraftingMargins.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private (MainWindow Window, MainViewModel Vm) OpenDemo()
    {
        var real = new AppServices(new FolderAppPaths(_dir));
        var vm = new MainViewModel(AppServices.CreateDemo(real));
        var window = new MainWindow { DataContext = vm, Width = 1320, Height = 860 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Shot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("CRAFTING_MARGINS_SCREENSHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) return;
        using var file = File.Create(Path.Combine(folder, name + ".png"));
#pragma warning disable CS0618   // the simple overload is all a test screenshot needs
        frame.Save(file);
#pragma warning restore CS0618
    }

    [Fact]
    public Task Every_tab_opens_on_the_demo_shop() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenDemo();
        Assert.DoesNotContain("went wrong", vm.Status);
        Assert.DoesNotContain("Unsaved", vm.Status);           // nothing is "unsaved" on a fresh start
        Assert.Equal(vm.Services.Settings.ProfitFloor, vm.Settings.ProfitFloor);
        Assert.NotEmpty(vm.Settings.FeeVersions);
        Assert.NotEmpty(vm.Orders.Orders);
        Assert.NotEmpty(vm.Pricing.Rows);
        Assert.NotEmpty(vm.Products.Listings);

        string[] tabs = ["Home", "Monthly", "Orders", "Reports", "Pricing", "Products", "Expenses", "Taxes", "Settings", "About"];
        for (int tab = 0; tab < tabs.Length; tab++)
        {
            vm.SelectedTab = tab;
            Dispatcher.UIThread.RunJobs();
            Shot(window, $"{tab:00} {tabs[tab]}");
        }
        // Sub-tabs that hold the grids changed in this version.
        vm.SelectedTab = MainViewModel.TabOrders; vm.Orders.SelectedSubTab = 1; Dispatcher.UIThread.RunJobs(); Shot(window, "02b Sales outside Etsy");
        vm.SelectedTab = MainViewModel.TabReports; vm.Reports.SelectedSubTab = 2; Dispatcher.UIThread.RunJobs(); Shot(window, "03b Shipping");
        vm.SelectedTab = MainViewModel.TabProducts;
        for (int sub = 1; sub <= 3; sub++) { vm.Products.SelectedSubTab = sub; Dispatcher.UIThread.RunJobs(); Shot(window, $"05{(char)('a' + sub)} Products sub {sub}"); }
        vm.SelectedTab = MainViewModel.TabSettings;
        for (int sub = 1; sub <= 2; sub++) { vm.SettingsTab = sub; Dispatcher.UIThread.RunJobs(); Shot(window, $"08{(char)('a' + sub)} Settings sub {sub}"); }
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task The_app_follows_a_region_that_writes_numbers_differently() => Session.Dispatch(async () =>
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var (window, vm) = OpenDemo();
            Assert.DoesNotContain("went wrong", vm.Status);
            Assert.Contains(",", vm.Home.YearProfit);                             // "$1.234,56": decimal comma on screen

            // What is typed is read the same way it is shown...
            Assert.True(Input.TryMoney("1.234,56", out var typed));
            Assert.Equal(1234.56m, typed);
            vm.SelectedTab = MainViewModel.TabOrders;
            vm.Orders.Selected = vm.Orders.Orders[0];
            vm.Orders.ExtraShipping = "4,50";
            await vm.Orders.SaveExtraShippingCommand.ExecuteAsync(null);
            // ...and what is stored doesn't depend on the region.
            Assert.Equal(4.50m, vm.Services.Db.LoadOrders().Single(o => o.OrderId == vm.Orders.Selected!.OrderId).ExtraShippingCost);
            Dispatcher.UIThread.RunJobs();
            Shot(window, "11 German region, Orders");
            vm.SelectedTab = MainViewModel.TabExpenses;
            Dispatcher.UIThread.RunJobs();
            Shot(window, "11b German region, Expenses");
            window.Close();
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }, CancellationToken.None);

    /// <summary>Every box, list and table on screen, with the name a screen reader would say for it.</summary>
    private static List<string> Unnamed(Window window, string where)
    {
        Dispatcher.UIThread.RunJobs();
        return [.. window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && Accessibility.NeedsName(c))
            .Where(c => c.TemplatedParent is null or DataGridCell or ContentPresenter)      // not the inner parts of another control
            .Where(c => string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(c)))
            .Select(c => $"{where}: {c.GetType().Name} {(c as TextBox)?.Text ?? c.Name}")];
    }

    [Fact]
    public Task Every_input_on_every_tab_has_a_name_a_screen_reader_can_say() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenDemo();
        var missing = new List<string>();
        var said = new List<string>();
        void Check(string where)
        {
            missing.AddRange(Unnamed(window, where));
            // With CRAFTING_MARGINS_SCREENSHOTS set, also write down every name, to read them over.
            said.AddRange(window.GetVisualDescendants().OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && Accessibility.NeedsName(c) && c.TemplatedParent is null or DataGridCell or ContentPresenter)
                .Select(c => $"{where,-22} {c.GetType().Name,-18} {Avalonia.Automation.AutomationProperties.GetName(c)}"));
        }

        for (int tab = 0; tab <= MainViewModel.TabAbout; tab++) { vm.SelectedTab = tab; Check($"tab {tab}"); }
        vm.SelectedTab = MainViewModel.TabOrders;
        vm.Orders.Selected = vm.Orders.Orders[0]; Check("orders, one picked");
        vm.Orders.SelectedSubTab = 1; Check("sales outside Etsy");
        vm.SelectedTab = MainViewModel.TabReports;
        for (int sub = 0; sub <= 4; sub++) { vm.Reports.SelectedSubTab = sub; Check($"reports {sub}"); }
        vm.SelectedTab = MainViewModel.TabPricing;
        for (int sub = 0; sub <= 2; sub++) { vm.Pricing.SelectedSubTab = sub; Check($"pricing {sub}"); }
        vm.SelectedTab = MainViewModel.TabProducts;
        for (int sub = 0; sub <= 3; sub++) { vm.Products.SelectedSubTab = sub; Check($"products {sub}"); }
        vm.SelectedTab = MainViewModel.TabSettings;
        for (int sub = 0; sub <= 2; sub++) { vm.SettingsTab = sub; Check($"settings {sub}"); }
        window.Close();

        var terms = new TermsWindow(new AppServices(new FolderAppPaths(Path.Combine(_dir, "terms"))));
        terms.Show();
        missing.AddRange(Unnamed(terms, "welcome"));
        terms.Close();

        if (Environment.GetEnvironmentVariable("CRAFTING_MARGINS_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "Screen reader names.txt"), said.Distinct());
        }
        Assert.True(missing.Count == 0, "No name for a screen reader:\n" + string.Join("\n", missing.Distinct()));
    }, CancellationToken.None);

    [Fact]
    public Task Renaming_an_item_sold_outside_etsy_takes_its_cost_rules_along() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var db = vm.Services.Db;
        vm.Orders.AddOtherSaleCommand.Execute(null);
        var row = vm.Orders.SelectedOtherSale!;
        row.Item = "Hand painted widgit";                       // a typo, to be fixed later
        row.Price = 12m;
        await vm.Orders.SaveOtherSalesCommand.ExecuteAsync(null);
        var oldId = CraftingMargins.Core.OtherItem.IdFor("Hand painted widgit");
        var rules = db.LoadRules();
        rules.Add(new CraftingMargins.Core.MappingRule { ListingId = oldId, Name = "Widget", FixedCost = 3m });
        db.SaveRules(rules);
        await vm.RefreshAsync();

        vm.Orders.OtherSales.Single(r => r.Item == "Hand painted widgit").Item = "Hand painted widget";
        await vm.Orders.SaveOtherSalesCommand.ExecuteAsync(null);

        var newId = CraftingMargins.Core.OtherItem.IdFor("Hand painted widget");
        Assert.Contains(db.LoadRules(), r => r.ListingId == newId && r.Name == "Widget");
        Assert.DoesNotContain(db.LoadRules(), r => r.ListingId == oldId);
        Assert.Contains("moved to the new name", vm.Status);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task The_same_retired_option_cannot_be_saved_twice() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var p = vm.Products;
        var before = vm.Services.Db.LoadAliases().Count;
        foreach (var newValue in new[] { "Charcoal", "Slate" })
        {
            p.AddAliasCommand.Execute(null);
            var a = p.SelectedAlias!;
            a.ListingId = "";
            a.VariationName = "Color";
            a.OldValue = newValue == "Charcoal" ? "Grey" : "grey ";     // the same old value, typed a little differently
            a.NewValue = newValue;
        }
        await p.SaveAliasesCommand.ExecuteAsync(null);
        Assert.Contains("listed twice", vm.Status);
        Assert.Equal(before, vm.Services.Db.LoadAliases().Count);       // nothing saved
        Assert.True(p.AliasesEdit.IsDirty);

        p.DeleteAliasCommand.Execute(null);                             // remove the second one
        await p.SaveAliasesCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, vm.Services.Db.LoadAliases().Count);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task The_table_focus_outline_is_a_setting_that_takes_effect_at_once_and_is_remembered() => Session.Dispatch(() =>
    {
        var (window, vm) = OpenDemo();
        try
        {
            Assert.True(new AppSettings().ShowTableFocusOutline);                 // on for a new user
            Assert.True(vm.Settings.ShowTableFocusOutline);
            vm.Settings.ShowTableFocusOutline = false;                             // untick it in Settings → Appearance
            vm.SelectedTab = MainViewModel.TabOrders;
            Dispatcher.UIThread.RunJobs();
            var grid = window.GetVisualDescendants().OfType<DataGrid>()
                .First(g => g.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(g) == "Orders");
            grid.Focus();
            grid.SelectedIndex = 3;
            grid.CurrentColumn = grid.Columns[1];
            Dispatcher.UIThread.RunJobs();

            bool OutlineShowing()
            {
                Dispatcher.UIThread.RunJobs();
                return grid.GetVisualDescendants().OfType<DataGridCell>()
                    .SelectMany(c => c.GetVisualDescendants().OfType<Grid>().Where(g => g.Name == "FocusVisual"))
                    .Any(g => g.IsEffectivelyVisible);
            }

            Assert.False(OutlineShowing());
            Shot(window, "12 Focus outline setting off");

            vm.Settings.ShowTableFocusOutline = true;                              // the tick box in Settings → Appearance
            Assert.True(OutlineShowing());                                         // the same window, no restart
            Assert.True(vm.Services.Settings.ShowTableFocusOutline);
            Assert.True(AppSettings.Load(vm.Services.Paths).ShowTableFocusOutline);    // saved straight away
            Shot(window, "13 Focus outline setting on");

            vm.Settings.ShowTableFocusOutline = false;
            Assert.False(OutlineShowing());
        }
        finally
        {
            AppTheme.ShowTableFocusOutline(Application.Current!, false);          // the app is shared by the other tests
            window.Close();
        }
    }, CancellationToken.None);


    [Fact]
    public Task The_welcome_screen_opens() => Session.Dispatch(() =>
    {
        var terms = new TermsWindow(new AppServices(new FolderAppPaths(_dir)));
        terms.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Crafting Margins", TermsWindow.PrivacyStatement);
        Assert.Contains(Path.Combine("Documents", "Crafting Margins"), TermsWindow.PrivacyStatement);
        Shot(terms, "10 Welcome");
        terms.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Unsaved_edits_survive_a_reload_and_another_grids_save() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var p = vm.Products;
        Assert.Empty(vm.UnsavedNames());
        Assert.DoesNotContain("Unsaved", vm.Status);

        // Edit a part and a material, and leave both unsaved.
        var part = p.Parts[0];
        var grams = part.Lines[0].Amount + 1.5m;        // change how much of its first material the part uses
        part.Lines[0].Amount = grams;
        var material = p.Materials[0];
        material.CostPerUnit += 0.001m;
        var price = material.CostPerUnit;
        Assert.True(p.PartsEdit.IsDirty);
        Assert.True(p.MaterialsEdit.IsDirty);

        // Something else reloads the app (a sync finishing, say): both edits are still there.
        await vm.RefreshAsync();
        Assert.Equal(grams, p.Parts[0].Lines[0].Amount);
        Assert.Equal(price, p.Materials[0].CostPerUnit);
        Assert.Contains("Unsaved changes kept", vm.Status);

        // Saving materials saves materials only; the part edit is still there and still unsaved.
        await p.SaveMaterialsCommand.ExecuteAsync(null);
        Assert.False(p.MaterialsEdit.IsDirty);
        Assert.Equal(price, vm.Services.Db.LoadMaterials().Single(m => m.Id == material.Model.Id).Prices.MaxBy(x => x.EffectiveFrom)!.CostPerUnit);
        Assert.True(p.PartsEdit.IsDirty);
        Assert.Equal(grams, p.Parts.Single(x => x.Model.Id == part.Model.Id).Lines[0].Amount);

        await p.SavePartsCommand.ExecuteAsync(null);
        Assert.Empty(vm.UnsavedNames());
        Assert.DoesNotContain("Unsaved", vm.Status);
        Assert.Equal(grams, vm.Services.Db.LoadParts().Single(x => x.Id == part.Model.Id).Versions.MaxBy(x => x.EffectiveFrom)!.Lines[0].Amount);

        // Typing a value and typing it back is not a change.
        var e = vm.Tax.Expenses[0];
        var amount = e.Amount;
        e.Amount = amount + 1;
        Assert.True(vm.Tax.ExpensesEdit.IsDirty);
        e.Amount = amount;
        Assert.False(vm.Tax.ExpensesEdit.IsDirty);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Rule_edits_are_kept_when_another_listing_is_clicked() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var p = vm.Products;
        var first = p.Listings.First(l => l.RuleCount > 0);
        var other = p.Listings.First(l => l != first);
        p.SelectedListing = first;
        Dispatcher.UIThread.RunJobs();
        p.Rules[0].Name = "Renamed in the test";
        Assert.True(p.RulesEdit.IsDirty);

        p.SelectedListing = other;                 // click another listing...
        Dispatcher.UIThread.RunJobs();
        Assert.True(p.RulesEdit.IsDirty);          // ...the edit is still pending
        await vm.RefreshAsync();                   // ...even across a reload
        p.SelectedListing = p.Listings.First(l => l.ListingId == first.ListingId);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Renamed in the test", p.Rules[0].Name);

        await p.SaveRulesCommand.ExecuteAsync(null);
        Assert.False(p.RulesEdit.IsDirty);
        Assert.Contains(vm.Services.Db.LoadRules(), r => r.ListingId == first.ListingId && r.Name == "Renamed in the test");
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Settings_refuse_fee_rates_that_add_up_to_100_percent() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var before = vm.Services.Db.LoadFeeRates().Count;
        vm.Settings.FeeVersions[0].OffsiteAdsPercent = 95m;
        await vm.Settings.SaveCommand.ExecuteAsync(null);
        Assert.Contains("100%", vm.Status);
        Assert.True(vm.Settings.Edit.IsDirty);                                   // nothing was saved
        Assert.All(vm.Services.Db.LoadFeeRates(), f => Assert.True(f.OffsiteAdsRate < 0.5m));
        Assert.Equal(before, vm.Services.Db.LoadFeeRates().Count);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task Renaming_an_expense_category_takes_its_expenses_along() => Session.Dispatch(async () =>
    {
        var (window, vm) = OpenDemo();
        var used = vm.Services.Db.LoadExpenses().GroupBy(x => x.Category).OrderByDescending(g => g.Count()).First();
        var row = vm.Tax.Categories.Single(c => c.Name == used.Key);
        row.Name = used.Key + " (renamed)";
        await vm.Tax.SaveCategoriesCommand.ExecuteAsync(null);
        var after = vm.Services.Db.LoadExpenses();
        Assert.Equal(used.Count(), after.Count(x => x.Category == used.Key + " (renamed)"));
        Assert.DoesNotContain(after, x => x.Category == used.Key);
        Assert.Contains(vm.Tax.Expenses, x => x.Category == used.Key + " (renamed)");
        window.Close();
    }, CancellationToken.None);
}
