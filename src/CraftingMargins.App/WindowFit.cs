// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;

namespace CraftingMargins.App;

/// <summary>Shrinks a window that is bigger than the screen it opens on (small laptop screens, high display scaling).</summary>
public static class WindowFit
{
    /// <summary>
    /// Before the window is shown: the size and place it was last closed with, and maximized if it
    /// was.  A place that isn't on any screen now (a monitor unplugged, another computer) is
    /// ignored and the window is centred instead.
    /// </summary>
    public static void Restore(Window w, Data.WindowPlacement? saved)
    {
        if (saved is null || saved.Width < 100 || saved.Height < 100) return;
        w.Width = Math.Max(w.MinWidth, saved.Width);
        w.Height = Math.Max(w.MinHeight, saved.Height);
        try
        {
            // The top-left corner, a little inside, must be on a screen's working area.
            var corner = new PixelPoint(saved.X + 40, saved.Y + 20);
            if (w.Screens.All.Any(s => s.WorkingArea.Contains(corner)))
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Position = new PixelPoint(saved.X, saved.Y);
            }
        }
        catch (Exception ex) { Data.Log.Write("Couldn't check the screens for the window's place: " + ex.Message); }
        if (saved.Maximized) w.WindowState = WindowState.Maximized;
    }

    public static void FitToScreen(Window w)
    {
        var screen = w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
        if (screen is null) return;
        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var maxW = screen.WorkingArea.Width / scale * 0.95;
        var maxH = screen.WorkingArea.Height / scale * 0.92;
        var changed = false;
        if (w.Width > maxW) { w.Width = Math.Max(w.MinWidth, maxW); changed = true; }
        if (w.Height > maxH) { w.Height = Math.Max(w.MinHeight, maxH); changed = true; }
        if (!changed) return;
        // Re-centre on the working area.
        var wa = screen.WorkingArea;
        w.Position = new PixelPoint(
            wa.X + (int)((wa.Width - w.Width * scale) / 2),
            wa.Y + (int)((wa.Height - w.Height * scale) / 2));
    }
}
