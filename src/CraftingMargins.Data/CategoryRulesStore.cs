// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using CraftingMargins.Core;

namespace CraftingMargins.Data;

/// <summary>
/// The user's own statement-category rules (categories.json in the data folder).
/// They are checked before the built-in rules, so new Etsy line types never need a new release.
/// </summary>
public static class CategoryRulesStore
{
    public static List<CategoryRule> Load(IAppPaths paths)
    {
        try
        {
            return File.Exists(paths.CategoryRulesFile) ? Categorizer.LoadRules(File.ReadAllText(paths.CategoryRulesFile)) : [];
        }
        catch (Exception ex)
        {
            Log.Write($"categories.json could not be read: {ex.Message}");
            return [];
        }
    }

    public static void Save(IAppPaths paths, IEnumerable<CategoryRule> rules)
    {
        Directory.CreateDirectory(paths.DataFolder);
        File.WriteAllText(paths.CategoryRulesFile, Categorizer.SaveRules(rules));
    }

    /// <summary>Adds a rule for an exact statement Type + Title.</summary>
    public static void Add(IAppPaths paths, string type, string title, string category)
    {
        var rules = Load(paths);
        rules.RemoveAll(r => r.Type == type && r.TitleContains == title);
        rules.Insert(0, new CategoryRule(type, title, category, "Added from Needs Attention"));
        Save(paths, rules);
    }
}
