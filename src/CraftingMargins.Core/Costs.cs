// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

// The user-maintained part of the app: materials, parts, shop-wide costs, and the
// rules that turn a sold item line into a cost.  Nothing here is specific to one craft:
// a material is anything bought and used up (filament by the gram, wax by the ounce, yarn
// by the yard, magnets and wicks by the piece), and a part is a recipe of materials.

/// <summary>The units a material can be bought and used in.  One unit per material; the app doesn't convert between them.</summary>
public static class Units
{
    /// <summary>Counted by the piece: magnets, wicks, jars, zippers.  A material in this unit is a "component".</summary>
    public const string Each = "each";
    public const string Default = "g";

    public static readonly string[] All = ["g", "kg", "oz", "lb", "ml", "l", "fl oz", "yd", "m", "ft", "in", Each];
}

/// <summary>What sort of things the shop makes.  It only sets starting points (units, categories, hints); nothing is locked to it.</summary>
public enum Craft { Printing3D, CandlesAndSoap, YarnAndFiber, Other }

public static class Crafts
{
    public static readonly (Craft Craft, string Name)[] All =
    [
        (Craft.Printing3D, "3D printing"),
        (Craft.CandlesAndSoap, "Candles, soap and other poured or mixed goods"),
        (Craft.YarnAndFiber, "Yarn, fabric and fiber"),
        (Craft.Other, "Something else"),
    ];

    /// <summary>The unit a new material starts in.</summary>
    public static string DefaultUnit(Craft c) => c switch { Craft.CandlesAndSoap => "oz", Craft.YarnAndFiber => "yd", _ => "g" };

    /// <summary>Whether products usually take machine time worth costing (a printer, laser or kiln running).</summary>
    public static bool UsesMachineTime(Craft c) => c == Craft.Printing3D;

    /// <summary>The name of the main-materials expense category.</summary>
    public static string MaterialsCategory(Craft c) => c switch
    {
        Craft.Printing3D => "Filament",
        Craft.CandlesAndSoap => "Wax, oils and fragrance",
        Craft.YarnAndFiber => "Yarn and fabric",
        _ => "Materials",
    };

    public static string ComponentsCategory(Craft c) => c switch
    {
        Craft.Printing3D => "Hardware",
        Craft.CandlesAndSoap => "Wicks, jars and lids",
        Craft.YarnAndFiber => "Notions (buttons, zippers, labels)",
        _ => "Components",
    };

    /// <summary>Examples for the hint above the Materials grid.</summary>
    public static string MaterialExamples(Craft c) => c switch
    {
        Craft.Printing3D => "PLA at $0.02 per g; magnets at $0.17 each",
        Craft.CandlesAndSoap => "soy wax at $0.19 per oz; fragrance oil at $1.60 per oz; wicks at $0.12 each; jars at $1.35 each",
        Craft.YarnAndFiber => "merino yarn at $0.11 per yd; buttons at $0.40 each",
        _ => "oak at $0.90 per ft; hinges at $1.10 each",
    };

    /// <summary>Examples for the hint above the Parts grid.</summary>
    public static string PartExamples(Craft c) => c switch
    {
        Craft.Printing3D => "a printed piece (120 g of PLA, 4 magnets, 3 hours of printer time)",
        Craft.CandlesAndSoap => "an 8 oz candle (7 oz of wax, 0.6 oz of fragrance, 1 wick, 1 jar)",
        Craft.YarnAndFiber => "a hat (180 yd of yarn, 1 label)",
        _ => "one thing you make, or a piece that several of your products share",
    };
}

public sealed class Material
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>The unit it is priced and used in (see <see cref="Units"/>).</summary>
    public string Unit { get; set; } = Units.Default;
    public List<MaterialPrice> Prices { get; set; } = [];

    /// <summary>
    /// Bought and used by the piece.  Components get no waste allowance (you don't lose 5% of
    /// a magnet) and are reported on their own line, apart from the main materials.
    /// </summary>
    public bool IsComponent => string.Equals(Unit, Units.Each, StringComparison.OrdinalIgnoreCase);
}

/// <summary>What one unit of a material cost from a date on.</summary>
public sealed record MaterialPrice(DateOnly EffectiveFrom, decimal CostPerUnit);

/// <summary>
/// One thing that gets made, as a recipe: how much of each material goes into it and how
/// long it takes.  A part can be a whole product or a piece several products share.
/// </summary>
public sealed class Part
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public List<PartVersion> Versions { get; set; } = [];
    public string Notes { get; set; } = "";
}

/// <summary>An amount of one material, in that material's own unit.</summary>
public sealed record PartLine(long MaterialId, decimal Amount);

/// <summary>
/// A part's recipe from a date on.  Amounts include whatever is always lost in making it
/// (supports and purge, trimmings).  MachineMinutes = time a machine runs (printer, laser,
/// kiln); OwnMinutes = the maker's own hands-on time.  Either may be zero.
/// </summary>
public sealed record PartVersion(DateOnly EffectiveFrom, List<PartLine> Lines, decimal MachineMinutes = 0, decimal OwnMinutes = 0)
{
    /// <summary>A recipe of one material (the common case, and handy in tests).</summary>
    public static PartVersion Of(DateOnly from, long materialId, decimal amount, decimal machineMinutes = 0, decimal ownMinutes = 0) =>
        new(from, [new PartLine(materialId, amount)], machineMinutes, ownMinutes);
}

/// <summary>Shop-wide per-sale costs in effect from a date.  Defaults are zero.</summary>
public sealed record CostSettings
{
    public DateOnly EffectiveFrom { get; init; } = new(2000, 1, 1);
    /// <summary>What an hour of machine time costs: electricity and wear.</summary>
    public decimal MachineHourRate { get; init; }
    /// <summary>What an hour of the maker's own time is counted at.  Zero = not counted as a cost.</summary>
    public decimal OwnHourRate { get; init; }
    /// <summary>Added to the cost of main materials (not components), e.g. 0.05 = 5%, for failed prints, spills and offcuts.</summary>
    public decimal WastePercent { get; init; }
    public decimal PackagingPerOrder { get; init; }
    public decimal PackagingPerUnit { get; init; }
}

/// <summary>
/// A mapping rule for one listing.  It applies to a sold line when every condition
/// matches (a missing condition or "*" = any value).  All matching rules for the
/// listing add up, so a product can be "body by size" + "extras by option".
/// A rule with only a FixedCost is how a product is costed without listing its materials.
/// </summary>
public sealed class MappingRule
{
    public long Id { get; set; }
    public string ListingId { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string> Conditions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RulePart> Parts { get; set; } = [];
    /// <summary>Fixed cost per unit sold (a bought-in product, or extras like a printed insert).</summary>
    public decimal FixedCost { get; set; }
    public int SortOrder { get; set; }

    public const string Any = "*";

    public bool Matches(IReadOnlyList<KeyValuePair<string, string>> variations)
    {
        foreach (var (name, wanted) in Conditions)
        {
            if (string.IsNullOrWhiteSpace(wanted) || wanted == Any) continue;
            var actual = variations.FirstOrDefault(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase));
            if (actual.Key is null) return false;
            if (!string.Equals(actual.Value.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}

/// <summary>Quantity of a part per unit sold.  If MultiplyByVariation is set, the quantity is also multiplied by the first number in that variation's value ("2 Sets (0-9)" -> 2).</summary>
public sealed record RulePart(long PartId, decimal Quantity, string? MultiplyByVariation = null);

/// <summary>Maps a retired variation value to its current one.  ListingId empty = every listing.</summary>
public sealed class VariationAlias
{
    public long Id { get; set; }
    public string ListingId { get; set; } = "";
    public string VariationName { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
}

/// <summary>
/// Cost of one sold item line.  Mapped = at least one rule matched.
/// Material = main materials; Waste = the allowance on them; Components = things bought by
/// the piece; MachineCost = machine hours × its rate; OwnCost = the maker's hours × their rate.
/// </summary>
public sealed record LineCost(
    bool Mapped,
    decimal Material,
    decimal Waste,
    decimal Components,
    decimal MachineHours,
    decimal MachineCost,
    decimal OwnHours,
    decimal OwnCost,
    decimal Fixed,
    string Problem)
{
    public decimal Total => Material + Waste + Components + MachineCost + OwnCost + Fixed;
    /// <summary>Machine and own time together: the hours this sale took.</summary>
    public decimal MakingHours => MachineHours + OwnHours;
    public static LineCost Unmapped(string why) => new(false, 0, 0, 0, 0, 0, 0, 0, 0, why);
}

/// <summary>Everything the cost calculator needs, loaded once.</summary>
public sealed class CostModel
{
    public List<Material> Materials { get; init; } = [];
    public List<Part> Parts { get; init; } = [];
    public List<MappingRule> Rules { get; init; } = [];
    public List<VariationAlias> Aliases { get; init; } = [];
    public List<CostSettings> CostSettings { get; init; } = [new()];
    /// <summary>True = part recipes in effect at the sale date; false (default) = current recipes for all sales.</summary>
    public bool UseWeightsAtSaleDate { get; init; }

    public CostSettings SettingsAt(DateOnly date) =>
        CostSettings.Count == 0 ? new CostSettings() : Effective.At(CostSettings, s => s.EffectiveFrom, date);

    /// <summary>Packaging per unit for one listing, where it differs from the shop-wide setting.</summary>
    public Dictionary<string, decimal> PackagingByListing { get; init; } = [];

    /// <summary>Listings the user says really cost nothing to make (a digital file, say), so a $0 cost isn't flagged.</summary>
    public HashSet<string> ZeroCostListings { get; init; } = [];

    /// <summary>True when a sold line still needs the user's attention: no rule, or a rule that adds up to $0 on a listing not marked as free to make.</summary>
    public bool NeedsCost(string listingId, LineCost c) => !c.Mapped || (c.Total == 0 && !ZeroCostListings.Contains(listingId));

    /// <summary>Packaging per unit sold: the listing's own amount if set, else the shop-wide one.</summary>
    public decimal PackagingPerUnit(string listingId, DateOnly date) =>
        PackagingByListing.TryGetValue(listingId, out var p) ? p : SettingsAt(date).PackagingPerUnit;

    /// <summary>Applies aliases (retired value -> current value).</summary>
    public List<KeyValuePair<string, string>> ApplyAliases(string listingId, IEnumerable<KeyValuePair<string, string>> variations)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var v in variations)
        {
            var alias = Aliases.FirstOrDefault(a =>
                (a.ListingId.Length == 0 || a.ListingId == listingId) &&
                string.Equals(a.VariationName, v.Key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.OldValue.Trim(), v.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            result.Add(alias is null ? v : new(v.Key, alias.NewValue));
        }
        return result;
    }

    public bool IsRetired(string listingId, string variationName, string value) =>
        Aliases.Any(a => (a.ListingId.Length == 0 || a.ListingId == listingId) &&
                         string.Equals(a.VariationName, variationName, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(a.OldValue.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The recipe of a part to use for a sale on a date (see <see cref="UseWeightsAtSaleDate"/>).</summary>
    public PartVersion VersionFor(Part part, DateOnly saleDate) =>
        UseWeightsAtSaleDate
            ? Effective.At(part.Versions, x => x.EffectiveFrom, saleDate)
            : Effective.Latest(part.Versions, x => x.EffectiveFrom);

    /// <summary>What <paramref name="count"/> of a part cost on a date.  Adds to the running totals; problems are collected.</summary>
    private void AddPart(Part part, decimal count, DateOnly date, Totals t, List<string> problems)
    {
        var pv = VersionFor(part, date);
        foreach (var line in pv.Lines)
        {
            if (line.Amount == 0) continue;
            var mat = Materials.FirstOrDefault(m => m.Id == line.MaterialId);
            if (mat is null) { problems.Add($"Part \"{part.Name}\" uses a material that no longer exists"); continue; }
            if (mat.Prices.Count == 0) { problems.Add($"\"{mat.Name}\" (used by part \"{part.Name}\") has no price"); continue; }
            var cost = count * line.Amount * Effective.At(mat.Prices, p => p.EffectiveFrom, date).CostPerUnit;
            if (mat.IsComponent) t.Components += cost; else t.Material += cost;
        }
        t.MachineMinutes += count * pv.MachineMinutes;
        t.OwnMinutes += count * pv.OwnMinutes;
    }

    private sealed class Totals { public decimal Material, Components, MachineMinutes, OwnMinutes; }

    /// <summary>
    /// Cost of one sold line (all units).  Never defaults: if no rule matches, the line is Unmapped.
    /// Packaging is per order and is added by the order report, not here.
    /// </summary>
    public LineCost CostOf(string listingId, string variationsText, int quantity, DateOnly saleDate)
    {
        var vars = ApplyAliases(listingId, Variations.Parse(variationsText));
        var matching = Rules.Where(r => r.ListingId == listingId).OrderBy(r => r.SortOrder).Where(r => r.Matches(vars)).ToList();
        if (matching.Count == 0)
            return LineCost.Unmapped(Rules.Any(r => r.ListingId == listingId)
                ? "No rule matches these variations"
                : "No rules for this listing");

        var t = new Totals();
        decimal fixedCost = 0;
        var problems = new List<string>();

        foreach (var rule in matching)
        {
            fixedCost += rule.FixedCost * quantity;
            foreach (var rp in rule.Parts)
            {
                var part = Parts.FirstOrDefault(p => p.Id == rp.PartId);
                if (part is null || part.Versions.Count == 0) { problems.Add($"Rule \"{rule.Name}\" uses a missing part"); continue; }

                decimal multiplier = 1;
                if (!string.IsNullOrEmpty(rp.MultiplyByVariation))
                {
                    var v = vars.FirstOrDefault(x => string.Equals(x.Key, rp.MultiplyByVariation, StringComparison.OrdinalIgnoreCase));
                    var n = EtsyText.FirstInteger(v.Value);
                    if (n is null) { problems.Add($"No number in variation \"{rp.MultiplyByVariation}\""); continue; }
                    multiplier = n.Value;
                }
                AddPart(part, rp.Quantity * multiplier * quantity, saleDate, t, problems);
            }
        }
        return Finish(t, fixedCost, SettingsAt(saleDate), problems);
    }

    /// <summary>Cost of a hand-picked list of parts (price calculator), with today's or a given date's prices.</summary>
    public LineCost CostOfParts(IEnumerable<(long PartId, decimal Count)> parts, DateOnly date)
    {
        var t = new Totals();
        var problems = new List<string>();
        foreach (var (id, count) in parts)
        {
            var part = Parts.FirstOrDefault(p => p.Id == id);
            if (part is null || part.Versions.Count == 0) { problems.Add("A part is missing"); continue; }
            AddPart(part, count, date, t, problems);
        }
        return Finish(t, 0, SettingsAt(date), problems);
    }

    private static LineCost Finish(Totals t, decimal fixedCost, CostSettings settings, List<string> problems)
    {
        var machineHours = t.MachineMinutes / 60m;
        var ownHours = t.OwnMinutes / 60m;
        return new LineCost(problems.Count == 0, t.Material, t.Material * settings.WastePercent, t.Components,
            machineHours, machineHours * settings.MachineHourRate, ownHours, ownHours * settings.OwnHourRate,
            fixedCost, string.Join("; ", problems));
    }
}
