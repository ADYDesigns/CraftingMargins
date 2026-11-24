// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.App.ViewModels;

/// <summary>Home: import area and the shop at a glance.</summary>
public sealed record ImportLine(bool Ok, string FileName, string Summary);

/// <summary>One step of the getting-started checklist on Home.</summary>
public sealed record ChecklistItem(bool Done, string Text, string Hint, int Tab, int SubTab = -1)
{
    public string Mark => Done ? "✓" : "";
    public bool NotDone => !Done;
}

public sealed partial class HomeViewModel : ObservableObject
{
    // ----- Suggestions: facts from the shop's own numbers, each with a "Show me" and "Hide" -----
    public ObservableCollection<Suggestion> SuggestionList { get; } = [];
    [ObservableProperty] private bool _hasSuggestions;
    public Action<Suggestion>? ShowSuggestion { get; set; }
    public Action<Suggestion>? HideSuggestion { get; set; }
    [RelayCommand] private void ShowMe(Suggestion s) => ShowSuggestion?.Invoke(s);
    [RelayCommand] private void HideOne(Suggestion s) { HideSuggestion?.Invoke(s); SuggestionList.Remove(s); HasSuggestions = SuggestionList.Count > 0; }

    /// <summary>Shows the suggestions that aren't hidden (a hidden one comes back when what it's about changes).</summary>
    public void LoadSuggestions(IEnumerable<Suggestion> all, IReadOnlyDictionary<string, string> hidden)
    {
        SuggestionList.Clear();
        foreach (var s in all.Where(s => !(hidden.TryGetValue(s.Key, out var fp) && fp == s.Fingerprint))) SuggestionList.Add(s);
        HasSuggestions = SuggestionList.Count > 0;
    }

    public ObservableCollection<ChecklistItem> Checklist { get; } = [];
    [ObservableProperty] private bool _showChecklist;
    [ObservableProperty] private string _checklistTitle = "";
    /// <summary>Set by the main view model: go to a tab (and sub-tab).</summary>
    public Action<int, int>? Navigate { get; set; }
    public Action? HideChecklistAction { get; set; }

    [RelayCommand] private void Go(ChecklistItem item) => Navigate?.Invoke(item.Tab, item.SubTab);
    [RelayCommand] private void HideChecklist() => HideChecklistAction?.Invoke();

    /// <summary>What a new user still has to do.  Each step ticks itself off from the data.</summary>
    public void LoadChecklist(ShopData data, int listings, int unmappedUnits, bool hidden)
    {
        var tab = MainViewModel.TabProducts;
        ChecklistItem[] items =
        [
            new(data.Statement.Count > 0, "Import your monthly statements",
                "Shop Manager → Finances → Monthly statements → Download CSV, one per month. Drop them in the box above.", MainViewModel.TabHome),
            new(data.Orders.Count > 0 && data.Items.Count > 0, "Import your orders",
                "Settings → Options → Download Data: Sold Orders and Sold Order Items (or connect the Etsy API).", MainViewModel.TabHome),
            new(listings > 0, "Import your listings",
                "Download Data → Currently for sale listings, so the pricing check knows what's still for sale.", MainViewModel.TabHome),
            new(data.Costs.Materials.Count > 0, "Add your materials", "Everything you buy and use up, with what each costs.", tab, 2),
            new(data.Costs.Parts.Count > 0, "Add the parts you make", "What goes into each thing you make, and how long it takes.", tab, 1),
            new(data.Costs.Rules.Count > 0 && unmappedUnits == 0, "Give every listing a cost rule",
                unmappedUnits > 0 ? $"{unmappedUnits} sold unit(s) still have no cost." : "Link each listing's options to the parts it uses.", tab, 0),
        ];
        Checklist.Clear();
        foreach (var i in items) Checklist.Add(i);
        var done = items.Count(i => i.Done);
        ChecklistTitle = $"Getting started: {done} of {items.Length} done";
        ShowChecklist = !hidden && done < items.Length;
    }

    public ObservableCollection<ImportLine> ImportResults { get; } = [];
    [ObservableProperty] private bool _hasImportResults;
    [ObservableProperty] private string _yearProfit = "—";
    [ObservableProperty] private string _monthProfit = "—";
    [ObservableProperty] private string _monthLabel = "This month";
    [ObservableProperty] private string _selfTest = "—";
    [ObservableProperty] private bool _selfTestOk = true;
    [ObservableProperty] private string _attentionSummary = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _monthCompare = "";
    [ObservableProperty] private string _dropHint = "Drop new Etsy CSV files here to bring your numbers up to date";
    [ObservableProperty] private string _yearCompare = "";
    [ObservableProperty] private string _belowFloor = "—";
    [ObservableProperty] private bool _belowFloorOk = true;
    [ObservableProperty] private IReadOnlyList<ChartBar> _profitChart = [];
    [ObservableProperty] private string _chartTitle = "Net profit by month";
    [ObservableProperty] private MoneySplit _yearSplit = MoneySplit.Empty;
    [ObservableProperty] private string _yearSplitTitle = "Where the money went";

    public void Load(ShopData data, List<MonthlyPnL> months, AttentionViewModel attention, int belowFloor)
    {
        HasData = months.Count > 0;
        BelowFloorOk = belowFloor == 0;
        BelowFloor = belowFloor == 0 ? "None" : $"{belowFloor} option{(belowFloor == 1 ? "" : "s")}";
        var last12 = months.TakeLast(12).ToList();
        ProfitChart = last12.Select(m => new ChartBar(
            DateOnly.ParseExact(m.Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString(last12.Select(x => x.Month[..4]).Distinct().Count() > 1 ? "MMM yy" : "MMM"),
            Math.Round(m.NetProfit, 2))).ToList();
        ChartTitle = last12.Count < 2 ? "Net profit by month" : $"Net profit by month, {last12[0].Month} to {last12[^1].Month}";
        if (months.Count == 0)
        {
            YearSplit = MoneySplit.Empty;
            YearProfit = MonthProfit = "—";
            SelfTest = "No statements yet";
            AttentionSummary = "Drop your Etsy CSV files above to get started.";
            return;
        }
        var year = months[^1].Month[..4];
        YearProfit = Money.Format(months.Where(m => m.Month.StartsWith(year)).Sum(m => m.NetProfit));
        YearSplit = MoneySplit.From(months.Where(m => m.Month.StartsWith(year)));
        YearSplitTitle = $"Where the money went in {year}";
        // Comparisons under the big numbers: last month, and the same months a year earlier.
        MonthCompare = months.Count > 1
            ? $"{DateOnly.ParseExact(months[^2].Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture):MMMM}: {Money.Format(months[^2].NetProfit)}" : "";
        var thisYear = months.Where(m => m.Month.StartsWith(year)).Select(m => m.Month[5..]).ToHashSet();
        var lastYear = months.Where(m => m.Month.StartsWith((int.Parse(year) - 1).ToString()) && thisYear.Contains(m.Month[5..])).ToList();
        YearCompare = lastYear.Count > 0 ? $"Same months of {int.Parse(year) - 1}: {Money.Format(lastYear.Sum(m => m.NetProfit))}" : "";
        MonthLabel = DateOnly.ParseExact(months[^1].Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("MMMM yyyy").ToUpperInvariant();
        MonthProfit = Money.Format(months[^1].NetProfit);
        var failed = months.Where(m => !m.SelfTestPassed).Select(m => m.Month).ToList();
        SelfTestOk = failed.Count == 0;
        SelfTest = SelfTestOk ? "Every month adds up to the penny" : "Doesn't add up: " + string.Join(", ", failed);
        AttentionSummary = attention.AllClear ? "Nothing needs attention." : "Needs attention: " + attention.Summary;
    }

    public void ShowImportResults(List<ImportResult> results)
    {
        ImportResults.Clear();
        foreach (var r in results)
            ImportResults.Add(new ImportLine(r.Ok, r.FileName, r.Summary));
        HasImportResults = ImportResults.Count > 0;
    }
}

/// <summary>One row of the Monthly P&amp;L grid: the headline numbers.  The full breakdown shows below the grid.</summary>
public sealed class PnlRow
{
    public string Month { get; init; } = "";          // "2026-03" or "2026 total"
    public string Label { get; init; } = "";          // "March 2026" or "2026 total"
    public bool IsTotal { get; init; }
    public bool Passed { get; init; }
    public int Orders { get; init; }
    public decimal NetSales { get; init; }
    public decimal Refunds { get; init; }
    public decimal EtsyFees { get; init; }
    public decimal Shipping { get; init; }
    public decimal Costs { get; init; }
    public decimal OutsideEtsy { get; init; }
    public decimal NetProfit { get; init; }
    public List<CheckIssue> Issues { get; init; } = [];
    public List<CalcLine> Lines { get; init; } = [];
    /// <summary>The months this row adds up (one, or a whole year), for the donut.</summary>
    public List<MonthlyPnL> Source { get; init; } = [];
    /// <summary>Profit as a share of what came in (sales after tax, plus sales outside Etsy).</summary>
    public decimal? Margin => NetSales + OutsideEtsy is var s && s > 0 ? NetProfit / s : null;
}

public sealed partial class PnlViewModel : ObservableObject
{
    public ObservableCollection<PnlRow> Rows { get; } = [];
    public ObservableCollection<string> SelectedIssues { get; } = [];
    public ObservableCollection<CalcLine> SelectedLines { get; } = [];
    [ObservableProperty] private PnlRow? _selected;
    [ObservableProperty] private string _selectedTitle = "";
    [ObservableProperty] private string _selectedVerdict = "";
    [ObservableProperty] private MoneySplit _selectedSplit = MoneySplit.Empty;
    [ObservableProperty] private decimal _selectedNetProfit;

    private PnlRow? _lastSelected;

    partial void OnSelectedChanged(PnlRow? value)
    {
        // Switching tabs rebuilds the grid, which reports "nothing selected"; keep the month that was picked.
        if (value is null && _lastSelected is not null && Rows.Contains(_lastSelected))
        {
            var keep = _lastSelected;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Selected is null) Selected = keep; });
            return;
        }
        _lastSelected = value;
        SelectedIssues.Clear();
        SelectedLines.Clear();
        if (value is null) { SelectedTitle = SelectedVerdict = ""; SelectedSplit = MoneySplit.Empty; return; }
        SelectedTitle = value.Label;
        SelectedSplit = MoneySplit.From(value.Source);
        foreach (var l in value.Lines.Where(l => !l.Bold)) SelectedLines.Add(l);   // net profit is shown pinned under the list
        SelectedNetProfit = value.NetProfit;
        foreach (var i in value.Issues) SelectedIssues.Add($"{(value.IsTotal ? i.Month + ": " : "")}{(i.IsError ? "Problem: " : "Note: ")}{i.Message}");
        SelectedVerdict = !value.Passed ? "Doesn't add up. The lines below say where."
            : value.Issues.Count == 0 ? "Every Etsy line adds up to the penny." : "Adds up to the penny, with a note or two.";
    }

    public void Load(List<MonthlyPnL> months)
    {
        Rows.Clear();
        static PnlRow Row(string key, string label, IReadOnlyCollection<MonthlyPnL> ms, bool total)
        {
            decimal S(Func<MonthlyPnL, decimal> f) => ms.Sum(f);
            var lines = new List<CalcLine>
            {
                new("Sales (sales tax taken out)", S(m => m.NetSales)),
                new("Refunds", S(m => m.Refunds)),
                new("Transaction fees", S(m => m.TransactionFees)),
                new("Processing fees", S(m => m.ProcessingFees)),
                new("Listing fees", S(m => m.ListingFees)),
                new("Offsite Ads", S(m => m.OffsiteAds)),
                new("Etsy Ads", S(m => m.EtsyAds)),
                new("Shipping labels", S(m => m.ShippingLabels)),
                new("Shipping paid outside Etsy", S(m => m.ExtraShipping)),
                new("Materials", S(m => m.Material + m.Waste)),
                new("Components", S(m => m.Components)),
                new("Packaging", S(m => m.Packaging)),
                new("Machine time", S(m => m.MachineCost)),
                new("Your own time", S(m => m.OwnCost)),
                new("Fixed item costs", S(m => m.FixedCosts)),
                new("Sales outside Etsy", S(m => m.OtherSales)),
                new("Fees outside Etsy", S(m => m.OtherFees)),
            };
            lines.RemoveAll(l => l.Amount == 0 && l.Name != "Sales (sales tax taken out)");
            lines.Add(new("Net profit", Math.Round(S(m => m.NetProfit), 2), true));
            return new PnlRow
            {
                Month = key, Label = label, IsTotal = total, Passed = ms.All(m => m.SelfTestPassed),
                Orders = ms.Sum(m => m.Orders), NetSales = S(m => m.NetSales), Refunds = S(m => m.Refunds), EtsyFees = S(m => m.EtsyFees),
                Shipping = S(m => m.Shipping), Costs = S(m => m.OtherCosts), OutsideEtsy = S(m => m.OtherSales + m.OtherFees),
                NetProfit = Math.Round(S(m => m.NetProfit), 2), Issues = [.. ms.SelectMany(m => m.Issues)], Lines = lines, Source = [.. ms],
            };
        }

        foreach (var m in months)
            Rows.Add(Row(m.Month, DateOnly.ParseExact(m.Month + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("MMMM yyyy"), [m], false));
        foreach (var year in months.GroupBy(m => m.Month[..4]))
            Rows.Add(Row($"{year.Key} total", $"{year.Key} total", [.. year], true));
        Selected = Rows.LastOrDefault();
    }
}

/// <summary>One sale made outside Etsy, as edited in the grid.</summary>
public sealed partial class OtherSaleRow : ObservableObject
{
    public string OrderId { get; init; } = "";
    public IReadOnlyList<string> ItemOptions { get; init; } = [];
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _channel = "";
    [ObservableProperty] private string _item = "";
    [ObservableProperty] private string _options = "";
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private decimal _price;
    [ObservableProperty] private decimal _shipping;
    [ObservableProperty] private decimal _fees;
    [ObservableProperty] private decimal _postage;
    [ObservableProperty] private string _note = "";
}

public sealed partial class OrdersViewModel : ObservableObject
{
    private readonly MainViewModel main;
    public EditState OtherSalesEdit { get; }

    public OrdersViewModel(MainViewModel main)
    {
        this.main = main;
        OtherSalesEdit = new("sales outside Etsy", () => EditState.Lines(OtherSales.Select(r =>
            EditState.Line(r.OrderId, r.Date, r.Channel, r.Item, r.Options, r.Quantity, r.Price, r.Shipping, r.Fees, r.Postage, r.Note))));
        OtherSalesEdit.Watch(OtherSales);
    }

    private List<OrderProfit> _all = [];
    public ObservableCollection<OrderProfit> Orders { get; } = [];
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private OrderProfit? _selected;
    [ObservableProperty] private string _extraShipping = "";
    [ObservableProperty] private string _summary = "";

    partial void OnFilterChanged(string value) => Apply();
    public ObservableCollection<CalcLine> SelectedLines { get; } = [];
    [ObservableProperty] private string _selectedTitle = "Pick an order";
    [ObservableProperty] private string _selectedNote = "Click an order in the list to see where its money went.";

    partial void OnSelectedChanged(OrderProfit? value)
    {
        ExtraShipping = value is null ? "" : (-value.ExtraShipping).ToString("0.00");
        SelectedLines.Clear();
        if (value is null) { SelectedTitle = "Pick an order"; SelectedNote = "Click an order in the list to see where its money went."; return; }
        SelectedTitle = $"Order {value.OrderId}";
        SelectedNote = $"{value.SaleDate:MMMM d, yyyy}" + (value.DateShipped is { } s ? $", shipped {s:MMM d}" : "") + (value.Status.Length > 0 ? $".  {value.Status}." : ".");
        void L(string name, decimal amount, bool always = false) { if (always || amount != 0) SelectedLines.Add(new CalcLine(name, amount)); }
        if (value.OnStatement)
        {
            L(value.IsOther ? "Paid by the buyer" : "Paid by the buyer (with tax)", value.Gross, true);
            L("Sales tax (passed to the state)", value.TaxLines);
            L("Refunds", value.Refunds);
            L(value.IsOther ? "Fees" : "Transaction fees", value.IsOther ? 0 : value.TransactionFees);
            L(value.IsOther ? "Fees" : "Processing fee", value.ProcessingFees);
            L("Offsite Ads fee", value.OffsiteAds);
            L("Listing fees", value.ListingFees);
        }
        else
        {
            L("Items", value.ItemPrice - value.Discount, true);
            L("Shipping charged", value.ShippingCharged);
        }
        L(value.LabelShared ? "Shipping label (even share)" : "Shipping label", value.Label);
        L("Shipping paid outside Etsy", value.ExtraShipping);
        L("Making costs", value.Costs);
        L("Packaging", value.Packaging);
        if (value.OnStatement) SelectedLines.Add(new CalcLine("Profit", value.Profit, true));
        else SelectedNote += "  Etsy's fees and the profit show once its money is on a statement you've imported.";
    }

    public void Load(List<OrderProfit> orders, ShopData data)
    {
        var keep = Selected?.OrderId;
        _all = orders;
        Apply();
        if (keep is not null) Selected = Orders.FirstOrDefault(o => o.OrderId == keep);
        LoadOtherSales(data);
    }

    // ----- Sales outside Etsy -----
    [ObservableProperty] private int _selectedSubTab;
    [ObservableProperty] private OtherSaleRow? _selectedOtherSale;
    [ObservableProperty] private string _otherSummary = "";
    public ObservableCollection<OtherSaleRow> OtherSales { get; } = [];
    private Dictionary<string, string> _listingIdByName = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _itemNames = [];

    private void LoadOtherSales(ShopData data)
    {
        // Item names to pick from: Etsy listings (newest title) and items already sold elsewhere.
        _listingIdByName = data.Items.GroupBy(i => i.ListingId)
            .Select(g => (Id: g.Key, Name: g.OrderByDescending(i => i.SaleDate).First().ItemName))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        _itemNames = [.. _listingIdByName.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        var year = DateTime.Today.Year;
        var thisYear = data.Orders.Where(o => o.IsOther && o.SaleDate.Year == year).ToList();
        var saved = data.Orders.Count(o => o.IsOther);
        OtherSummary = saved == 0 ? "No sales outside Etsy yet." :
            $"{saved} sale(s); {thisYear.Count} in {year} worth {Money.Format(thisYear.Sum(o => o.OrderValue + o.Shipping))}.";
        OtherSalesEdit.Check();
        if (OtherSalesEdit.IsDirty) return;   // unsaved rows stay as typed
        using var loading = OtherSalesEdit.Loading();
        var items = data.Items.ToLookup(i => i.OrderId);
        OtherSales.Clear();
        foreach (var o in data.Orders.Where(o => o.IsOther).OrderByDescending(o => o.SaleDate))
        {
            var i = items[o.OrderId].FirstOrDefault();
            OtherSales.Add(new OtherSaleRow
            {
                OrderId = o.OrderId, Date = o.SaleDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Channel = o.OrderType, Item = i?.ItemName ?? "",
                Options = i?.Variations ?? "", Quantity = i?.Quantity ?? 1, Price = i?.Price ?? 0, Shipping = o.Shipping,
                Fees = o.CardProcessingFees, Postage = o.ExtraShippingCost, Note = o.Status, ItemOptions = _itemNames,
            });
        }
    }

    [RelayCommand]
    private void AddOtherSale()
    {
        var last = OtherSales.FirstOrDefault();
        var row = new OtherSaleRow { Channel = last?.Channel ?? "", Date = last?.Date ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ItemOptions = _itemNames };
        OtherSales.Insert(0, row);
        SelectedOtherSale = row;
    }

    [RelayCommand]
    private void DeleteOtherSale()
    {
        if (SelectedOtherSale is { } r) OtherSales.Remove(r);
    }

    [RelayCommand]
    private async Task SaveOtherSales()
    {
        var db = main.Services.Db;
        var existing = db.LoadOrders().Where(o => o.IsOther).Select(o => o.OrderId).ToHashSet();
        var used = OtherSales.Select(r => r.OrderId).Where(id => id.Length > 0).ToHashSet();
        var next = existing.Concat(used).Select(id => int.TryParse(id.Replace("M-", ""), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var orders = new List<Order>();
        var items = new List<OrderItem>();
        foreach (var r in OtherSales)
        {
            if (!Input.TryDate(r.Date, out var d))
            { main.Status = $"\"{r.Date}\" isn't a date (use 2026-10-31)."; SelectedOtherSale = r; return; }
            if (r.Item.Trim().Length == 0) { main.Status = "Each sale needs an item."; SelectedOtherSale = r; return; }
            if (r.Quantity < 1) { main.Status = "Quantity must be at least 1."; SelectedOtherSale = r; return; }
            if (r.Price < 0 || r.Shipping < 0 || r.Fees < 0 || r.Postage < 0) { main.Status = "Price, shipping, fees and postage can't be negative."; SelectedOtherSale = r; return; }
            var id = r.OrderId.Length > 0 ? r.OrderId : $"M-{next++}";
            var name = r.Item.Trim();
            var listingId = _listingIdByName.TryGetValue(name, out var lid) ? lid : OtherItem.IdFor(name);
            orders.Add(new Order
            {
                OrderId = id, Source = Order.OtherSource, OrderType = r.Channel.Trim(), SaleDate = d, NumberOfItems = r.Quantity,
                OrderValue = r.Price * r.Quantity, Shipping = r.Shipping, CardProcessingFees = r.Fees, Status = r.Note.Trim(),
                OrderTotal = r.Price * r.Quantity + r.Shipping, OrderNet = r.Price * r.Quantity + r.Shipping - r.Fees,
            });
            items.Add(new OrderItem
            {
                TransactionId = id + "-1", OrderId = id, ListingId = listingId, SaleDate = d, ItemName = name, Quantity = r.Quantity,
                Price = r.Price, ItemTotal = r.Price * r.Quantity, Variations = Variations.Format(Variations.Parse(r.Options)),
            });
        }
        // An item that isn't an Etsy listing is known by its typed name.  When that name is
        // corrected on every sale that used it, its cost rules follow it to the new name
        // instead of being left behind on the old one.
        var before = db.LoadItems().Where(i => existing.Contains(i.OrderId)).ToDictionary(i => i.OrderId, i => i.ListingId);
        var stillUsed = items.Select(i => i.ListingId).ToHashSet();
        var renames = items
            .Where(i => before.TryGetValue(i.OrderId, out var old) && old != i.ListingId
                        && old.StartsWith(OtherItem.IdPrefix, StringComparison.Ordinal) && i.ListingId.StartsWith(OtherItem.IdPrefix, StringComparison.Ordinal)
                        && !stillUsed.Contains(old))
            .GroupBy(i => before[i.OrderId])
            .Where(g => g.Select(i => i.ListingId).Distinct().Count() == 1)       // every sale of it was renamed to the same thing
            .ToDictionary(g => g.Key, g => g.First().ListingId);

        db.DeleteOrders(existing.Except(orders.Select(o => o.OrderId)));
        db.UpsertOrders(orders);
        db.UpsertItems(items);
        foreach (var (o, r) in orders.Zip(OtherSales)) db.SetExtraShippingCost(o.OrderId, r.Postage);
        var moved = db.MoveListingCosts(renames);
        OtherSalesEdit.MarkClean();
        await main.RefreshAsync($"Saved {orders.Count} sale(s) outside Etsy." + (moved > 0 ? $"  The cost rules of {moved} renamed item(s) moved to the new name." : ""));
    }

    private void Apply()
    {
        Orders.Clear();
        foreach (var o in _all.Where(o => Filter.Length == 0 ||
                     o.OrderId.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                     o.Items.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                     o.Status.Contains(Filter, StringComparison.OrdinalIgnoreCase)))
            Orders.Add(o);
        var shown = Orders.Where(o => o.OnStatement).ToList();
        Summary = shown.Count == 0 ? $"{Orders.Count} orders" :
            $"{Orders.Count} orders · {shown.Count(o => o.AdAttributed)} from Offsite Ads · average profit {Money.Format(shown.Average(o => o.Profit))} · lowest {Money.Format(shown.Min(o => o.Profit))}";
    }

    /// <summary>Saves a shipping cost not on the Etsy statement (e.g. a label bought elsewhere).</summary>
    [RelayCommand]
    private async Task SaveExtraShipping()
    {
        if (Selected is null) return;
        if (!Input.TryMoney(ExtraShipping, out var amount) || amount < 0)
        {
            main.Status = "Extra shipping must be an amount like 4.50 (0 to remove it).";
            return;
        }
        var id = Selected.OrderId;
        main.Services.Db.SetExtraShippingCost(id, amount);
        await main.RefreshAsync($"Saved extra shipping {Money.Format(amount)} on order {id}.");
    }
}

/// <summary>Things the user has to look at: unknown statement lines, unmapped sales, personalization, self-test problems.</summary>
public sealed partial class AttentionViewModel(MainViewModel main) : ObservableObject
{
    public sealed class UnknownLineRow
    {
        public string Type { get; init; } = "";
        public string Title { get; init; } = "";
        public int Count { get; init; }
        public decimal Total { get; init; }
    }

    public sealed record UnmappedRow(string ListingId, string ItemName, string Variations, int Units, string Problem);

    public ObservableCollection<UnknownLineRow> UnknownLines { get; } = [];
    public ObservableCollection<UnmappedRow> Unmapped { get; } = [];
    public ObservableCollection<string> SuspectNames { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];
    public string[] Categories { get; } = Category.Assignable;

    [ObservableProperty] private UnknownLineRow? _selectedUnknown;
    [ObservableProperty] private string? _categoryToAssign;
    [ObservableProperty] private UnmappedRow? _selectedUnmapped;
    [ObservableProperty] private string? _selectedSuspect;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasUnmapped;
    [ObservableProperty] private bool _hasUnknown;
    [ObservableProperty] private bool _hasSuspects;
    [ObservableProperty] private bool _hasProblems;
    [ObservableProperty] private bool _allClear;
    public ObservableCollection<string> NoMachineTime { get; } = [];
    [ObservableProperty] private bool _hasNoMachineTime;

    [RelayCommand]
    private void GoToParts()
    {
        main.Products.SelectedSubTab = 1;
        main.SelectedTab = MainViewModel.TabProducts;
    }

    [ObservableProperty] private bool _needsAction;
    [ObservableProperty] private string _headerText = "";

    public void Load(ShopData data, List<MonthlyPnL> months, List<Listing> listings)
    {
        UnknownLines.Clear();
        foreach (var g in data.Statement.Where(s => data.Categorizer.Categorize(s) == Category.Unknown).GroupBy(s => (s.Type, s.Title)))
            UnknownLines.Add(new UnknownLineRow { Type = g.Key.Type, Title = g.Key.Title, Count = g.Count(), Total = g.Sum(s => s.Net) });

        // Sales with no cost: no rule matches, or a rule matches but adds up to $0 (no parts, no fixed cost).
        Unmapped.Clear();
        var other = data.OtherOrderIds();
        var cutoffItems = data.Items.Where(i => data.CostCutoff is null || i.SaleDate <= data.CostCutoff || other.Contains(i.OrderId));
        var noCost = cutoffItems
            .Select(i => (Item: i, Cost: data.Costs.CostOf(i.ListingId, i.Variations, i.Quantity, i.SaleDate)))
            .Where(x => data.Costs.NeedsCost(x.Item.ListingId, x.Cost))
            .ToList();
        foreach (var listing in noCost.GroupBy(x => x.Item.ListingId))
        {
            static string Why(LineCost c) => c.Mapped ? "Its rule adds up to $0 (no parts or fixed cost)" : c.Problem;
            var byOptions = listing.GroupBy(x => x.Item.Variations).ToList();
            // A listing whose sales all differ (buyer-typed text) gets one row, not one per sale.
            if (byOptions.Count > 5)
                Unmapped.Add(new UnmappedRow(listing.Key, listing.First().Item.ItemName, $"({byOptions.Count} different option sets)",
                    listing.Sum(x => x.Item.Quantity), Why(listing.First().Cost)));
            else
                foreach (var g in byOptions)
                    Unmapped.Add(new UnmappedRow(listing.Key, g.First().Item.ItemName, g.Key, g.Sum(x => x.Item.Quantity), Why(g.First().Cost)));
        }
        var sorted = Unmapped.OrderByDescending(u => u.Units).ToList();
        Unmapped.Clear();
        foreach (var u in sorted) Unmapped.Add(u);

        // Parts that sold items use but that have no print time: their electricity/wear cost
        // and profit per print hour are wrong until it's entered.
        NoMachineTime.Clear();
        var model = data.Costs;
        var soldListings = data.Items.Select(i => i.ListingId).ToHashSet();
        var usedPartIds = model.Rules.Where(r => soldListings.Contains(r.ListingId)).SelectMany(r => r.Parts).Select(p => p.PartId).ToHashSet();
        // Only for shops that track machine time at all: a candle or a scarf has none, and that is not a problem.
        foreach (var p in model.Parts.Where(p => main.Services.Settings.TrackMachineTime && usedPartIds.Contains(p.Id) && p.Versions.Count > 0)
                     .Where(p => Effective.Latest(p.Versions, v => v.EffectiveFrom).Lines.Any(l => l.Amount > 0 && model.Materials.FirstOrDefault(m => m.Id == l.MaterialId) is { IsComponent: false }))
                     .Where(p => Effective.Latest(p.Versions, v => v.EffectiveFrom).MachineMinutes == 0).OrderBy(p => p.Name))
            NoMachineTime.Add(p.Name);
        HasNoMachineTime = NoMachineTime.Count > 0;

        SuspectNames.Clear();
        foreach (var n in Variations.SuspectFreeText(data.Items, listings, main.Services.Settings.KeptVariationNames)) SuspectNames.Add(n);

        Problems.Clear();
        foreach (var i in months.SelectMany(m => m.Issues).Where(i => !i.Message.Contains("no cost rule")))
            Problems.Add($"{i.Month}   {(i.IsError ? "Problem:" : "Note:")} {i.Message}");

        HasUnmapped = Unmapped.Count > 0;
        HasUnknown = UnknownLines.Count > 0;
        HasSuspects = SuspectNames.Count > 0;
        HasProblems = Problems.Count > 0;
        AllClear = !(HasUnmapped || HasUnknown || HasSuspects || HasProblems || HasNoMachineTime);

        // The red header button: only for things the user has to act on.  Self-test notes
        // (warnings) are listed at the bottom of Home but don't light it up; failures do.
        // Counted by kind (sales without a cost, parts without print time, unknown line types,
        // personalization, failed self-test), not row by row.
        var actions = (Unmapped.Count > 0 ? 1 : 0) + (NoMachineTime.Count > 0 ? 1 : 0) + (UnknownLines.Count > 0 ? 1 : 0)
                      + (SuspectNames.Count > 0 ? 1 : 0) + (months.Any(m => !m.SelfTestPassed) ? 1 : 0);
        NeedsAction = actions > 0;
        HeaderText = actions == 1 ? "1 thing needs attention" : $"{actions} things need attention";

        var parts = new List<string>();
        if (UnknownLines.Count > 0) parts.Add($"{UnknownLines.Count} unknown statement line type(s)");
        if (Unmapped.Count > 0) parts.Add($"{Unmapped.Sum(u => u.Units)} sold unit(s) with no cost");
        if (NoMachineTime.Count > 0) parts.Add($"{NoMachineTime.Count} part(s) with no machine time");
        if (SuspectNames.Count > 0) parts.Add($"{SuspectNames.Count} possible personalization field(s)");
        if (Problems.Count > 0) parts.Add($"{Problems.Count} self-test note(s)");
        Summary = parts.Count == 0 ? "Nothing needs attention." : char.ToUpperInvariant(parts[0][0]) + string.Join(" · ", parts)[1..];
    }

    /// <summary>
    /// The text a line's title must contain for the new category rule.  Starts as the whole
    /// title; shorten it ("Fee for sale made through" …) so one rule covers every line of that kind.
    /// </summary>
    [ObservableProperty] private string _titleContains = "";
    partial void OnSelectedUnknownChanged(UnknownLineRow? value) => TitleContains = value?.Title ?? "";

    [RelayCommand]
    private async Task AssignCategory()
    {
        if (SelectedUnknown is null || CategoryToAssign is null) { main.Status = "Pick a line and a category first."; return; }
        var text = TitleContains.Trim();
        if (text.Length == 0 || !SelectedUnknown.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
        { main.Status = "The text to match must be part of the selected line's title."; return; }
        CategoryRulesStore.Add(main.Services.Paths, SelectedUnknown.Type, text, CategoryToAssign);
        await main.RefreshAsync($"{SelectedUnknown.Type} lines whose title contains \"{text}\" now count as {CategoryToAssign}.");
    }

    [RelayCommand]
    private void GoToRules()
    {
        if (SelectedUnmapped is not null) main.GoToRulesFor(SelectedUnmapped.ListingId);
    }

    [RelayCommand]
    private async Task DropSuspect()
    {
        if (SelectedSuspect is not { } name) return;
        if (!await Dialogs.Confirm("Remove personalization",
                $"Remove \"{name}\" from every stored sale, and skip it on future imports?\n\nUse this for free text the buyer typed (names, messages). It can't be undone, but re-importing your order files brings it back if you change your mind.",
                "Remove it")) return;
        var s = main.Services.Settings;
        if (!s.IgnoredVariationNames.Contains(name, StringComparer.OrdinalIgnoreCase)) s.IgnoredVariationNames.Add(name);
        main.Services.SaveSettings();
        var n = await Task.Run(() => main.Services.Db.StripVariationName(name));
        await main.RefreshAsync($"Removed \"{name}\" from {n} sale line(s).");
    }

    [RelayCommand]
    private async Task KeepSuspect()
    {
        if (SelectedSuspect is not { } name) return;
        var kept = main.Services.Settings.KeptVariationNames;
        if (!kept.Contains(name, StringComparer.OrdinalIgnoreCase)) kept.Add(name);
        main.Services.SaveSettings();
        await main.RefreshAsync($"\"{name}\" is a real option; it won't be flagged again.");
    }
}
