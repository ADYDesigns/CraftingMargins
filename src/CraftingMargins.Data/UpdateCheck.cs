// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;

namespace CraftingMargins.Data;

/// <summary>A newer release on GitHub.</summary>
public sealed record ReleaseInfo(string Version, string Url);

/// <summary>
/// Asks GitHub for the latest release (one request on start, can be turned off in Settings).
/// Never downloads anything: it only tells the user a newer version exists.
/// </summary>
public static class UpdateCheck
{
    /// <param name="repo">"owner/name".  Empty = not published yet, so nothing is checked.</param>
    public static async Task<ReleaseInfo?> NewerReleaseAsync(HttpClient http, string repo, string currentVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;   // 404 = no releases yet
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        if (!IsNewer(tag, currentVersion)) return null;
        // Only ever link to GitHub.
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = $"https://github.com/{repo}/releases/latest";
        return new ReleaseInfo(Clean(tag), url);
    }

    /// <summary>"v1.2.0" vs "1.1.9" → true.  Anything that isn't a version number → false.</summary>
    public static bool IsNewer(string tag, string current) =>
        Version.TryParse(Clean(tag), out var latest) && Version.TryParse(Clean(current), out var now) && Normalize(latest) > Normalize(now);

    private static string Clean(string v) => v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
