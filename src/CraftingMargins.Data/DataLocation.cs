// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Text.Json;

namespace CraftingMargins.Data;

/// <summary>
/// Where the data folder is, when the user moved it away from Documents\Crafting Margins
/// (Settings → Data folder).  The settings file lives inside the data folder, so the choice
/// is remembered in a small pointer file outside it, in the user's local app data.
/// </summary>
public sealed class DataLocation(string pointerFile)
{
    public static DataLocation ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crafting Margins", "location.json"));

    public string PointerFile { get; } = pointerFile;

    private sealed record Pointer(string DataFolder);

    /// <summary>The chosen folder, or null for the default.</summary>
    public string? Read()
    {
        try
        {
            if (!File.Exists(PointerFile)) return null;
            var p = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(PointerFile));
            return string.IsNullOrWhiteSpace(p?.DataFolder) ? null : p.DataFolder;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Write("The data-folder pointer couldn't be read: " + ex.Message);
            return null;
        }
    }

    /// <summary>Remembers <paramref name="folder"/>; null goes back to the default.</summary>
    public void Write(string? folder)
    {
        if (folder is null) { if (File.Exists(PointerFile)) File.Delete(PointerFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(PointerFile)!);
        File.WriteAllText(PointerFile, JsonSerializer.Serialize(new Pointer(folder), new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>True when <paramref name="folder"/> already holds Crafting Margins data.</summary>
    public static bool HasData(string folder) => File.Exists(((IAppPaths)new FolderAppPaths(folder)).DatabaseFile);

    /// <summary>
    /// Copies the whole data folder (database, settings, categories, encrypted secrets, backups,
    /// logs) to an empty or new folder.  The demo shop isn't copied; it's rebuilt when opened.
    /// Never overwrites a file that's already there.  The source is left exactly as it was.
    /// </summary>
    public static int Copy(string from, string to)
    {
        var src = Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar);
        var dst = Path.GetFullPath(to).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) throw new IOException("That's the folder the data is already in.");
        if (dst.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("The new folder can't be inside the current data folder.");
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            if (rel.StartsWith("Demo" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(dst, rel);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            n++;
        }
        return n;
    }
}
