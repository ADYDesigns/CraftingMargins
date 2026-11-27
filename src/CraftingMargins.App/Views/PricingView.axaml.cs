// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CraftingMargins.App.Views;

/// <summary>The Pricing tab.  Its data comes from the main view model, like every tab.</summary>
public partial class PricingView : UserControl
{
    public PricingView()
    {
        InitializeComponent();
        // A right-click selects the row under the pointer first, so the menu acts on that row.
        PricingGrid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(PricingGrid).Properties.IsRightButtonPressed) return;
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is { DataContext: { } item })
                PricingGrid.SelectedItem = item;
        }, RoutingStrategies.Tunnel);
    }
}
