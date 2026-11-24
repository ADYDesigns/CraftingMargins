// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;

namespace CraftingMargins.App.ViewModels;

// Rows the grids edit.  Each row keeps the lists its combo boxes need, so the XAML
// never has to reach up to a parent view model.

public sealed partial class MaterialRow : ObservableObject
{
    public Material Model { get; init; } = new();
    [ObservableProperty] private string _name = "";
    /// <summary>The unit it is priced and used in: g, oz, yd, each…</summary>
    [ObservableProperty] private string _unit = Units.Default;
    [ObservableProperty] private decimal _costPerUnit;
    [ObservableProperty] private string _effectiveFrom = "";
    public int PriceVersions => Model.Prices.Count;
    public IReadOnlyList<string> UnitOptions { get; } = Units.All;
}

/// <summary>One material in a part's recipe: how much of it, in the material's own unit.</summary>
public sealed partial class PartLineRow : ObservableObject
{
    /// <summary>Material name → its unit, kept current by the Products view model.</summary>
    public Func<string?, string> UnitOf { get; init; } = _ => "";
    public ObservableCollection<string> MaterialOptions { get; init; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Unit))] private string? _material;
    [ObservableProperty] private decimal _amount;
    public string Unit => UnitOf(Material);
}

public sealed partial class PartRow : ObservableObject
{
    public Part Model { get; init; } = new();
    [ObservableProperty] private string _name = "";
    /// <summary>What goes into one of this part.</summary>
    public ObservableCollection<PartLineRow> Lines { get; } = [];
    [ObservableProperty] private PartLineRow? _selectedLine;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NeedsMachineTime), nameof(TimeOk))] private decimal _machineMinutes;
    /// <summary>The maker's own hands-on minutes.</summary>
    [ObservableProperty] private decimal _ownMinutes;
    [ObservableProperty] private string _effectiveFrom = "";
    [ObservableProperty] private string _notes = "";
    /// <summary>"120 g PLA, 4 each Magnets": the recipe on one line, for the grid.</summary>
    [ObservableProperty] private string _recipe = "";
    public int WeightVersions => Model.Versions.Count;
    /// <summary>Set by the Products view model: the shop tracks machine time, so a part with none is flagged.</summary>
    public bool MachineTimeExpected { get; set; }
    /// <summary>Machine time is tracked, this part has materials, and no machine time was entered: shown with a red dot.</summary>
    public bool NeedsMachineTime => MachineTimeExpected && MachineMinutes == 0 && Lines.Any(l => l.Amount > 0 && l.Unit != Units.Each && l.Unit.Length > 0);
    public bool TimeOk => !NeedsMachineTime;
    public void RefreshRecipe()
    {
        Recipe = string.Join(", ", Lines.Where(l => !string.IsNullOrEmpty(l.Material)).Select(l => $"{l.Amount:0.###} {l.Unit} {l.Material}"));
        OnPropertyChanged(nameof(NeedsMachineTime));
        OnPropertyChanged(nameof(TimeOk));
    }
}

public sealed partial class ListingRow : ObservableObject
{
    public string ListingId { get; init; } = "";
    public string Name { get; init; } = "";
    public int UnitsSold { get; init; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsMapped), nameof(NoCostText))] private int _unmappedUnits;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Subline), nameof(IsMapped), nameof(NoCostText))] private int _ruleCount;
    public string Subline => (UnitsSold == 0 ? "Not sold yet" : $"{UnitsSold} sold") + $" · {RuleCount} rule{(RuleCount == 1 ? "" : "s")}";
    public string NoCostText => UnmappedUnits > 0 ? $"{UnmappedUnits} without a cost" : UnitsSold == 0 && RuleCount == 0 ? "No cost rule yet" : "";
    /// <summary>Every sale of this listing has a cost (or, for a listing that hasn't sold, it has a rule).</summary>
    public bool IsMapped => NoCostText.Length == 0;
}

public sealed partial class ConditionRow : ObservableObject
{
    public string VariationName { get; init; } = "";
    public List<string> Options { get; init; } = [];
    [ObservableProperty] private string _selected = ProductsViewModel.AnyValue;
}

public sealed partial class RulePartRow : ObservableObject
{
    public ObservableCollection<string> PartOptions { get; init; } = [];
    public List<string> MultiplyOptions { get; init; } = [];
    [ObservableProperty] private string? _part;
    [ObservableProperty] private decimal _quantity = 1;
    [ObservableProperty] private string _multiplyBy = ProductsViewModel.NoMultiplier;
}

public sealed partial class RuleRow : ObservableObject
{
    public MappingRule Model { get; init; } = new();
    public ObservableCollection<ConditionRow> Conditions { get; } = [];
    public ObservableCollection<RulePartRow> Parts { get; } = [];
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private decimal _fixedCost;
    [ObservableProperty] private RulePartRow? _selectedPart;

    public string Summary
    {
        get
        {
            var conds = Conditions.Where(c => c.Selected != ProductsViewModel.AnyValue).Select(c => $"{c.VariationName} = {c.Selected}").ToList();
            return conds.Count == 0 ? "Every sale of this listing" : "When " + string.Join(" and ", conds);
        }
    }
    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));
}

/// <param name="Cost">The cost as a number, so the column sorts by amount.</param>
public sealed record PreviewRow(string Variations, int Units, string CostPerUnit, bool Ok, string Status, decimal? Cost = null);

public sealed partial class AliasRow : ObservableObject
{
    [ObservableProperty] private string _listingId = "";
    [ObservableProperty] private string _variationName = "";
    [ObservableProperty] private string _oldValue = "";
    [ObservableProperty] private string _newValue = "";
}

/// <summary>Products &amp; Costs tab: listings and their cost rules, parts, materials, retired-value aliases.</summary>
public sealed partial class ProductsViewModel : ObservableObject
{
    public const string AnyValue = "(any)";
    public const string NoMultiplier = "(no multiplier)";

    private readonly MainViewModel main;
    private ShopData _data = new();
    private List<MappingRule> _allRules = [];
    private Dictionary<string, Listing> _listingById = [];
    /// <summary>Listings whose rule edits are in the working copy (<see cref="_allRules"/>) but not saved yet.</summary>
    private readonly HashSet<string> _pendingRuleListings = [];
    private bool _reloading;

    public ObservableCollection<ListingRow> Listings { get; } = [];
    public ObservableCollection<RuleRow> Rules { get; } = [];
    public ObservableCollection<PreviewRow> Preview { get; } = [];
    public ObservableCollection<MaterialRow> Materials { get; } = [];
    public ObservableCollection<PartRow> Parts { get; } = [];
    public ObservableCollection<AliasRow> Aliases { get; } = [];
    private readonly ObservableCollection<string> _materialNames = [];
    private readonly ObservableCollection<string> _partNames = [];

    // Unsaved-changes tracking, one per Save button.
    public EditState RulesEdit { get; }
    public EditState PartsEdit { get; }
    public EditState MaterialsEdit { get; }
    public EditState AliasesEdit { get; }

    [ObservableProperty] private ListingRow? _selectedListing;
    [ObservableProperty] private RuleRow? _selectedRule;
    [ObservableProperty] private MaterialRow? _selectedMaterial;
    [ObservableProperty] private PartRow? _selectedPart;
    [ObservableProperty] private AliasRow? _selectedAlias;
    [ObservableProperty] private string _listingHeader = "Pick a listing on the left.";
    [ObservableProperty] private int _selectedSubTab;

    private static string Today => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public ProductsViewModel(MainViewModel main)
    {
        this.main = main;
        MaterialsEdit = new("materials", () => EditState.Lines(Materials.Select(m => EditState.Line(m.Model.Id, m.Name, m.Unit, m.CostPerUnit, m.EffectiveFrom))));
        PartsEdit = new("parts", () => EditState.Lines(Parts.Select(p => EditState.Line(p.Model.Id, p.Name, p.MachineMinutes, p.OwnMinutes, p.EffectiveFrom, p.Notes,
            string.Join(";", p.Lines.Select(l => EditState.Line(l.Material, l.Amount)))))));
        AliasesEdit = new("retired options", () => EditState.Lines(Aliases.Select(a => EditState.Line(a.ListingId, a.VariationName, a.OldValue, a.NewValue))));
        RulesEdit = new("cost rules", RulesSignature, () => _pendingRuleListings.Count > 0);
        MaterialsEdit.Watch(Materials);
        PartsEdit.Watch(Parts);
        AliasesEdit.Watch(Aliases);
        Rules.CollectionChanged += (_, _) => RulesEdit.Check();
    }

    private string RulesSignature() => EditState.Lines(Rules.Select(r => EditState.Line(r.Model.Id, r.Name, r.FixedCost,
        string.Join(";", r.Conditions.Select(c => c.VariationName + "=" + c.Selected)),
        string.Join(";", r.Parts.Select(p => EditState.Line(p.Part, p.Quantity, p.MultiplyBy))))));

    /// <summary>Throws away rule edits made on listings other than the one showing (the caller reloads the rest).</summary>
    public void ForgetPendingRules() => _pendingRuleListings.Clear();

    public void Load(ShopData data, IReadOnlyList<Listing>? listings = null)
    {
        var keepListing = SelectedListing?.ListingId;
        var keepRule = SelectedRule is null ? -1 : Rules.IndexOf(SelectedRule);

        // Rule edits on screen go into the working copy before anything is rebuilt, so a reload
        // caused by something else (another Save, a sync finishing) doesn't lose them.
        if (SelectedListing is { } showing) KeepRuleEdits(showing.ListingId);
        foreach (var e in new[] { MaterialsEdit, PartsEdit, AliasesEdit }) e.Check();
        // The craft and what is tracked may have changed in Settings: the hints and the Parts grid's columns follow.
        foreach (var name in new[] { nameof(TrackMachineTime), nameof(TrackOwnTime), nameof(MaterialsHint), nameof(PartsHint) }) OnPropertyChanged(name);

        _data = data;
        if (_pendingRuleListings.Count == 0) _allRules = data.Costs.Rules.Select(Clone).ToList();

        _reloading = true;
        try
        {
            if (!MaterialsEdit.IsDirty)
                using (MaterialsEdit.Loading())
                {
                    Materials.Clear();
                    foreach (var m in data.Costs.Materials)
                    {
                        var p = m.Prices.OrderBy(x => x.EffectiveFrom).LastOrDefault();
                        Materials.Add(new MaterialRow { Model = m, Name = m.Name, Unit = m.Unit, CostPerUnit = p?.CostPerUnit ?? 0, EffectiveFrom = p?.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Today });
                    }
                }
            RebuildMaterialNames();

            if (!PartsEdit.IsDirty)
                using (PartsEdit.Loading())
                {
                    Parts.Clear();
                    foreach (var p in data.Costs.Parts) Parts.Add(ToRow(p));
                }
            RebuildPartNames();

            if (!AliasesEdit.IsDirty)
                using (AliasesEdit.Loading())
                {
                    Aliases.Clear();
                    foreach (var a in data.Costs.Aliases)
                        Aliases.Add(new AliasRow { ListingId = a.ListingId, VariationName = a.VariationName, OldValue = a.OldValue, NewValue = a.NewValue });
                }

            // Listings: everything that has sold (newest sale first), then listings that are for
            // sale but haven't sold yet, so a new listing can be costed before its first order.
            SelectedListing = null;
            Listings.Clear();
            var sold = data.Items.GroupBy(i => i.ListingId).OrderByDescending(g => g.Max(i => i.SaleDate)).ToList();
            foreach (var g in sold)
                Listings.Add(new ListingRow { ListingId = g.Key, Name = g.OrderByDescending(i => i.SaleDate).First().ItemName, UnitsSold = g.Sum(i => i.Quantity) });
            var soldIds = sold.Select(g => g.Key).ToHashSet();
            _listingById = PricingCheck.ListingsById(data, listings ?? []);
            foreach (var (id, l) in _listingById.Where(kv => !soldIds.Contains(kv.Key) && PricingCheck.IsForSale(kv.Value))
                         .OrderBy(kv => kv.Value.Title, StringComparer.OrdinalIgnoreCase))
                Listings.Add(new ListingRow { ListingId = id, Name = EtsyText.Clean(l.Title) });
            // A rule (saved or being edited) for a listing that's no longer in either list still shows, so it can be seen and removed.
            foreach (var id in _allRules.Select(r => r.ListingId).Distinct().Where(id => Listings.All(l => l.ListingId != id)).ToList())
                Listings.Add(new ListingRow { ListingId = id, Name = ListingKey.IsTitle(id) ? id[ListingKey.TitlePrefix.Length..] : $"Listing {id} (no longer for sale)" });
            UpdateListingCounts();
            SelectedListing = Listings.FirstOrDefault(l => l.ListingId == keepListing) ?? Listings.FirstOrDefault();
            if (keepRule >= 0 && keepRule < Rules.Count && SelectedListing?.ListingId == keepListing) SelectedRule = Rules[keepRule];
        }
        finally { _reloading = false; }
    }

    private PartRow ToRow(Part p)
    {
        var v = p.Versions.OrderBy(x => x.EffectiveFrom).LastOrDefault();
        var row = new PartRow
        {
            Model = p, Name = p.Name, MachineMinutes = v?.MachineMinutes ?? 0, OwnMinutes = v?.OwnMinutes ?? 0,
            EffectiveFrom = v?.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Today, Notes = p.Notes,
            MachineTimeExpected = main.Services.Settings.TrackMachineTime,
        };
        Watch(row);
        foreach (var line in v?.Lines ?? [])
            row.Lines.Add(NewLine(_data.Costs.Materials.FirstOrDefault(m => m.Id == line.MaterialId)?.Name, line.Amount));
        row.RefreshRecipe();
        return row;
    }

    /// <summary>The unit of a material by name, from the Materials grid as it stands (saved or not).</summary>
    private string UnitOf(string? material) => Materials.FirstOrDefault(m => m.Name == material)?.Unit ?? "";

    private PartLineRow NewLine(string? material, decimal amount) =>
        new() { MaterialOptions = _materialNames, UnitOf = UnitOf, Material = material, Amount = amount };

    /// <summary>A change to a part's recipe lines is a change to the Parts grid (unsaved-changes tracking), and updates its one-line recipe.</summary>
    private void Watch(PartRow row)
    {
        row.Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (PartLineRow l in e.NewItems) l.PropertyChanged += (_, _) => { row.RefreshRecipe(); PartsEdit.Check(); };
            row.RefreshRecipe();
            PartsEdit.Check();
        };
    }

    [RelayCommand]
    private void AddPartLine()
    {
        if (SelectedPart is not { } p) return;
        var line = NewLine(_materialNames.FirstOrDefault(), 0);
        p.Lines.Add(line);
        p.SelectedLine = line;
    }

    [RelayCommand]
    private void RemovePartLine()
    {
        if (SelectedPart is { SelectedLine: { } l } p) p.Lines.Remove(l);
    }

    /// <summary>The shop tracks machine time / the maker's own time: decides which columns the Parts grid shows.</summary>
    public bool TrackMachineTime => main.Services.Settings.TrackMachineTime;
    public bool TrackOwnTime => main.Services.Settings.TrackOwnTime;
    /// <summary>Hints with examples for the shop's craft.</summary>
    public string MaterialsHint =>
        $"Everything you buy and use up, each priced in the unit you use it in. For example: {Crafts.MaterialExamples(main.Services.Settings.Craft)}. " +
        "Things counted by the piece use the unit 'each'. When a price changes, use 'New price from today' so older sales keep the old price.";
    public string PartsHint =>
        $"A part is a recipe: what goes into one thing you make, for example {Crafts.PartExamples(main.Services.Settings.Craft)}. " +
        "Amounts are in each material's own unit and include what is always lost in making it. Pick a part to edit what goes into it.";

    private void RebuildMaterialNames()
    {
        var names = new List<string>();
        names.AddRange(Materials.Select(m => m.Name));
        if (names.SequenceEqual(_materialNames)) return;   // leave the combo boxes alone when nothing changed
        _materialNames.Clear();
        foreach (var n in names) _materialNames.Add(n);
    }

    private void RebuildPartNames()
    {
        var names = Parts.Select(p => p.Name).ToList();
        if (names.SequenceEqual(_partNames)) return;
        _partNames.Clear();
        foreach (var n in names) _partNames.Add(n);
    }

    private static MappingRule Clone(MappingRule r) => new()
    {
        Id = r.Id, ListingId = r.ListingId, Name = r.Name, FixedCost = r.FixedCost, SortOrder = r.SortOrder,
        Conditions = new(r.Conditions, StringComparer.OrdinalIgnoreCase), Parts = [.. r.Parts],
    };

    /// <summary>The cost model as currently edited (not yet saved), for the preview.</summary>
    private CostModel WorkingModel() => new()
    {
        Materials = _data.Costs.Materials, Parts = _data.Costs.Parts, Aliases = _data.Costs.Aliases,
        CostSettings = _data.Costs.CostSettings, UseWeightsAtSaleDate = _data.Costs.UseWeightsAtSaleDate,
        ZeroCostListings = _data.Costs.ZeroCostListings,
        Rules = _allRules,
    };

    private void UpdateListingCounts()
    {
        var model = WorkingModel();
        var items = _data.Items.ToLookup(i => i.ListingId);
        foreach (var l in Listings)
        {
            l.RuleCount = _allRules.Count(r => r.ListingId == l.ListingId);
            l.UnmappedUnits = items[l.ListingId]
                .Where(i => model.NeedsCost(i.ListingId, model.CostOf(i.ListingId, i.Variations, i.Quantity, i.SaleDate))).Sum(i => i.Quantity);   // no rule, or a rule that adds up to $0
        }
    }

    /// <summary>Packaging per unit for the selected listing; blank = the shop-wide setting.</summary>
    [ObservableProperty] private string _listingPackaging = "";
    /// <summary>The selected listing really costs nothing to make, so a $0 cost isn't flagged.</summary>
    [ObservableProperty] private bool _listingCostsNothing;

    [RelayCommand]
    private async Task SaveListingPackaging()
    {
        if (SelectedListing is not { } l) return;
        var map = main.Services.Db.LoadListingPackaging();
        var text = ListingPackaging.Trim();
        if (text.Length == 0) map.Remove(l.ListingId);
        else if (Input.TryMoney(text, out var v) && v >= 0) map[l.ListingId] = v;
        else { main.Status = "Packaging must be an amount like 0.85, or blank for the shop-wide setting."; return; }
        main.Services.Db.SaveListingPackaging(map);
        var free = main.Services.Db.LoadZeroCostListings();
        if (ListingCostsNothing) free.Add(l.ListingId); else free.Remove(l.ListingId);
        main.Services.Db.SaveZeroCostListings(free);
        var id = l.ListingId;
        await main.RefreshAsync((text.Length == 0 ? "This listing uses the shop-wide packaging cost." : $"Packaging for this listing: {Money.Format(map[id])} per unit.") +
                                (ListingCostsNothing ? "  It's marked as costing nothing to make, so a $0 cost isn't flagged." : ""));
    }

    public void SelectListing(string? listingId)
    {
        SelectedSubTab = 0;
        SelectedListing = Listings.FirstOrDefault(l => l.ListingId == listingId);
    }

    partial void OnSelectedListingChanged(ListingRow? oldValue, ListingRow? newValue)
    {
        // Clicking another listing keeps the edits made on this one (in the working copy, until Save rules).
        if (!_reloading && oldValue is not null) KeepRuleEdits(oldValue.ListingId);
        ShowListing(newValue);
    }

    /// <summary>Moves the rule rows on screen into the working copy, if they differ from what was shown.</summary>
    private void KeepRuleEdits(string listingId)
    {
        if (!RulesEdit.ChangedSinceLoad) return;
        ApplyRuleRows(listingId, strict: false);
        _pendingRuleListings.Add(listingId);
    }

    private void ShowListing(ListingRow? value)
    {
        using (RulesEdit.Loading())
        {
            Rules.Clear();
            Preview.Clear();
            if (value is null) { ListingHeader = "Pick a listing on the left."; return; }
            ListingHeader = ListingKey.IsTitle(value.ListingId) ? $"{value.Name}  (not sold yet)" : $"{value.Name}  (listing {value.ListingId})";
            ListingPackaging = _data.Costs.PackagingByListing.TryGetValue(value.ListingId, out var pk) ? pk.ToString("0.00", CultureInfo.CurrentCulture) : "";
            ListingCostsNothing = _data.Costs.ZeroCostListings.Contains(value.ListingId);
            foreach (var r in _allRules.Where(r => r.ListingId == value.ListingId).OrderBy(r => r.SortOrder))
                Rules.Add(ToRuleRow(r));
            SelectedRule = Rules.FirstOrDefault();
        }
        BuildPreview();
    }

    /// <summary>
    /// Option names and values of the selected listing, for the condition boxes: what has sold
    /// (after aliases) plus everything the listing offers, so an option can have a rule before
    /// its first sale.
    /// </summary>
    private Dictionary<string, List<string>> SeenVariations()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (SelectedListing is null) return result;
        var id = SelectedListing.ListingId;
        var model = WorkingModel();
        void Add(string name, string value)
        {
            if (name.Length == 0) return;
            if (!result.TryGetValue(name, out var list)) result[name] = list = [];
            if (value.Length > 0 && !list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
        }
        foreach (var i in _data.Items.Where(i => i.ListingId == id))
            foreach (var (k, v) in model.ApplyAliases(i.ListingId, Variations.Parse(i.Variations)))
                Add(k, v);
        if (_listingById.TryGetValue(id, out var listing))
            foreach (var (vars, _) in PricingCheck.OfferedOptions(listing))
                foreach (var (k, v) in Variations.Parse(vars))
                    Add(k, v);
        return result;
    }

    private RuleRow ToRuleRow(MappingRule r)
    {
        var row = new RuleRow { Model = r, Name = r.Name, FixedCost = r.FixedCost };
        var seen = SeenVariations();
        foreach (var name in seen.Keys.Union(r.Conditions.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var options = new List<string> { AnyValue };
            if (seen.TryGetValue(name, out var vals)) options.AddRange(vals.OrderBy(v => v));
            var selected = r.Conditions.TryGetValue(name, out var c) && c != MappingRule.Any ? c : AnyValue;
            if (!options.Contains(selected, StringComparer.OrdinalIgnoreCase)) options.Add(selected);
            var cond = new ConditionRow { VariationName = name, Options = options, Selected = selected };
            cond.PropertyChanged += (_, _) => { row.RefreshSummary(); RulesEdit.Check(); };
            row.Conditions.Add(cond);
        }
        var multiply = new List<string> { NoMultiplier };
        multiply.AddRange(seen.Keys);
        foreach (var p in r.Parts)
            row.Parts.Add(Watch(new RulePartRow
            {
                PartOptions = _partNames, MultiplyOptions = multiply,
                Part = _data.Costs.Parts.FirstOrDefault(x => x.Id == p.PartId)?.Name,
                Quantity = p.Quantity, MultiplyBy = p.MultiplyByVariation ?? NoMultiplier,
            }));
        row.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(RuleRow.Name) or nameof(RuleRow.FixedCost)) RulesEdit.Check(); };
        row.Parts.CollectionChanged += (_, _) => RulesEdit.Check();
        return row;
    }

    private RulePartRow Watch(RulePartRow p)
    {
        p.PropertyChanged += (_, _) => RulesEdit.Check();
        return p;
    }

    /// <summary>
    /// Copies the rule rows on screen back into the working rule list for a listing.
    /// Strict (Check, Save): a row with no part picked stops it with a message.
    /// Not strict (keeping edits while moving away): such rows are left out.
    /// </summary>
    private bool ApplyRuleRows(string? listingId, bool strict)
    {
        if (listingId is null) return true;
        if (strict)
            foreach (var row in Rules)
            {
                if (row.Parts.Any(p => _data.Costs.Parts.All(x => x.Name != p.Part))) { main.Status = $"Rule \"{row.Name}\" has a row with no part picked (or a part that hasn't been saved yet)."; return false; }
                if (row.FixedCost < 0 || row.Parts.Any(p => p.Quantity < 0)) { main.Status = $"Rule \"{row.Name}\": costs and quantities can't be negative."; return false; }
            }
        _allRules.RemoveAll(r => r.ListingId == listingId);
        int order = 0;
        foreach (var row in Rules)
        {
            var r = row.Model;
            r.ListingId = listingId;
            r.Name = row.Name;
            r.FixedCost = row.FixedCost;
            r.SortOrder = order++;
            r.Conditions = new(StringComparer.OrdinalIgnoreCase);
            foreach (var c in row.Conditions.Where(c => c.Selected != AnyValue)) r.Conditions[c.VariationName] = c.Selected;
            r.Parts = [];
            foreach (var p in row.Parts)
            {
                var part = _data.Costs.Parts.FirstOrDefault(x => x.Name == p.Part);
                if (part is null) continue;
                r.Parts.Add(new RulePart(part.Id, p.Quantity, p.MultiplyBy == NoMultiplier ? null : p.MultiplyBy));
            }
            _allRules.Add(r);
        }
        return true;
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        if (!ApplyRuleRows(SelectedListing?.ListingId, strict: true)) return;
        BuildPreview();
    }

    /// <summary>What each option costs under the rules as they stand in the working copy: options that have sold, then offered ones that haven't.</summary>
    private void BuildPreview()
    {
        Preview.Clear();
        UpdateListingCounts();
        if (SelectedListing is null) return;
        var id = SelectedListing.ListingId;
        var model = WorkingModel();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string vars, int units, DateOnly date)
        {
            var c = model.CostOf(id, vars, 1, date);
            var retired = Variations.Parse(vars).Any(v => model.IsRetired(id, v.Key, v.Value));
            var free = c.Mapped && c.Total == 0 && model.ZeroCostListings.Contains(id);
            Preview.Add(new PreviewRow(vars.Length == 0 ? "(no variations)" : vars, units,
                c.Mapped ? Money.Format(Math.Round(c.Total, 2)) : "—", !model.NeedsCost(id, c),
                (!c.Mapped ? c.Problem : free ? "Costs nothing to make" : c.Total == 0 ? "Its rule adds up to $0" : c.MakingHours > 0 ? $"{c.MakingHours:0.##} making hours" : "Costed")
                + (units == 0 ? "  (not sold yet)" : "") + (retired ? "  (retired value)" : ""),
                c.Mapped ? Math.Round(c.Total, 2) : null));
        }

        foreach (var g in _data.Items.Where(i => i.ListingId == id).GroupBy(i => i.Variations).OrderByDescending(g => g.Sum(i => i.Quantity)))
        {
            shown.Add(Variations.Format(model.ApplyAliases(id, Variations.Parse(g.Key))));
            Add(g.Key, g.Sum(i => i.Quantity), g.Max(i => i.SaleDate));
        }
        if (_listingById.TryGetValue(id, out var listing))
            foreach (var (vars, _) in PricingCheck.OfferedOptions(listing))
                if (shown.Add(vars)) Add(vars, 0, today);
    }

    [RelayCommand]
    private void AddRule()
    {
        if (SelectedListing is null) return;
        var row = ToRuleRow(new MappingRule { ListingId = SelectedListing.ListingId, Name = $"Rule {Rules.Count + 1}" });
        Rules.Add(row);
        SelectedRule = row;
    }

    [RelayCommand]
    private void DeleteRule()
    {
        if (SelectedRule is null) return;
        Rules.Remove(SelectedRule);
        SelectedRule = Rules.FirstOrDefault();
        RefreshPreview();
    }

    [RelayCommand]
    private void AddRulePart()
    {
        if (SelectedRule is null) return;
        var multiply = new List<string> { NoMultiplier };
        multiply.AddRange(SeenVariations().Keys);
        var p = Watch(new RulePartRow { PartOptions = _partNames, MultiplyOptions = multiply, Part = _partNames.FirstOrDefault() });
        SelectedRule.Parts.Add(p);
        SelectedRule.SelectedPart = p;
    }

    [RelayCommand]
    private void RemoveRulePart()
    {
        if (SelectedRule?.SelectedPart is { } p) SelectedRule.Parts.Remove(p);
    }

    [RelayCommand]
    private async Task SaveRules()
    {
        if (!ApplyRuleRows(SelectedListing?.ListingId, strict: true)) return;
        main.Services.Db.SaveRules(_allRules);
        _pendingRuleListings.Clear();
        RulesEdit.MarkClean();
        await main.RefreshAsync("Cost rules saved.");
    }

    // ---------- materials ----------

    [RelayCommand]
    private void AddMaterial()
    {
        var row = new MaterialRow { Name = "New material", Unit = Crafts.DefaultUnit(main.Services.Settings.Craft), EffectiveFrom = "2000-01-01" };
        Materials.Add(row);
        SelectedMaterial = row;
    }

    [RelayCommand]
    private void NewMaterialPrice()
    {
        if (SelectedMaterial is not { } m) return;
        // Keep the current price as history; the row now edits a new price from today.
        if (TryDate(m.EffectiveFrom, out var d)) Upsert(m.Model.Prices, new MaterialPrice(d, m.CostPerUnit));
        m.EffectiveFrom = Today;
        main.Status = $"Enter {m.Name}'s new cost per {m.Unit}, then Save.  Sales before today keep the old price.";
    }

    [RelayCommand]
    private void DeleteMaterial()
    {
        if (SelectedMaterial is not { } m) return;
        if (Parts.FirstOrDefault(p => p.Lines.Any(l => l.Material == m.Name)) is { } user) { main.Status = $"{m.Name} is used by the part \"{user.Name}\"; take it out of that part first."; return; }
        Materials.Remove(m);
    }

    [RelayCommand]
    private async Task SaveMaterials()
    {
        foreach (var row in Materials)
        {
            if (!TryDate(row.EffectiveFrom, out _)) { main.Status = $"\"{row.EffectiveFrom}\" isn't a date (use 2026-10-31)."; SelectedMaterial = row; return; }
            if (string.IsNullOrWhiteSpace(row.Name)) { main.Status = "Every material needs a name."; SelectedMaterial = row; return; }
            if (row.CostPerUnit < 0) { main.Status = $"{row.Name}: the cost can't be negative."; SelectedMaterial = row; return; }
            if (!Units.All.Contains(row.Unit)) { main.Status = $"{row.Name}: pick a unit from the list."; SelectedMaterial = row; return; }
        }
        if (Materials.GroupBy(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) { main.Status = "Two materials have the same name."; return; }
        var list = new List<Material>();
        foreach (var row in Materials)
        {
            TryDate(row.EffectiveFrom, out var d);
            var m = row.Model;
            m.Name = row.Name.Trim();
            m.Unit = row.Unit;
            Upsert(m.Prices, new MaterialPrice(d, row.CostPerUnit));
            list.Add(m);
        }
        main.Services.Db.SaveMaterials(list);
        MaterialsEdit.MarkClean();
        await main.RefreshAsync("Materials saved.");
    }

    // ---------- parts ----------

    [RelayCommand]
    private void AddPart()
    {
        var row = new PartRow { Name = "New part", EffectiveFrom = "2000-01-01", MachineTimeExpected = main.Services.Settings.TrackMachineTime };
        Watch(row);
        if (_materialNames.Count > 0) row.Lines.Add(NewLine(_materialNames[0], 0));
        Parts.Add(row);
        SelectedPart = row;
    }

    /// <summary>The recipe lines of a part row as stored lines; a line whose material isn't saved yet is left out.</summary>
    private List<PartLine> StoredLines(PartRow row) =>
        [.. row.Lines.Select(l => (Material: _data.Costs.Materials.FirstOrDefault(m => m.Name == l.Material), l.Amount))
            .Where(x => x.Material is not null).Select(x => new PartLine(x.Material!.Id, x.Amount))];

    [RelayCommand]
    private void NewPartWeights()
    {
        if (SelectedPart is not { } p) return;
        // Keep the current recipe as history; the row now edits a new one from today.
        if (TryDate(p.EffectiveFrom, out var d)) Upsert(p.Model.Versions, new PartVersion(d, StoredLines(p), p.MachineMinutes, p.OwnMinutes));
        p.EffectiveFrom = Today;
        main.Status = $"Enter what goes into {p.Name} from today, then Save.  The old amounts are kept for the \"amounts at the sale date\" setting.";
    }

    [RelayCommand]
    private void DeletePart()
    {
        if (SelectedPart is not { } p) return;
        if (_allRules.Any(r => r.Parts.Any(x => x.PartId == p.Model.Id && p.Model.Id != 0))) { main.Status = $"{p.Name} is used by a cost rule; remove it there first."; return; }
        Parts.Remove(p);
    }

    [RelayCommand]
    private async Task SaveParts()
    {
        var materials = _data.Costs.Materials;
        foreach (var row in Parts)
        {
            if (!TryDate(row.EffectiveFrom, out _)) { main.Status = $"\"{row.EffectiveFrom}\" isn't a date (use 2026-10-31)."; SelectedPart = row; return; }
            if (string.IsNullOrWhiteSpace(row.Name)) { main.Status = "Every part needs a name."; SelectedPart = row; return; }
            if (row.MachineMinutes < 0 || row.OwnMinutes < 0 || row.Lines.Any(l => l.Amount < 0)) { main.Status = $"{row.Name}: amounts and minutes can't be negative."; SelectedPart = row; return; }
            if (row.Lines.FirstOrDefault(l => string.IsNullOrEmpty(l.Material)) is { } blank) { main.Status = $"{row.Name}: a row has no material picked. Pick one or remove the row."; SelectedPart = row; row.SelectedLine = blank; return; }
            if (row.Lines.FirstOrDefault(l => materials.All(m => m.Name != l.Material)) is { } unsaved)
            { main.Status = $"Save the material \"{unsaved.Material}\" (Materials tab) before using it in a part."; SelectedPart = row; return; }
            if (row.Lines.GroupBy(l => l.Material).FirstOrDefault(g => g.Count() > 1) is { } twice)
            { main.Status = $"{row.Name}: \"{twice.Key}\" is listed twice. Put the whole amount on one row."; SelectedPart = row; return; }
        }
        if (Parts.GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) { main.Status = "Two parts have the same name."; return; }
        var list = new List<Part>();
        foreach (var row in Parts)
        {
            TryDate(row.EffectiveFrom, out var d);
            var p = row.Model;
            p.Name = row.Name.Trim();
            p.Notes = row.Notes;
            Upsert(p.Versions, new PartVersion(d, StoredLines(row), row.MachineMinutes, row.OwnMinutes));
            list.Add(p);
        }
        main.Services.Db.SaveParts(list);
        PartsEdit.MarkClean();
        await main.RefreshAsync("Parts saved.");
    }

    // ---------- aliases ----------

    [RelayCommand]
    private void AddAlias()
    {
        var row = new AliasRow { ListingId = SelectedListing?.ListingId ?? "" };
        Aliases.Add(row);
        SelectedAlias = row;
    }

    [RelayCommand]
    private void DeleteAlias()
    {
        if (SelectedAlias is { } a) Aliases.Remove(a);
    }

    [RelayCommand]
    private async Task SaveAliases()
    {
        // The same old value mapped twice for the same listing and option: only one could ever apply, so say so instead of saving both.
        var twice = Aliases.Where(a => a.VariationName.Trim().Length > 0 && a.OldValue.Trim().Length > 0)
            .GroupBy(a => (a.ListingId.Trim(), a.VariationName.Trim().ToUpperInvariant(), a.OldValue.Trim().ToUpperInvariant()))
            .FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
        {
            SelectedAlias = twice.Last();
            main.Status = $"\"{twice.First().OldValue.Trim()}\" is listed twice for the option \"{twice.First().VariationName.Trim()}\"" +
                          (twice.Key.Item1.Length > 0 ? $" on listing {twice.Key.Item1}" : "") + ". Remove one of the rows.";
            return;
        }
        var list = Aliases
            .Where(a => a.VariationName.Trim().Length > 0 && a.OldValue.Trim().Length > 0)
            .Select(a => new VariationAlias { ListingId = a.ListingId.Trim(), VariationName = a.VariationName.Trim(), OldValue = a.OldValue.Trim(), NewValue = a.NewValue.Trim() })
            .ToList();
        main.Services.Db.SaveAliases(list);
        AliasesEdit.MarkClean();
        await main.RefreshAsync("Retired values saved.");
    }

    // ---------- taking the typed-in costs out, and bringing them back ----------

    /// <summary>
    /// Writes the saved materials, parts and cost rules as three CSV files in a folder the user
    /// picks.  They come back in by dropping them on Home like any Etsy file.
    /// </summary>
    [RelayCommand]
    private async Task ExportCosts()
    {
        if (UnsavedCosts() is { Length: > 0 } unsaved &&
            !await Dialogs.Confirm("Export costs", $"The export writes what is saved. Your unsaved changes ({unsaved}) won't be in it.\n\nExport anyway?", "Export", "Go back")) return;
        var folder = await Dialogs.PickFolder("Where should the cost files go?", main.Services.Settings.LastExportFolder);
        if (folder is null) return;
        try
        {
            var files = CraftingMargins.Data.CostsFile.Export(main.Services.Db, folder);
            main.Services.Settings.LastExportFolder = folder;
            main.Services.SaveSettings();
            main.Status = $"Saved {files.Count} files in {folder}: materials, parts and cost rules. To bring them back, drop them on the Home tab.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            main.Status = "Couldn't save the files (is one open in Excel, or the folder read-only?): " + ex.Message;
        }
    }

    private string UnsavedCosts()
    {
        var edits = new[] { RulesEdit, PartsEdit, MaterialsEdit };
        foreach (var e in edits) e.Check();
        return string.Join(", ", edits.Where(e => e.IsDirty).Select(e => e.Name));
    }

    // ---------- helpers ----------

    private static bool TryDate(string text, out DateOnly d) => Input.TryDate(text, out d);

    private static void Upsert(List<MaterialPrice> list, MaterialPrice v)
    {
        list.RemoveAll(x => x.EffectiveFrom == v.EffectiveFrom);
        list.Add(v);
    }

    private static void Upsert(List<PartVersion> list, PartVersion v)
    {
        list.RemoveAll(x => x.EffectiveFrom == v.EffectiveFrom);
        list.Add(v);
    }
}
