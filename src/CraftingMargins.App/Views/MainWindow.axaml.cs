// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CraftingMargins.App.ViewModels;
using CraftingMargins.Data;

namespace CraftingMargins.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private bool _closeConfirmed;

    /// <summary>Size and place the last time the window wasn't maximized or minimized, kept so they're saved even when it closes maximized.</summary>
    private (double Width, double Height, PixelPoint Position)? _normal;
    private bool _placed;

    public MainWindow()
    {
        InitializeComponent();

        // Open the way it was left: maximized, or at its last size and place (if that's still on a screen).
        DataContextChanged += (_, _) =>
        {
            if (_placed || Vm is not { } vm) return;
            _placed = true;
            var saved = vm.Services.Settings.Window;
            WindowFit.Restore(this, saved);
            // Opening maximized never shows the normal size, so carry the saved one over for next time.
            if (saved is not null) _normal = (saved.Width, saved.Height, new PixelPoint(saved.X, saved.Y));
        };
        PositionChanged += (_, _) => RememberNormal();
        PropertyChanged += (_, e) => { if (e.Property == ClientSizeProperty) RememberNormal(); };

        Opened += async (_, _) =>
        {
            if (WindowState == WindowState.Normal) WindowFit.FitToScreen(this);
            RememberNormal();
            // Etsy's terms: keep API data fresh (listings within 6 hours).
            if (Vm is { } vm)
            {
                _ = vm.CheckForUpdate();
                try { await vm.EtsyApi.AutoSyncIfStale(); }
                catch (Exception ex)
                {
                    // Never let a failed sync at start stop the app from opening (or from opening next time).
                    Log.Write("Automatic sync at start failed: " + ex);
                    vm.Status = "The automatic Etsy sync didn't finish: " + ex.Message;
                }
            }
        };
        // The Home tab owns the file picker and the Needs attention section (HomeView).
        ImportButton.Click += async (_, _) => await HomeTab.PickFiles();
        AttentionButton.Click += (_, _) => { if (Vm is { } vm) { vm.SelectedTab = MainViewModel.TabHome; HomeTab.ShowAttention(); } };

        Closing += async (_, e) =>
        {
            if (Vm is not { } vm) return;

            // Unsaved edits: ask once.  The close is held while the question is open, then repeated if the answer is yes.
            if (!_closeConfirmed && !e.IsProgrammatic && vm.UnsavedNames() is { Length: > 0 } names)
            {
                e.Cancel = true;
                if (await Dialogs.Confirm("Unsaved changes", $"You have unsaved changes to: {names}.\n\nClose and lose them?", "Close without saving", "Go back"))
                {
                    _closeConfirmed = true;
                    Close();
                }
                return;
            }

            try
            {
                vm.Services.Settings.Window = _normal is { } n
                    ? new WindowPlacement(WindowState == WindowState.Maximized, n.Width, n.Height, n.Position.X, n.Position.Y)
                    : null;
                vm.Services.SaveSettings();
            }
            catch (Exception ex) { Log.Write("Couldn't save the window's size: " + ex.Message); }

            // Back up when the app closes, but only if something changed since the last backup:
            // opening the app to look at a number must never push a good backup out of the set that's kept.
            try
            {
                if (!vm.Services.IsDemo)
                    Backups.MakeBackupIfChanged(vm.Services.Db, vm.Services.Paths, vm.Services.Settings, vm.Services.BackupFolder);
            }
            catch (Exception ex) { Log.Write("Backup on close failed: " + ex.Message); }
        };
    }

    private void RememberNormal()
    {
        if (WindowState == WindowState.Normal && IsVisible && ClientSize.Width > 0)
            _normal = (ClientSize.Width, ClientSize.Height, Position);
    }
}
