// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CraftingMargins.App.ViewModels;

/// <summary>
/// Knows whether one editable part of a screen (a grid and its Save button) has changes that
/// haven't been saved.  It compares what's on screen with what was loaded, so typing a value
/// and typing it back is not a change.  A part with unsaved changes is left alone when the
/// rest of the app reloads, and the app asks before closing.
/// </summary>
public sealed partial class EditState(string name, Func<string> signature, Func<bool>? alsoDirty = null) : ObservableObject
{
    /// <summary>What to call it when asking the user, e.g. "cost rules".</summary>
    public string Name { get; } = name;

    [ObservableProperty] private bool _isDirty;
    private string _clean = "";
    private int _loading;
    /// <summary>False until the first load: with nothing loaded yet there is nothing to have changed.</summary>
    private bool _loaded;

    /// <summary>What's on screen now is what's saved.</summary>
    public void MarkClean()
    {
        _loaded = true;
        _clean = signature();
        IsDirty = alsoDirty?.Invoke() ?? false;
    }

    /// <summary>Looks again (called whenever a row changes).</summary>
    public void Check()
    {
        if (_loading > 0 || !_loaded) return;
        IsDirty = (alsoDirty?.Invoke() ?? false) || signature() != _clean;
    }

    /// <summary>The rows on screen differ from what was loaded (ignores <c>alsoDirty</c>).</summary>
    public bool ChangedSinceLoad => _loading == 0 && _loaded && signature() != _clean;

    /// <summary>Wrap a reload in this: changes made by the reload itself don't count, and the result is the new saved state.</summary>
    public IDisposable Loading()
    {
        _loading++;
        return new Scope(this);
    }

    private sealed class Scope(EditState owner) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (--owner._loading == 0) owner.MarkClean();
        }
    }

    /// <summary>Checks again whenever a row is added, removed or edited.</summary>
    public void Watch<T>(ObservableCollection<T> rows) where T : INotifyPropertyChanged
    {
        rows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (INotifyPropertyChanged r in e.OldItems) r.PropertyChanged -= RowChanged;
            if (e.NewItems is not null) foreach (INotifyPropertyChanged r in e.NewItems) r.PropertyChanged += RowChanged;
            if (e.Action != NotifyCollectionChangedAction.Move) Check();
        };
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e) => Check();

    /// <summary>Joins values into one comparable line.</summary>
    public static string Line(params object?[] values) => string.Join('\u001f', values.Select(v => v switch
    {
        null => "",
        decimal d => d.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString(),
    }));

    public static string Lines(IEnumerable<string> lines) => string.Join('\u001e', lines);
}
