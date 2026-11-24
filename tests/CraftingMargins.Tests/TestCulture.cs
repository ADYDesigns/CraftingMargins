// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CraftingMargins.Tests;

/// <summary>
/// The app shows numbers the way the computer's region writes them, and many tests compare
/// that text ("$1,234.56").  The tests run as a US PC so they give the same answer on any
/// developer's machine; tests about other regions switch culture themselves.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseUnitedStates()
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = us;
        CultureInfo.DefaultThreadCurrentUICulture = us;
        CultureInfo.CurrentCulture = us;
        CultureInfo.CurrentUICulture = us;
    }
}
