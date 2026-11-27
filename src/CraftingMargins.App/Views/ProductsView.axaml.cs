// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.ComponentModel;
using Avalonia.Controls;
using CraftingMargins.App.ViewModels;

namespace CraftingMargins.App.Views;

/// <summary>The Products &amp; Costs tab: listings and their cost rules, parts, materials, retired options.</summary>
public partial class ProductsView : UserControl
{
    private ProductsViewModel? _products;

    public ProductsView()
    {
        InitializeComponent();
        // The two time columns of the Parts grid are only shown when the shop tracks that kind of
        // time (Settings → Shop settings), so a candle maker never sees "Machine minutes".
        DataContextChanged += (_, _) => Follow((DataContext as MainViewModel)?.Products);
    }

    private void Follow(ProductsViewModel? products)
    {
        if (_products is not null) _products.PropertyChanged -= Changed;
        _products = products;
        if (_products is not null) _products.PropertyChanged += Changed;
        ShowTimeColumns();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductsViewModel.TrackMachineTime) or nameof(ProductsViewModel.TrackOwnTime)) ShowTimeColumns();
        // A part picked from elsewhere (a new one, made from the pricing check, say) is scrolled into view.
        // Posted, so it runs once the Parts tab is showing and the grid has its rows.
        if (e.PropertyName == nameof(ProductsViewModel.SelectedPart) && _products?.SelectedPart is { } part)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_products?.SelectedPart, part)) PartsGrid.ScrollIntoView(part, null);
            }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void ShowTimeColumns()
    {
        if (_products is null) return;
        foreach (var column in PartsGrid.Columns)
        {
            if (column.Header as string == "Machine minutes") column.IsVisible = _products.TrackMachineTime;
            if (column.Header as string == "Your minutes") column.IsVisible = _products.TrackOwnTime;
        }
    }
}
