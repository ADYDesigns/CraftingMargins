// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace CraftingMargins.App;

/// <summary>
/// Gives every box, list and table a name a screen reader can say.  Without one, Narrator or
/// VoiceOver announces "edit" or "combo box" and nothing else.
///
/// Rather than typing a name on each of a few hundred controls (and forgetting the next one
/// that's added), the name is taken from what a sighted person reads next to the control:
/// the column heading for a control inside a table, the label above or beside it, or its
/// grey placeholder text.  A name written in the XAML (AutomationProperties.Name) always wins,
/// and is the fix for any control whose surroundings don't describe it.
/// A test opens every tab and fails if a control is left without a name.
/// </summary>
public static class Accessibility
{
    /// <summary>Called once at start: names each control of these kinds as it appears.</summary>
    public static void Register()
    {
        Control.LoadedEvent.AddClassHandler<TextBox>(Name);
        Control.LoadedEvent.AddClassHandler<ComboBox>(Name);
        Control.LoadedEvent.AddClassHandler<NumericUpDown>(Name);
        Control.LoadedEvent.AddClassHandler<CalendarDatePicker>(Name);
        Control.LoadedEvent.AddClassHandler<AutoCompleteBox>(Name);
        Control.LoadedEvent.AddClassHandler<CheckBox>(Name);
        Control.LoadedEvent.AddClassHandler<ListBox>(Name);
        Control.LoadedEvent.AddClassHandler<DataGrid>(Name);
        Control.LoadedEvent.AddClassHandler<Ellipse>(NameDot);
    }

    /// <summary>Kinds of control that must end up with a name (used by the test as well).</summary>
    public static bool NeedsName(Control c) =>
        c is TextBox or ComboBox or NumericUpDown or CalendarDatePicker or AutoCompleteBox or ListBox or DataGrid
        || c is CheckBox { Content: null or "" };

    private static void Name(Control c, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))) return;
        if (c is CheckBox { Content: string { Length: > 0 } }) return;     // its own text is its name
        if (NameFor(c) is { Length: > 0 } name) AutomationProperties.SetName(c, name);
    }

    /// <summary>
    /// The red/green status dots carry meaning by colour alone.  Each gets a spoken name and a
    /// tooltip from the text beside it, or "OK" / "Needs attention" from its colour.
    /// </summary>
    private static void NameDot(Ellipse dot, RoutedEventArgs e)
    {
        if (!dot.Classes.Contains("dot") || !string.IsNullOrWhiteSpace(AutomationProperties.GetName(dot))) return;
        var good = dot.Fill is Avalonia.Media.ISolidColorBrush b && AppTheme.LiveBrush("GoodBrush") is Avalonia.Media.ISolidColorBrush g && b.Color == g.Color;
        var text = good ? "OK" : "Needs attention";
        AutomationProperties.SetName(dot, text);
        if (ToolTip.GetTip(dot) is null) ToolTip.SetTip(dot, text);
    }

    /// <summary>The name for a control, worked out from its surroundings; null if nothing nearby describes it.</summary>
    public static string? NameFor(Control c)
    {
        if (AutomationProperties.GetName(c) is { Length: > 0 } own) return own;

        // The text box inside a number box, date picker or search box: the same name as the control it belongs to.
        if (c.TemplatedParent is Control owner && owner is NumericUpDown or CalendarDatePicker or AutoCompleteBox or ComboBox)
            return NameFor(owner);
        if (c.TemplatedParent is DataGridCell or DataGridRow) return ColumnHeader(c);

        // Inside a table: the column heading.
        if (ColumnHeader(c) is { Length: > 0 } header) return header;

        // A label above or beside it, looking outward a few levels (a box is often wrapped with a "$" or a button).
        Control at = c;
        for (int level = 0; level < 3 && at.GetVisualParent() is Panel panel; level++)
        {
            if (LabelBefore(panel, at) is { Length: > 0 } label) return label;
            at = panel;
        }

        // Its own grey hint text.
        var placeholder = c switch
        {
            TextBox t => t.PlaceholderText,
            ComboBox cb => cb.PlaceholderText,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(placeholder)) return placeholder;

        // A table or list with a heading somewhere above it in the same card.
        for (Control? up = c.GetVisualParent() as Control; up is not null && up is not Window; up = up.GetVisualParent() as Control)
            if (up is Panel p && LabelBefore(p, c.GetVisualAncestors().OfType<Control>().Prepend(c).First(x => x.GetVisualParent() == p)) is { Length: > 0 } heading)
                return heading;
        return null;
    }

    private static string? ColumnHeader(Control c)
    {
        if (c.FindAncestorOfType<DataGridCell>() is null) return null;
        return DataGridColumn.GetColumnContainingElement(c)?.Header as string is { Length: > 0 } h ? h : null;
    }

    /// <summary>The nearest text before a control among its siblings: a label first, else any heading-like text.</summary>
    private static string? LabelBefore(Panel panel, Control control)
    {
        var index = panel.Children.IndexOf(control);
        if (index < 0) return null;
        IEnumerable<TextBlock> before = panel.Children.Take(index).OfType<TextBlock>().Reverse();
        // In a grid, text in the same row counts even if it was written later in the file.
        if (panel is Grid)
            before = before.Concat(panel.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == Grid.GetRow(control) && Grid.GetColumn(t) < Grid.GetColumn(control)));
        var texts = before.Where(t => !t.Classes.Contains("hint") && !t.Classes.Contains("unsaved") && Clean(t.Text).Length >= 3).ToList();
        var pick = texts.FirstOrDefault(t => t.Classes.Contains("label")) ?? texts.FirstOrDefault();
        return pick is null ? null : Clean(pick.Text);
    }

    private static string Clean(string? text)
    {
        var t = (text ?? "").Trim().TrimEnd('$', ':').Trim();
        return t.Length > 80 ? t[..80] : t;
    }
}
