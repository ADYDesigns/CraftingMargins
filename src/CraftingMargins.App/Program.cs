// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using CraftingMargins.Data;

namespace CraftingMargins.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Input.UseRegionForNumbersOnly();

        // Errors on background threads can't be recovered from, but they can at least be written down.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal error: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write("Background task failed: " + e.Exception);
            e.SetObserved();
        };
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Write("Fatal error: " + ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
