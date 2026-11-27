// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftingMargins.Core;
using CraftingMargins.Data;

namespace CraftingMargins.App.ViewModels;

// Phase 3 screens: Reports, Pricing (check + planned sale), Tax & expenses.

/// <summary>"All years" or one year, for the report pickers.</summary>
public static class YearChoice
{
    public const string All = "All years";
    public static Period ToPeriod(string? choice) =>
        int.TryParse(choice, out var y) ? Period.Year(y) : Period.All;

    public static List<string> From(ShopData data, bool includeAll)
    {
        var years = data.Orders.Select(o => o.SaleDate.Year).Concat(data.Statement.Select(s => s.Date.Year))
            .Append(DateTime.Today.Year).Distinct().OrderByDescending(y => y).Select(y => y.ToString()).ToList();
        if (includeAll) years.Add(All);
        return years;
    }
}

// ============================================================== Reports

public sealed partial class ReportsViewModel(MainViewModel main) : ObservableObject
{
    private ShopData _data = new();
    private List<OrderProfit> _orders = [];
    private List<MonthlyPnL> _months = [];
    private List<UnmatchedLabel> _unmatchedLabels = [];
    private bool _loading;

    public ObservableCollection<string> Years { get; } = [];
    [ObservableProperty] private string? _selectedYear;
    [ObservableProperty] private int _selectedSubTab;
    public ObservableCollection<YearRow> YearRows { get; } = [];
    [ObservableProperty] private IReadOnlyList<ChartBar> _productBars = [];
    [ObservableProperty] private double _productChartHeight = 60;
    [ObservableProperty] private IReadOnlyList<ChartBar> _yearBars = [];
    [ObservableProperty] private IReadOnlyList<ChartBar> _yearCompareBars = [];
    [ObservableProperty] private string _yearBarsLabel = "";
    [ObservableProperty] private string _yearCompareLabel = "";
    [ObservableProperty] private string _yearSummary = "";

    public ObservableCollection<ProductSales> Products { get; } = [];
    public ObservableCollection<AdRow> AdRows { get; } = [];
    public ObservableCollection<ShippingRow> ShippingRows { get; } = [];
    public ObservableCollection<OrderProfit> LostOnShipping { get; } = [];
    public ObservableCollection<OrderProfit> Discounted { get; } = [];
    [ObservableProperty] private string _productSummary = "";
    [ObservableProperty] private string _adSummary = "";
    [ObservableProperty] private string _shippingSummary = "";
    [ObservableProperty] private string _discountSummary = "";

    partial void OnSelectedYearChanged(string? value) { if (!_loading) Apply(); }

    public void Load(ShopData data, List<OrderProfit> orders, List<MonthlyPnL> months)
    {
        _data = data;
        _orders = orders;
        _months = months;
        _unmatchedLabels = OrderReport.AllocateLabelsDetailed(data).Unmatched;
        _loading = true;
        var keep = SelectedYear;
        Years.Clear();
        foreach (var y in YearChoice.From(data, includeAll: true)) Years.Add(y);
        SelectedYear = keep is not null && Years.Contains(keep) ? keep : Years.FirstOrDefault();
        _loading = false;
        Apply();
    }

    private void Apply()
    {
        var period = YearChoice.ToPeriod(SelectedYear);

        Products.Clear();
        var products = ProductReport.Build(_data, _orders, period);
        foreach (var p in products) Products.Add(p);
        // The ten most profitable listings as bars (the grid below has them all).
        ProductBars = [.. products.Where(p => p.OrdersWithProfit > 0).Take(10).Select(p => new ChartBar(p.Name, Money.Round(p.Profit)))];
        ProductChartHeight = Math.Max(48, ProductBars.Count * 19);   // compact, so the grid below keeps its room
        ProductSummary = products.Count == 0 ? "No sales in this period." :
            $"{products.Count} listings sold.  Best: {products[0].Name} ({Money.Format(products[0].Profit)}).  " +
            "Orders with several listings share their profit by item price.  Profit counts only orders whose money is on a loaded statement.";

        AdRows.Clear();
        var ads = AdReport.Build(_data, _orders, period);
        foreach (var a in ads) AdRows.Add(a);
        var t = ads[^1];
        AdSummary = t.Orders == 0 ? "No orders on a loaded statement in this period." :
            $"{t.AdShare:0%} of orders came through Offsite Ads.  Average profit {Fmt(t.AdAverage)} on an ad sale vs {Fmt(t.OrganicAverage)} organic.  " +
            $"Offsite Ads cost {Money.Format(-t.OffsiteAdsPaid)}" + (t.EtsyAdsPaid != 0 ? $", Etsy Ads {Money.Format(-t.EtsyAdsPaid)}." : ".");

        ShippingRows.Clear();
        var ship = ShippingReport.Build(_data, period, _unmatchedLabels);
        foreach (var s in ship) ShippingRows.Add(s);
        LostOnShipping.Clear();
        foreach (var o in ShippingReport.LostOnShipping(_orders, period)) LostOnShipping.Add(o);
        var st = ship[^1];
        ShippingSummary = $"Buyers paid {Money.Format(st.Charged)} for shipping; labels and adjustments cost {Money.Format(-(st.Labels + st.Adjustments + st.ExtraShipping))}, " +
                          (st.Net >= 0 ? $"so shipping made {Money.Format(st.Net)}." : $"so shipping lost {Money.Format(-st.Net)}.") +
                          $"  {LostOnShipping.Count} order(s) had a label that cost more than the buyer paid." +
                          (st.Unmatched != 0 ? $"  {Money.Format(-st.Unmatched)} of labels and adjustments couldn't be matched to an order, so no order's profit includes it (the Monthly P&L does)." : "");

        YearRows.Clear();
        var yoyYear = int.TryParse(SelectedYear, out var sy) ? sy : DateTime.Today.Year;
        var yoy = YearOverYear.Build(_months, yoyYear);
        foreach (var y in yoy) YearRows.Add(y);
        var monthsOnly = yoy.Where(r => !r.IsTotal).ToList();
        YearBars = [.. monthsOnly.Select(r => new ChartBar(r.Label[..3], Money.Round(r.Profit)))];
        YearCompareBars = [.. monthsOnly.Select(r => new ChartBar(r.Label[..3], Money.Round(r.LastProfit)))];
        YearBarsLabel = $"{yoyYear} profit";
        YearCompareLabel = $"{yoyYear - 1}";
        var yt = yoy[^1];
        var soFar = yoyYear < DateTime.Today.Year ? $"{yoyYear}" : $"{yoyYear} so far";
        YearSummary = yt.Sales == 0 && yt.LastSales == 0 ? $"No statements for {yoyYear} or {yoyYear - 1}." :
            yoy.All(r => r.LastSales == 0 && r.LastProfit == 0) ? $"Nothing from {yoyYear - 1} to compare with yet; import last year's statements to see the change. {soFar}: sales {Money.Format(yt.Sales)}, profit {Money.Format(yt.Profit)}." :
            $"{soFar}: sales {Money.Format(yt.Sales)} vs {Money.Format(yt.LastSales)} in the same months of {yoyYear - 1}" +
            (yt.SalesChange is { } ch ? $" ({ch:+0%;-0%})" : "") + $", profit {Money.Format(yt.Profit)} vs {Money.Format(yt.LastProfit)}.";

        Discounted.Clear();
        var disc = DiscountReport.Build(_orders, period);
        foreach (var o in disc) Discounted.Add(o);
        var known = disc.Where(o => o.OnStatement).ToList();
        var others = _orders.Where(o => o.OnStatement && period.Contains(o.SaleDate) && !DiscountReport.IsDiscounted(o)).ToList();
        DiscountSummary = disc.Count == 0 ? "No discounted orders in this period." :
            $"{disc.Count} discounted order(s), {Money.Format(disc.Sum(o => o.Discount + o.ShippingDiscount))} off in total." +
            (known.Count > 0 ? $"  Average profit {Money.Format(Money.Round(known.Average(o => o.Profit)))}" : "") +
            (others.Count > 0 ? $" vs {Money.Format(Money.Round(others.Average(o => o.Profit)))} without a discount." : ".");
    }

    private static string Fmt(decimal? v) => v is { } d ? Money.Format(d) : "—";

    [RelayCommand]
    private async Task Export()
    {
        var name = SelectedYear == YearChoice.All ? "All Years" : SelectedYear;
        var path = await Dialogs.SaveFile("Export reports", $"Crafting Margins Reports {name}.xlsx", "Excel workbook", "xlsx", main.Services.Settings.LastExportFolder);
        if (path is null) return;
        try
        {
            Data.Export.WriteXlsx(path,
            [
                ReportSheets.Products(Products),
                AdSheet(), ShippingSheet(),
                ReportSheets.Orders(Discounted, "Discounted orders"),
                ReportSheets.Orders(LostOnShipping, "Labels over shipping"),
            ]);
            main.Services.Settings.LastExportFolder = Path.GetDirectoryName(path) ?? "";
            main.Services.SaveSettings();
            main.Status = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { main.Status = "Couldn't save the file (is it open in Excel, or the folder read-only?): " + ex.Message; }
    }

    private Sheet AdSheet()
    {
        var s = new Sheet("Ads vs organic");
        s.Add("Month", "Orders", "From Offsite Ads", "Ad share", "Average profit (ad)", "Average profit (organic)", "Offsite Ads paid", "Etsy Ads paid");
        foreach (var a in AdRows)
        {
            object?[] row = [a.Label, a.Orders, a.AdOrders, $"{a.AdShare:0%}", a.AdAverage, a.OrganicAverage, a.OffsiteAdsPaid, a.EtsyAdsPaid];
            if (a.IsTotal) s.AddBold(row); else s.Add(row);
        }
        return s;
    }

    private Sheet ShippingSheet()
    {
        var s = new Sheet("Shipping");
        s.Add("Month", "Orders", "Shipping charged", "Labels", "Label adjustments", "Shipping paid outside Etsy", "Net", "Labels not matched to an order");
        foreach (var r in ShippingRows)
        {
            object?[] row = [r.Label, r.Orders, r.Charged, r.Labels, r.Adjustments, r.ExtraShipping, r.Net, r.Unmatched];
            if (r.IsTotal) s.AddBold(row); else s.Add(row);
        }
        return s;
    }
}

// ============================================================== Pricing

public sealed partial class SaleListingRow : ObservableObject
{
    public string ListingId { get; init; } = "";
    public string Title { get; init; } = "";
    public int Options { get; init; }
    [ObservableProperty] private bool _isSelected = true;
}

/// <summary>A part picked in the price calculator.</summary>
public sealed partial class CalcPartRow : ObservableObject
{
    public IReadOnlyList<string> PartOptions { get; init; } = [];
    [ObservableProperty] private string? _part;
    [ObservableProperty] private decimal _count = 1;
}

/// <summary>One line of the calculator's result.</summary>
public sealed record CalcLine(string Name, decimal Amount, bool Bold = false);

public sealed partial class PricingViewModel(MainViewModel main) : ObservableObject
{
    // ----- Price calculator (a product that may not be listed yet) -----
    [ObservableProperty] private decimal _calcPrice = 20m;
    [ObservableProperty] private decimal _calcShipping;
    [ObservableProperty] private string _calcLabel = "";
    [ObservableProperty] private decimal _calcExtraCost;
    [ObservableProperty] private bool _calcPackaging = true;
    [ObservableProperty] private string _calcSummary = "";
    [ObservableProperty] private string _calcCostText = "";
    [ObservableProperty] private bool _calcBelowGoal;
    [ObservableProperty] private CalcPartRow? _selectedCalcPart;
    public ObservableCollection<CalcPartRow> CalcParts { get; } = [];
    public ObservableCollection<CalcLine> CalcOrganic { get; } = [];
    public ObservableCollection<CalcLine> CalcWorst { get; } = [];
    private List<string> _partNames = [];

    partial void OnCalcPriceChanged(decimal value) => Calculate();
    partial void OnCalcShippingChanged(decimal value) => Calculate();
    partial void OnCalcLabelChanged(string value) => Calculate();
    partial void OnCalcExtraCostChanged(decimal value) => Calculate();
    partial void OnCalcPackagingChanged(bool value) => Calculate();

    [RelayCommand]
    private void AddCalcPart()
    {
        var row = new CalcPartRow { PartOptions = _partNames, Part = _partNames.FirstOrDefault() };
        row.PropertyChanged += (_, _) => Calculate();
        CalcParts.Add(row);
        Calculate();
    }

    [RelayCommand]
    private void RemoveCalcPart()
    {
        if (SelectedCalcPart is { } r) { CalcParts.Remove(r); Calculate(); }
    }

    private void Calculate()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var model = _data.Costs;
        var picked = CalcParts.Where(p => p.Part is not null && p.Count > 0)
            .Select(p => (model.Parts.FirstOrDefault(x => x.Name == p.Part)?.Id ?? -1, p.Count)).Where(p => p.Item1 > 0).ToList();
        var parts = model.CostOfParts(picked, today);
        var s = model.SettingsAt(today);
        var packaging = CalcPackaging ? s.PackagingPerOrder + s.PackagingPerUnit : 0;
        var cost = Money.Round(parts.Total + CalcExtraCost + packaging);
        decimal? label = Input.TryMoney(CalcLabel, out var l) && l >= 0 ? l : null;
        var goal = main.Services.Settings.ProfitFloor;
        var q = PriceCalculator.Quote(_data.FeesAt(today), CalcPrice, CalcShipping, label, cost, goal, parts.MakingHours);

        CalcCostText = $"Cost per unit {Money.Format(cost)}" +
            (picked.Count > 0 ? $"  (parts {Money.Format(Money.Round(parts.Total))}, {parts.MakingHours:0.##} making hours)" : "") +
            (packaging > 0 ? $"  incl. packaging {Money.Format(packaging)}" : "") +
            (parts.Problem.Length > 0 ? $".  {parts.Problem}" : "");

        CalcOrganic.Clear();
        CalcOrganic.Add(new("Price", q.Price));
        CalcOrganic.Add(new("Shipping the buyer pays", q.Shipping));
        CalcOrganic.Add(new("Shipping label", -q.Label));
        CalcOrganic.Add(new("Transaction fee", q.TransactionFee));
        CalcOrganic.Add(new("Processing fee", q.ProcessingFee));
        CalcOrganic.Add(new("Listing fee", q.ListingFee));
        CalcOrganic.Add(new("Your cost", -q.Cost));
        CalcOrganic.Add(new("Profit, normal sale", Money.Round(q.Organic), true));
        CalcWorst.Clear();
        CalcWorst.Add(new("Profit, normal sale", Money.Round(q.Organic)));
        CalcWorst.Add(new("Offsite Ads fee", q.OffsiteAdsFee));
        CalcWorst.Add(new("Profit, Offsite Ads sale", Money.Round(q.WorstCase), true));

        CalcBelowGoal = q.WorstCase < goal;
        CalcSummary = (CalcBelowGoal
                ? $"Below your goal of {Money.Format(goal)} on an Offsite Ads sale. Charge at least {Money.Format(q.MinPrice)}."
                : $"Meets your goal of {Money.Format(goal)} even on an Offsite Ads sale. The lowest price that still does: {Money.Format(q.MinPrice)}.")
            + (q.OrganicPerHour is { } h ? $"  {Money.Format(h)} per making hour." : "");
    }

    private void LoadCalculator()
    {
        _partNames = [.. _data.Costs.Parts.Select(p => p.Name).OrderBy(n => n)];
        foreach (var r in CalcParts) if (r.Part is not null && !_partNames.Contains(r.Part)) r.Part = null;
        Calculate();
    }

    private List<PriceRow> _all = [];
    private ShopData _data = new();
    private List<Listing> _listings = [];
    private List<OrderProfit> _orders = [];
    private bool _loading;

    [ObservableProperty] private int _selectedSubTab;
    public ObservableCollection<PriceRow> Rows { get; } = [];
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyProblems;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private bool _pricesMayBeStale;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(CreatePartForOptionCommand)), NotifyPropertyChangedFor(nameof(CreatePartHeader))]
    private PriceRow? _selected;
    public int BelowFloorCount { get; private set; }

    /// <summary>The right-click menu's wording: only an option with no cost rule at all can have a part made for it.</summary>
    public string CreatePartHeader => Selected switch
    {
        null or { HasNoRule: true } => "Create a part for this option…",
        { Cost: not null } => "Create a part for this option (this option already has a cost)",
        _ => "Create a part for this option (it has a rule; see Status)",
    };

    private bool CanCreatePart() => Selected is { HasNoRule: true };

    /// <summary>Makes an empty part and the rule that links this option to it, then opens the part to fill in.</summary>
    [RelayCommand(CanExecute = nameof(CanCreatePart))]
    private async Task CreatePartForOption()
    {
        if (Selected is { HasNoRule: true } row) await main.Products.CreatePartForOption(row);
    }
    /// <summary>Every row of the pricing check (for the suggestions on Home).</summary>
    public IReadOnlyList<PriceRow> AllRows => _all;

    // Planned sale preview
    public ObservableCollection<SaleListingRow> SaleListings { get; } = [];
    public ObservableCollection<PriceRow> SaleRows { get; } = [];
    [ObservableProperty] private decimal _salePercent = 20m;
    [ObservableProperty] private string _saleSummary = "";

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyProblemsChanged(bool value) => ApplyFilter();
    partial void OnSalePercentChanged(decimal value) { if (!_loading) BuildSale(); }

    /// <param name="rows">The pricing check, already worked out in the background.</param>
    public void Load(ShopData data, List<Listing> listings, List<OrderProfit> orders, List<PriceRow> rows)
    {
        _data = data;
        _listings = listings;
        _orders = orders;
        _all = rows;
        BelowFloorCount = _all.Count(r => r.BelowFloor);
        ApplyFilter();

        var apiListings = listings.Count(l => l.Offerings.Count > 0);
        var synced = main.Services.Settings.LastApiSync;
        SourceText = main.Services.IsDemo ? "Demo shop: every option of every listing at its listed price."
            : apiListings > 0
            ? $"Prices from Etsy{(synced is DateTime st ? $" (synced {st:MMM d, h:mm tt})" : "")} for {apiListings} listing(s); other listings use the price of their most recent sale."
            : listings.Count > 0
                ? "Prices are from each option's most recent sale, so options you haven't sold yet aren't listed. Connect the Etsy API to check every option at today's price."
                : "Import your listings file (or connect the Etsy API) so only listings that are still for sale are checked. Until then every option you've sold is listed, at its most recent sale price.";
        PricesMayBeStale = !main.Services.IsDemo && apiListings > 0 && (synced is null || DateTime.Now - synced > TimeSpan.FromHours(6));

        // Planned sale: keep the user's ticks across refreshes.
        _loading = true;
        var unticked = SaleListings.Where(s => !s.IsSelected).Select(s => s.ListingId).ToHashSet();
        foreach (var s in SaleListings) s.PropertyChanged -= SaleTickChanged;
        SaleListings.Clear();
        foreach (var g in _all.GroupBy(r => r.ListingId).OrderBy(g => g.First().Title, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SaleListingRow { ListingId = g.Key, Title = g.First().Title, Options = g.Count(), IsSelected = !unticked.Contains(g.Key) };
            row.PropertyChanged += SaleTickChanged;
            SaleListings.Add(row);
        }
        _loading = false;
        BuildSale();
        LoadCalculator();
    }

    private void SaleTickChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName == nameof(SaleListingRow.IsSelected)) BuildSale();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var r in _all.Where(r => (!OnlyProblems || r.Status.Length > 0) &&
                                          (Filter.Length == 0 || r.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                                           r.OptionsText.Contains(Filter, StringComparison.OrdinalIgnoreCase))))
            Rows.Add(r);
        var floor = main.Services.Settings.ProfitFloor;
        var unmapped = _all.Count(r => r.Cost is null);
        Summary = _all.Count == 0 ? "Nothing to check yet: import your orders (and listings), or sync with Etsy." :
            $"{Checked(_all)} checked against your goal of {Money.Format(floor)} per sale.  {BelowFloorCount} below it in the worst case (an Offsite Ads sale)" +
            (unmapped > 0 ? $"; {unmapped} have no cost rule yet." : ".");
    }

    /// <summary>"207 options (in 88 rows…)": options that earn the same, such as colors at one price, share a row.</summary>
    private static string Checked(IReadOnlyCollection<PriceRow> rows)
    {
        var combos = rows.Sum(r => r.Combinations);
        return combos == rows.Count ? $"{rows.Count} options" : $"{combos} options (in {rows.Count} rows: options that earn the same share one)";
    }

    private void BuildSale()
    {
        var pct = Math.Clamp(SalePercent, 0, 90) / 100m;
        var chosen = SaleListings.Where(s => s.IsSelected).Select(s => s.ListingId).ToHashSet();
        var rows = PricingCheck.Build(_data, _listings, _orders, new PricingOptions
        {
            Floor = main.Services.Settings.ProfitFloor, DiscountPercent = pct, DiscountedListings = chosen,
        }).Where(r => chosen.Contains(r.ListingId)).ToList();
        SaleRows.Clear();
        foreach (var r in rows) SaleRows.Add(r);
        var below = rows.Count(r => r.BelowFloor);
        SaleSummary = rows.Count == 0 ? "Tick at least one listing." :
            $"At {pct:0%} off, {below} of {Checked(rows)} fall below your goal of {Money.Format(main.Services.Settings.ProfitFloor)} in the worst case." +
            (rows.Any(r => r.Cost is null) ? "  Options without a cost rule aren't counted." : "");
    }

    [RelayCommand] private void SelectAllForSale() => TickAll(true);
    [RelayCommand] private void SelectNoneForSale() => TickAll(false);

    private void TickAll(bool select)
    {
        _loading = true;
        foreach (var s in SaleListings) s.IsSelected = select;
        _loading = false;
        BuildSale();
    }

    [RelayCommand]
    private void GoToRules()
    {
        if (Selected is not null) main.GoToRulesFor(Selected.ListingId);
    }

    [RelayCommand]
    private async Task RefreshPrices()
    {
        if (!main.Services.Etsy.IsConnected) { main.GoToSettings(MainViewModel.SettingsEtsyApi); main.Status = "Connect the Etsy API first (Settings → Etsy API)."; return; }
        await main.EtsyApi.SyncNowCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task Export()
    {
        var sale = SelectedSubTab == 1;
        var path = await Dialogs.SaveFile("Export", sale ? $"Crafting Margins Planned Sale {SalePercent:0}%.xlsx" : "Crafting Margins Pricing Check.xlsx",
            "Excel workbook", "xlsx", main.Services.Settings.LastExportFolder);
        if (path is null) return;
        try
        {
            Data.Export.WriteXlsx(path, [sale ? ReportSheets.Pricing(SaleRows, $"Planned sale {SalePercent:0} pct off") : ReportSheets.Pricing(_all)]);
            main.Services.Settings.LastExportFolder = Path.GetDirectoryName(path) ?? "";
            main.Services.SaveSettings();
            main.Status = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { main.Status = "Couldn't save the file (is it open in Excel, or the folder read-only?): " + ex.Message; }
    }

    /// <summary>Etsy's terms: listing prices shown must be no more than 6 hours old.</summary>
    public async Task OnShown()
    {
        if (PricesMayBeStale && main.Services.Etsy.IsConnected) await main.EtsyApi.AutoSyncIfStale();
    }
}

// ============================================================== Tax & expenses

/// <summary>A tax summary line with a property per month, for the grid.</summary>
public sealed class TaxRow(TaxLine l)
{
    public TaxLine Line { get; } = l;
    public string Section => Line.Section;
    /// <summary>The heading this line is grouped under.</summary>
    public string Group => Line.IsTotal ? "Total" : Line.Section;
    public string Name => Line.Name;
    public string ScheduleCLine => Line.ScheduleCLine;
    public string Note => Line.Note;
    public bool InTotal => Line.InTotal;
    public bool IsTotal => Line.IsTotal;
    public bool Dimmed => !Line.InTotal && !Line.IsTotal;
    public decimal Jan => Line.Month(1); public decimal Feb => Line.Month(2); public decimal Mar => Line.Month(3);
    public decimal Apr => Line.Month(4); public decimal May => Line.Month(5); public decimal Jun => Line.Month(6);
    public decimal Jul => Line.Month(7); public decimal Aug => Line.Month(8); public decimal Sep => Line.Month(9);
    public decimal Oct => Line.Month(10); public decimal Nov => Line.Month(11); public decimal Dec => Line.Month(12);
    public decimal Total => Line.Total;
}

public sealed partial class ExpenseRow : ObservableObject
{
    public long Id { get; init; }
    [ObservableProperty] private string _date = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private string? _category;
    [ObservableProperty] private string _vendor = "";
    [ObservableProperty] private string _note = "";
    public IReadOnlyList<string> CategoryOptions { get; init; } = [];
}

public sealed partial class ExpenseCategoryRow : ObservableObject
{
    /// <summary>The name it was saved under ("" for a new row), so renaming it can carry its expenses along.</summary>
    public string OriginalName { get; init; } = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _scheduleCLine = "";
    [ObservableProperty] private bool _countedPerSale;
    public IReadOnlyList<string> LineOptions { get; } = ScheduleC.Lines;
}

public sealed partial class TaxViewModel : ObservableObject
{
    private readonly MainViewModel main;
    public EditState ExpensesEdit { get; }
    public EditState CategoriesEdit { get; }

    public TaxViewModel(MainViewModel main)
    {
        this.main = main;
        ExpensesEdit = new("expenses", () => EditState.Lines(Expenses.Select(e => EditState.Line(e.Id, e.Date, e.Amount, e.Category, e.Vendor, e.Note))));
        CategoriesEdit = new("expense categories", () => EditState.Lines(Categories.Select(c => EditState.Line(c.OriginalName, c.Name, c.ScheduleCLine, c.CountedPerSale))));
        ExpensesEdit.Watch(Expenses);
        CategoriesEdit.Watch(Categories);
    }

    private List<MonthlyPnL> _months = [];
    private List<OrderProfit> _orders = [];
    private TaxSummary? _summary;
    private bool _loading;

    [ObservableProperty] private int _selectedSubTab;
    public ObservableCollection<string> Years { get; } = [];
    [ObservableProperty] private string? _selectedYear;
    [ObservableProperty] private bool _countAsBought;
    [ObservableProperty] private bool _byScheduleC;
    public ObservableCollection<TaxRow> Lines { get; } = [];
    [ObservableProperty] private Avalonia.Collections.DataGridCollectionView? _linesView;
    [ObservableProperty] private string _tileSales = "";
    [ObservableProperty] private string _tileFees = "";
    [ObservableProperty] private string _tileCosts = "";
    [ObservableProperty] private string _tileProfit = "";
    [ObservableProperty] private bool _tileProfitNegative;
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private string _notes = "";

    public ObservableCollection<ExpenseRow> Expenses { get; } = [];
    [ObservableProperty] private ExpenseRow? _selectedExpense;
    [ObservableProperty] private string _expenseSummary = "";
    public ObservableCollection<ExpenseCategoryRow> Categories { get; } = [];
    [ObservableProperty] private ExpenseCategoryRow? _selectedCategory;

    partial void OnSelectedYearChanged(string? value) { if (!_loading) Build(); }
    partial void OnByScheduleCChanged(bool value) { if (!_loading) Build(); }
    partial void OnCountAsBoughtChanged(bool value)
    {
        if (_loading) return;
        main.Services.Settings.SuppliesBasis = value ? SuppliesBasis.Bought : SuppliesBasis.Used;
        main.Services.SaveSettings();
        Build();
    }

    public void Load(List<MonthlyPnL> months, List<OrderProfit> orders, ShopData data)
    {
        _months = months;
        _orders = orders;
        _loading = true;
        CountAsBought = main.Services.Settings.SuppliesBasis == SuppliesBasis.Bought;
        var keep = SelectedYear;
        Years.Clear();
        var expenseYears = main.Services.Db.LoadExpenses().Select(e => e.Date.Year.ToString());
        foreach (var y in YearChoice.From(data, includeAll: false).Union(expenseYears).OrderByDescending(y => y)) Years.Add(y);
        SelectedYear = keep is not null && Years.Contains(keep) ? keep
            : months.Count > 0 ? months[^1].Month[..4] : Years.FirstOrDefault();
        // A grid with unsaved changes keeps them; the other one still reloads.
        CategoriesEdit.Check();
        ExpensesEdit.Check();
        if (!CategoriesEdit.IsDirty) LoadCategories();
        if (!ExpensesEdit.IsDirty) LoadExpenses();
        _loading = false;
        Build();
    }

    private int Year => int.TryParse(SelectedYear, out var y) ? y : DateTime.Today.Year;

    private void Build()
    {
        var cats = main.Services.Db.LoadExpenseCategories();
        _summary = TaxReport.Build(Year, _months, main.Services.Db.LoadExpenses(), cats, CountAsBought ? SuppliesBasis.Bought : SuppliesBasis.Used);
        Lines.Clear();
        foreach (var l in ByScheduleC ? _summary.ByScheduleC() : _summary.Lines) Lines.Add(new TaxRow(l));
        // Grouped under their section (Sales, Etsy fees, …) unless it's the Schedule C view.
        var view = new Avalonia.Collections.DataGridCollectionView(Lines);
        if (!ByScheduleC) view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(TaxRow.Group)));
        LinesView = view;
        TotalText = $"Net profit for {Year}: {Money.Format(_summary.NetProfit)}";
        decimal Sum(Func<TaxLine, bool> f) => _summary.Lines.Where(l => l.InTotal && !l.IsTotal && f(l)).Sum(l => l.Total);
        TileSales = Money.Format(Sum(l => l.Section == TaxReport.SalesSection));
        TileFees = Money.Format(Sum(l => l.Section == TaxReport.EtsySection));
        TileCosts = Money.Format(Sum(l => l.Section is TaxReport.ShippingSection or TaxReport.UsedSection or TaxReport.BoughtSection));
        TileProfit = Money.Format(_summary.NetProfit);
        TileProfitNegative = _summary.NetProfit < 0;
        Notes = string.Join("  ", _summary.Notes);
        Show1099K();
    }

    // ----- 1099-K check -----
    [ObservableProperty] private string _form1099KText = "";
    [ObservableProperty] private string _check1099KText = "";

    private void Show1099K()
    {
        var saved = main.Services.Settings.Form1099K.TryGetValue(Year.ToString(), out var v) ? v : (decimal?)null;
        Form1099KText = saved is { } s ? s.ToString("0.00", CultureInfo.CurrentCulture) : "";
        var c = TaxReport.Check1099K(Year, _months, saved);
        Check1099KText =
            $"Etsy payments for orders on your {Year} statements: {Money.Format(c.EtsyPayments)} (price, shipping and sales tax, before fees and refunds). " +
            (c.Difference is not { } d ? "Type the gross amount from your 1099-K to compare."
             : Math.Abs(d) < 0.01m ? "It matches your 1099-K exactly."
             : $"Your 1099-K is {Money.Format(Math.Abs(d))} {(d > 0 ? "higher" : "lower")}. Usual reasons: a month's statement isn't imported yet, or orders paid at the very start or end of the year are counted in a different year" +
               (c.Refunds != 0 ? $". Refunds ({Money.Format(-c.Refunds)} this year) are not taken off a 1099-K, and they aren't taken off here either." : "."));
    }

    [RelayCommand]
    private void Save1099K()
    {
        var key = Year.ToString();
        var text = Form1099KText.Trim();
        if (text.Length == 0) main.Services.Settings.Form1099K.Remove(key);
        else if (Input.TryMoney(text, out var v) && v >= 0) main.Services.Settings.Form1099K[key] = v;
        else { main.Status = "Type the 1099-K amount like 2575.75."; return; }
        main.Services.SaveSettings();
        Show1099K();
    }

    private void LoadCategories()
    {
        using var loading = CategoriesEdit.Loading();
        Categories.Clear();
        foreach (var c in main.Services.Db.LoadExpenseCategories())
            Categories.Add(new ExpenseCategoryRow { OriginalName = c.Name, Name = c.Name, ScheduleCLine = c.ScheduleCLine, CountedPerSale = c.CountedPerSale });
    }

    private void LoadExpenses()
    {
        using var loading = ExpensesEdit.Loading();
        var names = main.Services.Db.LoadExpenseCategories().Select(c => c.Name).ToList();
        var all = main.Services.Db.LoadExpenses();
        Expenses.Clear();
        foreach (var e in all.OrderByDescending(e => e.Date))
            Expenses.Add(new ExpenseRow
            {
                Id = e.Id, Date = Input.Date(e.Date), Amount = e.Amount, Category = e.Category,
                Vendor = e.Vendor, Note = e.Note, CategoryOptions = names.Contains(e.Category) || e.Category.Length == 0 ? names : [.. names, e.Category],
            });
        var thisYear = all.Where(e => e.Date.Year == Year).ToList();
        ExpenseSummary = $"{Expenses.Count} expense(s) in total; {thisYear.Count} in {Year} adding up to {Money.Format(thisYear.Sum(e => e.Amount))}.";
    }

    [RelayCommand]
    private void AddExpense()
    {
        var row = new ExpenseRow { CategoryOptions = Categories.Select(c => c.Name).ToList(), Category = SelectedExpense?.Category };
        Expenses.Insert(0, row);
        SelectedExpense = row;
    }

    [RelayCommand]
    private void DeleteExpense()
    {
        if (SelectedExpense is { } e) Expenses.Remove(e);
    }

    [RelayCommand]
    private async Task SaveExpenses()
    {
        var list = new List<Expense>();
        foreach (var r in Expenses)
        {
            if (!Input.TryDate(r.Date, out var d))
            { main.Status = $"\"{r.Date}\" isn't a date (use 2026-10-31)."; SelectedExpense = r; return; }
            // A negative amount is money back: a supplier refund or a return.
            if (r.Amount == 0) { main.Status = "Each expense needs an amount (use a minus sign for a refund from a supplier)."; SelectedExpense = r; return; }
            list.Add(new Expense { Id = r.Id, Date = d, Amount = r.Amount, Category = r.Category ?? "", Vendor = r.Vendor.Trim(), Note = r.Note.Trim() });
        }
        main.Services.Db.SaveExpenses(list);
        ExpensesEdit.MarkClean();
        await main.RefreshAsync($"Saved {list.Count} expense(s).");
    }

    [RelayCommand]
    private void AddCategory()
    {
        var row = new ExpenseCategoryRow { Name = "New category", ScheduleCLine = ScheduleC.Other };
        Categories.Add(row);
        SelectedCategory = row;
    }

    [RelayCommand]
    private void DeleteCategory()
    {
        if (SelectedCategory is { } c) Categories.Remove(c);
    }

    [RelayCommand]
    private async Task SaveCategories()
    {
        var rows = Categories.Where(c => c.Name.Trim().Length > 0).ToList();
        var list = rows.Select(c => new ExpenseCategory(c.Name.Trim(), c.ScheduleCLine ?? "", c.CountedPerSale)).ToList();
        if (list.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) { main.Status = "Two categories have the same name."; return; }

        // A renamed category takes its expenses with it (saved ones, and any being edited in the grid).
        var renames = rows.Where(c => c.OriginalName.Length > 0 && !string.Equals(c.OriginalName, c.Name.Trim(), StringComparison.Ordinal))
            .ToDictionary(c => c.OriginalName, c => c.Name.Trim());
        var moved = 0;
        if (renames.Count > 0)
        {
            var expenses = main.Services.Db.LoadExpenses();
            foreach (var e in expenses)
                if (renames.TryGetValue(e.Category, out var to)) { e.Category = to; moved++; }
            if (moved > 0) main.Services.Db.SaveExpenses(expenses);
            if (ExpensesEdit.IsDirty)
            {
                var names = list.Select(c => c.Name).ToList();
                foreach (var r in Expenses.ToList())
                    if (r.Category is { } cat && renames.TryGetValue(cat, out var to))
                        Expenses[Expenses.IndexOf(r)] = new ExpenseRow { Id = r.Id, Date = r.Date, Amount = r.Amount, Category = to, Vendor = r.Vendor, Note = r.Note, CategoryOptions = names };
            }
        }
        main.Services.Db.SaveExpenseCategories(list);
        CategoriesEdit.MarkClean();
        await main.RefreshAsync("Expense categories saved." + (moved > 0 ? $"  {moved} expense(s) moved to the new name." : ""));
    }

    [RelayCommand]
    private void ResetCategories()
    {
        var saved = Categories.Select(c => c.OriginalName).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Categories.Clear();
        foreach (var c in ScheduleC.DefaultCategories(main.Services.Settings.Craft))
            Categories.Add(new ExpenseCategoryRow { OriginalName = saved.Contains(c.Name) ? c.Name : "", Name = c.Name, ScheduleCLine = c.ScheduleCLine, CountedPerSale = c.CountedPerSale });
        main.Status = "Default categories restored. Press Save categories to keep them.";
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (_summary is null) return;
        var path = await Dialogs.SaveFile("Export the year to Excel", $"Crafting Margins {Year}.xlsx", "Excel workbook", "xlsx", main.Services.Settings.LastExportFolder);
        if (path is null) return;
        Save(path, p => Data.Export.WriteXlsx(p, ReportSheets.YearWorkbook(_summary, _months, _orders, main.Services.Db.LoadExpenses())));
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        if (_summary is null) return;
        var path = await Dialogs.SaveFile("Export the tax summary as CSV", $"Crafting Margins Tax Summary {Year}.csv", "CSV file", "csv", main.Services.Settings.LastExportFolder);
        if (path is null) return;
        Save(path, p => Data.Export.WriteCsv(p, ReportSheets.Tax(_summary, ByScheduleC)));
    }

    private void Save(string path, Action<string> write)
    {
        try
        {
            write(path);
            main.Services.Settings.LastExportFolder = Path.GetDirectoryName(path) ?? "";
            main.Services.SaveSettings();
            main.Status = $"Saved {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { main.Status = "Couldn't save the file (is it open in Excel, or the folder read-only?): " + ex.Message; }
    }
}
