// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CraftingMargins.App.ViewModels;

namespace CraftingMargins.App.Views;

/// <summary>The Home tab: the drop zone for Etsy files, the figures at a glance, and what needs attention.</summary>
public partial class HomeView : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public HomeView()
    {
        InitializeComponent();
        BrowseButton.Click += async (_, _) => await PickFiles();
        GoPricingButton.Click += (_, _) => { if (Vm is { } vm) vm.SelectedTab = MainViewModel.TabPricing; };
        DropZone.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        DropZone.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files is null || Vm is null) return;
            await Vm.ImportFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
        });
    }

    /// <summary>Scrolls down to the Needs attention section (the red button in the window's header).</summary>
    public void ShowAttention() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AttentionSection.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>Asks for Etsy CSV files and imports them (this tab's Browse button and the header's Import button).</summary>
    public async Task PickFiles()
    {
        if (Vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        IStorageFolder? start = null;
        if (Directory.Exists(Vm.Services.Settings.LastImportFolder))
            start = await top.StorageProvider.TryGetFolderFromPathAsync(Vm.Services.Settings.LastImportFolder);
        start ??= await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Etsy CSV exports or your cost workbook",
            AllowMultiple = true,
            SuggestedStartLocation = start,
            FileTypeFilter = [new FilePickerFileType("Etsy CSV files and the cost workbook") { Patterns = ["*.csv", "*.xlsx"] }],
        });
        if (Vm is { } vm) await vm.ImportFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }
}
