// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CraftingMargins.App.ViewModels;
using CraftingMargins.App.Views;
using CraftingMargins.Data;

namespace CraftingMargins.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Accessibility.Register();
        AppTheme.Apply(this, ThemeMode.System, AccentColor.Blue);   // replaced by the user's choice once settings load

        // Colours picked in code (good/bad, red for losses) follow Windows switching light/dark.
        ActualThemeVariantChanged += (_, _) => AppTheme.UpdateLiveBrushes(this);

        // Tables set their scroll bars to auto-hide inside their own template, which an app style
        // can't override.  Turn it off on every table so it's obvious when a table scrolls sideways.
        Avalonia.Controls.Primitives.TemplatedControl.TemplateAppliedEvent.AddClassHandler<Avalonia.Controls.DataGrid>((grid, e) =>
        {
            foreach (var name in new[] { "PART_HorizontalScrollbar", "PART_VerticalScrollbar" })
                if (e.NameScope.Find(name) is Avalonia.Controls.Primitives.ScrollBar bar)
                    bar.AllowAutoHide = false;
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Anything a screen didn't expect: write it down and tell the user, instead of the app vanishing.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                ReportError(e.Exception);
            };

            AppServices services;
            var paths = ResolveDataFolder();

            // One copy of the app per data folder (see InstanceLock for why).
            _lock = InstanceLock.TryTake(paths.DataFolder);
            if (_lock is null)
            {
                desktop.MainWindow = StartupErrorWindow("Crafting Margins is already open",
                    $"Another copy of Crafting Margins is already open on this data folder:\n{paths.DataFolder}\n\n" +
                    "Use that window instead (look on the taskbar or behind other windows). Two copies working on the same data would overwrite each other's changes.");
                base.OnFrameworkInitializationCompleted();
                return;
            }

            try { services = new AppServices(paths); }
            catch (Exception ex)
            {
                // Usually the database: locked by another program, on a drive that went away, or damaged.
                Log.Write("Couldn't start: " + ex);
                desktop.MainWindow = StartupErrorWindow("Crafting Margins couldn't open its data.",
                    $"Data folder:\n{paths.DataFolder}\n\nWhat went wrong:\n{ex.Message}\n\n" +
                    "Things to check: the folder's drive is connected, and a sync program (OneDrive, Dropbox) isn't " +
                    "holding the file. Nothing has been changed or deleted. Your backups are in the Backups folder inside the data folder.");
                base.OnFrameworkInitializationCompleted();
                return;
            }
            AppTheme.Apply(this, services.Settings.ThemeMode, services.Settings.AccentColor);
            AppTheme.ShowTableFocusOutline(this, services.Settings.ShowTableFocusOutline);
            Log.Write($"Crafting Margins {AppServices.Version} started.");
            if (DataFolderMissing is not null) Log.Write($"The chosen data folder {DataFolderMissing} wasn't found; using the default.");

            RealServices = services;
            if (services.Settings.AcceptedTermsVersion < AppServices.TermsVersion)
                ShowWelcome(desktop, show: false);
            else
                desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(services) };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private bool _errorShowing;

    /// <summary>An error no screen handled.  Logged, shown in the status line, and in one message box at a time.</summary>
    public async void ReportError(Exception ex)
    {
        try
        {
            Log.Write("Unexpected error: " + ex);
            if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow { DataContext: MainViewModel vm, IsVisible: true }) return;
            vm.Status = "Something went wrong: " + ex.Message;
            if (_errorShowing) return;
            _errorShowing = true;
            try
            {
                await Dialogs.Info("Something went wrong",
                    $"{ex.Message}\n\nWhat you were doing may not have finished. The details are in today's log file in:\n{vm.Services.Paths.LogFolder}\n\n" +
                    "If it keeps happening, close the app and open it again.");
            }
            finally { _errorShowing = false; }
        }
        catch (Exception inner) { Log.Write("Couldn't show the error: " + inner.Message); }
    }

    /// <summary>This copy's hold on its data folder.</summary>
    private InstanceLock? _lock;

    /// <summary>Shown instead of the main window when the app can't start: the data can't be opened, or it's already open.</summary>
    private static Avalonia.Controls.Window StartupErrorWindow(string heading, string message)
    {
        var close = new Avalonia.Controls.Button { Content = "Close", MinWidth = 90, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        var win = new Avalonia.Controls.Window
        {
            Title = "Crafting Margins couldn't start",
            Width = 560,
            SizeToContent = Avalonia.Controls.SizeToContent.Height,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = heading, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 16, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Avalonia.Controls.TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Text = message },
                    close,
                },
            },
        };
        close.Click += (_, _) => win.Close();
        return win;
    }

    /// <summary>The variable that points the app at another data folder (testing, or a portable setup).  It beats Settings.</summary>
    public const string DataFolderVariable = "CRAFTING_MARGINS_DATA";

    /// <summary>The folder chosen in Settings when it couldn't be found at start (e.g. an unplugged USB drive).</summary>
    public string? DataFolderMissing { get; private set; }

    /// <summary>
    /// Where the data lives: the CRAFTING_MARGINS_DATA variable, else the folder chosen in
    /// Settings (if it's there), else Documents\Crafting Margins.
    /// </summary>
    private IAppPaths ResolveDataFolder()
    {
        var variable = Environment.GetEnvironmentVariable(DataFolderVariable);
        if (!string.IsNullOrWhiteSpace(variable)) return new FolderAppPaths(variable);
        if (DataLocation.ForCurrentUser().Read() is { } chosen)
        {
            if (Directory.Exists(chosen)) return new FolderAppPaths(chosen);
            DataFolderMissing = chosen;
        }
        return new DocumentsAppPaths();
    }

    /// <summary>Settings → Data folder: carry on with the data in another folder, without restarting.</summary>
    public void SwitchDataFolder(string folder, string status)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main }) return;
        // Take the new folder before letting go of the old one, so a failure leaves things as they were.
        var newLock = InstanceLock.TryTake(folder)
            ?? throw new IOException("Another copy of Crafting Margins is already open on that folder. Close it first.");
        try { RealServices = new AppServices(new FolderAppPaths(folder)); }
        catch { newLock.Dispose(); throw; }
        _lock?.Dispose();
        _lock = newLock;
        DataFolderMissing = null;
        var vm = new MainViewModel(RealServices) { SelectedTab = MainViewModel.TabSettings, SettingsTab = MainViewModel.SettingsProgram };
        main.DataContext = vm;
        vm.Status = status;
    }

    /// <summary>The user's own shop (never the demo).</summary>
    public AppServices? RealServices { get; private set; }

    public static App? Instance => Current as App;

    /// <summary>
    /// First run: the accept screen comes first.  Accept opens the main window on the user's
    /// own data; "Explore a demo shop" opens it on the demo (the terms come back on leaving it).
    /// </summary>
    private void ShowWelcome(IClassicDesktopStyleApplicationLifetime desktop, bool show)
    {
        var services = RealServices!;
        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        var terms = new TermsWindow(services);
        terms.Closed += (_, _) =>
        {
            var accepted = services.Settings.AcceptedTermsVersion >= AppServices.TermsVersion;
            if (!accepted && !terms.DemoRequested) { desktop.Shutdown(); return; }
            var main = new MainWindow { DataContext = new MainViewModel(accepted ? services : AppServices.CreateDemo(services)) };
            desktop.MainWindow = main;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            main.Show();
        };
        desktop.MainWindow = terms;
        if (show) terms.Show();
    }

    /// <summary>Settings → Open the demo shop (or the welcome screen's button).</summary>
    public void EnterDemo()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main } || RealServices is null) return;
        main.DataContext = new MainViewModel(AppServices.CreateDemo(RealServices));
    }

    /// <summary>Back to the user's own shop.  Someone who hasn't accepted the terms yet goes back to the welcome screen.</summary>
    public void LeaveDemo()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main } desktop || RealServices is null) return;
        Log.Write("Left the demo shop.");
        if (RealServices.Settings.AcceptedTermsVersion < AppServices.TermsVersion)
        {
            ShowWelcome(desktop, show: true);
            main.Close();
            return;
        }
        main.DataContext = new MainViewModel(RealServices);
    }
}
