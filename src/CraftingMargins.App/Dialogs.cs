// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace CraftingMargins.App;

/// <summary>Small Yes/No and OK message boxes.</summary>
public static class Dialogs
{
    public static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task<bool> Confirm(string title, string message, string yes = "Yes", string no = "Cancel")
    {
        var owner = MainWindow;
        if (owner is null) return false;
        bool result = false;
        var win = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        // Enter = the first button, Esc = the second (or the only one).
        var yesBtn = new Button { Content = yes, MinWidth = 90, IsDefault = true, IsCancel = no.Length == 0 };
        var noBtn = new Button { Content = no, MinWidth = 90, IsCancel = true, IsVisible = no.Length > 0 };
        yesBtn.Click += (_, _) => { result = true; win.Close(); };
        noBtn.Click += (_, _) => win.Close();
        win.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yesBtn, noBtn } },
            },
        };
        await win.ShowDialog(owner);
        return result;
    }

    /// <summary>A message with one OK button.</summary>
    public static Task Info(string title, string message) => Confirm(title, message, "OK", "");

    /// <summary>Asks for a folder.  Returns its local path, or null if cancelled.</summary>
    public static async Task<string?> PickFolder(string title, string? startFolder)
    {
        var owner = MainWindow;
        if (owner is null) return null;
        IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
            start = await owner.StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>In the demo shop, exported files are named "Demo …" so they're never mistaken for real ones.</summary>
    public static bool DemoMode { get; set; }

    /// <summary>Asks where to save a file.  Returns the local path, or null if cancelled.</summary>
    public static async Task<string?> SaveFile(string title, string suggestedName, string typeName, string extension, string? folder)
    {
        var owner = MainWindow;
        if (owner is null) return null;
        Avalonia.Platform.Storage.IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            start = await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
        start ??= await owner.StorageProvider.TryGetWellKnownFolderAsync(Avalonia.Platform.Storage.WellKnownFolder.Documents);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = DemoMode ? "Demo " + suggestedName : suggestedName,
            DefaultExtension = extension,
            SuggestedStartLocation = start,
            ShowOverwritePrompt = true,
            FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }],
        });
        return file?.TryGetLocalPath();
    }
}
