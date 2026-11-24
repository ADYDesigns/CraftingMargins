// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia.Controls;
using CraftingMargins.Data;

namespace CraftingMargins.App.Views;

/// <summary>The About tab: version, who provides the app, the notices, and where the data is.</summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        // The schema number is the database layout this version reads and writes (Database.SchemaVersion).
        VersionText.Text = $"Version {AppServices.Version} · Schema {Database.SchemaVersion}";
        PrivacyText.Text = TermsWindow.PrivacyStatement;
    }
}
