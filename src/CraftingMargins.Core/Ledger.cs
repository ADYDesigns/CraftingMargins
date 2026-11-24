// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
namespace CraftingMargins.Core;

/// <summary>
/// Turns Etsy API ledger entries into statement lines, so every report works the same
/// whether a month came from a statement CSV or the API.
/// </summary>
public static class LedgerLines
{
    public const string ApiSource = "Etsy API";
    /// <summary>Statement Type given to an API line: "API: " + the ledger_type.  Category rules match on it.</summary>
    public const string TypePrefix = "API: ";

    public static string TypeFor(string ledgerType) => TypePrefix + ledgerType;

    public static StatementLine ToStatementLine(LedgerEntry e)
    {
        var line = new StatementLine
        {
            ShopId = e.ShopId,
            Date = e.Date,
            Type = TypeFor(e.LedgerType),
            Title = e.Description,
            Info = e.ReferenceId.Length > 0 ? $"{e.ReferenceType} #{e.ReferenceId}" : e.ReferenceType,
            Currency = e.Currency,
            Amount = e.Amount,
            FeesAndTaxes = 0,
            Net = e.Amount,
            SourceFile = ApiSource,
        };
        // Description text may name an order ("Order #123") the way statement titles do.
        StatementIds.Fill(line);
        var rt = e.ReferenceType.ToLowerInvariant();
        if (e.ReferenceId.Length > 0)
        {
            if (rt.Contains("receipt") || rt == "order") line.OrderId ??= e.ReferenceId;
            if (rt.Contains("listing")) line.ListingId ??= e.ReferenceId;
            if (rt.Contains("label") || rt.Contains("shipping")) line.LabelId ??= e.ReferenceId;
        }
        return line;
    }
}

/// <summary>A proposed category for an API ledger type, learned from a month that also has a statement CSV.</summary>
public sealed record LedgerTypeRow(string LedgerType, string? Description, int Lines, decimal Total, string Category, string Note)
{
    public bool Resolved => Category != Core.Category.Unknown;
}

public sealed record UnmatchedLine(string Side, DateOnly Date, string Type, string Title, decimal Net);

public sealed class LedgerCompareResult
{
    public string Month { get; init; } = "";
    public int StatementLines { get; init; }
    public int LedgerEntries { get; init; }
    public int Matched { get; set; }
    public decimal StatementTotal { get; set; }
    public decimal LedgerTotal { get; set; }
    public List<LedgerTypeRow> Types { get; } = [];
    public List<UnmatchedLine> Unmatched { get; } = [];

    /// <summary>Category rules (Type = "API: ledger_type") that make the API give the same P&amp;L as the CSV.</summary>
    public List<CategoryRule> ProposedRules { get; } = [];

    /// <summary>Every line matched, every type resolved, and the totals agree to the penny.</summary>
    public bool Passed => LedgerEntries > 0 && Unmatched.All(u => u.Net == 0) && Types.All(t => t.Resolved) && StatementTotal == LedgerTotal;
}

/// <summary>
/// Ledger compare: Etsy doesn't document its ledger_type strings, so one month is
/// pulled from the API and lined up against the same month's statement CSV.  Each API entry is
/// matched to the CSV line with the same amount (Net) on the same or next day, preferring the
/// same order/label/listing number.  The category of the matched CSV line becomes the
/// category of that ledger_type.  Nothing is guessed: a type whose lines land in more than one
/// category is split by description, and if that doesn't settle it, it stays Unknown.
/// </summary>
public static class LedgerCompare
{
    public static LedgerCompareResult Compare(string month, IEnumerable<StatementLine> statement, IEnumerable<LedgerEntry> ledger, Categorizer categorizer)
    {
        var csv = statement.Where(s => s.Month == month && s.SourceFile != LedgerLines.ApiSource).ToList();
        var api = ledger.Where(e => e.Month == month).OrderBy(e => e.SequenceNumber).ThenBy(e => e.EntryId).ToList();
        var result = new LedgerCompareResult { Month = month, StatementLines = csv.Count, LedgerEntries = api.Count };

        var free = new List<StatementLine>(csv);
        var pairs = new List<(LedgerEntry Entry, StatementLine Line)>();

        bool SameIds(StatementLine a, StatementLine b) =>
            (a.OrderId is not null && a.OrderId == b.OrderId) ||
            (a.LabelId is not null && a.LabelId == b.LabelId) ||
            (a.ListingId is not null && a.ListingId == b.ListingId);

        // Pass 1: same amount, same day, same order/label/listing.  Pass 2: same amount, same day.
        // Pass 3: same amount, one day apart (time zones).
        var passes = new Func<LedgerEntry, StatementLine, StatementLine, bool>[]
        {
            (e, asLine, s) => s.Date == e.Date && SameIds(asLine, s),
            (e, _, s) => s.Date == e.Date,
            (e, _, s) => Math.Abs(s.Date.DayNumber - e.Date.DayNumber) <= 1,
        };
        var pending = api.Select(e => (Entry: e, AsLine: LedgerLines.ToStatementLine(e))).ToList();
        foreach (var pass in passes)
        {
            foreach (var p in pending.ToList())
            {
                var hit = free.FirstOrDefault(s => s.Net == p.Entry.Amount && pass(p.Entry, p.AsLine, s));
                if (hit is null) continue;
                free.Remove(hit);
                pending.Remove(p);
                pairs.Add((p.Entry, hit));
            }
        }
        result.Matched = pairs.Count;

        foreach (var s in free)
            result.Unmatched.Add(new("Statement CSV", s.Date, s.Type, s.Title, s.Net));
        foreach (var p in pending)
            result.Unmatched.Add(new("Etsy API", p.Entry.Date, LedgerLines.TypeFor(p.Entry.LedgerType), p.Entry.Description, p.Entry.Amount));

        // Learn a category for each ledger_type from its matched lines.
        foreach (var byType in pairs.GroupBy(p => p.Entry.LedgerType).OrderBy(g => g.Key))
        {
            var cats = byType.Select(p => categorizer.Categorize(p.Line)).Distinct().ToList();
            var type = LedgerLines.TypeFor(byType.Key);
            if (cats.Count == 1 && cats[0] != Category.Unknown)
            {
                result.Types.Add(new(byType.Key, null, byType.Count(), byType.Sum(p => p.Entry.Amount), cats[0], "Learned from the statement"));
                result.ProposedRules.Add(new CategoryRule(type, null, cats[0], $"Learned by Ledger compare ({month})"));
                continue;
            }

            // Mixed: try each exact description on its own.
            foreach (var byDesc in byType.GroupBy(p => p.Entry.Description).OrderBy(g => g.Key))
            {
                var dc = byDesc.Select(p => categorizer.Categorize(p.Line)).Distinct().ToList();
                var ok = dc.Count == 1 && dc[0] != Category.Unknown;
                result.Types.Add(new(byType.Key, byDesc.Key, byDesc.Count(), byDesc.Sum(p => p.Entry.Amount),
                    ok ? dc[0] : Category.Unknown,
                    ok ? "Learned from the statement (by description)" : dc.Contains(Category.Unknown)
                        ? "The matching statement lines have no category either"
                        : "Matches lines in more than one category: " + string.Join(", ", dc)));
                if (ok) result.ProposedRules.Add(new CategoryRule(type, byDesc.Key, dc[0], $"Learned by Ledger compare ({month})"));
            }
        }
        // Types seen only on unmatched entries can't be learned.
        foreach (var g in pending.GroupBy(p => p.Entry.LedgerType).Where(g => result.Types.All(t => t.LedgerType != g.Key)))
            result.Types.Add(new(g.Key, null, g.Count(), g.Sum(p => p.Entry.Amount), Category.Unknown, "No matching statement line"));

        // Totals the way the P&L counts them (Excluded lines, like deposits, left out of both).
        result.StatementTotal = csv.Where(s => categorizer.Categorize(s) != Category.Excluded).Sum(s => s.Net);
        var learned = new Categorizer(result.ProposedRules, categorizer.Rules);
        result.LedgerTotal = api.Select(LedgerLines.ToStatementLine).Where(l => learned.Categorize(l) != Category.Excluded).Sum(l => l.Net);
        return result;
    }
}
