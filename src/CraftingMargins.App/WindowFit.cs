// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;

namespace CraftingMargins.App;

/// <summary>Shrinks a window that is bigger than the screen it opens on (small laptop screens, high display scaling).</summary>
public static class WindowFit
{
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
