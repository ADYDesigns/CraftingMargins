// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CraftingMargins.Data;

/// <summary>
/// Dated database backups made with SQLite's online backup API (safe even while the
/// database is open).  Each backup is one file and also carries the category rules and
/// settings, so restoring it brings back everything that was typed in.
///
/// A backup is made when the app closes, but only if something changed since the last one, so
/// the newest N (Settings: 0 to 10, default 3, 0 = off) are N different states of the data and
/// not N copies of the same one.
/// </summary>
public static class Backups
{
    /// <summary>"Crafting Margins Backup 2026-11-19 210000.db": plainly a backup, not another copy of the data.</summary>
    private const string Prefix = "Crafting Margins Backup ";
    private const string Stamp = "yyyy-MM-dd HHmmss";

    /// <summary>Files stored inside a backup next to the data (a table that only exists in backup files).</summary>
    private const string ExtrasTable = "BackupFiles";

    /// <summary>What identifies the state of the data: SQLite's change counter plus the category rules file.</summary>
    public static string StateOf(Database db, IAppPaths? paths)
    {
        var rules = paths is not null && File.Exists(paths.CategoryRulesFile) ? File.GetLastWriteTimeUtc(paths.CategoryRulesFile).Ticks : 0;
        return $"{db.ChangeCounter()?.ToString(CultureInfo.InvariantCulture) ?? "?"}|{rules.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The backup made when the app closes: skipped when nothing has changed since the last
    /// backup, so opening the app just to look never pushes a good backup out.
    /// Returns the file written, or null if none was needed.
    /// </summary>
    public static string? MakeBackupIfChanged(Database db, IAppPaths paths, AppSettings settings, string folder, DateTime? now = null)
    {
        if (settings.BackupsToKeep <= 0) return null;   // the user turned backups off
        var state = StateOf(db, paths);
        if (state == settings.LastBackupState && List(folder).Count > 0) return null;
        var file = MakeBackup(db, folder, settings.BackupsToKeep, now, paths);
        if (file is not null)
        {
            settings.LastBackupState = state;
            settings.Save(paths);
        }
        return file;
    }

    public static string? MakeBackup(Database db, string folder, int keep, DateTime? now = null, IAppPaths? paths = null)
    {
        if (!File.Exists(db.FilePath)) return null;
        Directory.CreateDirectory(folder);
        var stamp = (now ?? DateTime.Now).ToString(Stamp, CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"{Prefix}{stamp}.db");

        using (var source = db.Open())
        using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            dest.Open();
            source.BackupDatabase(dest);
            if (paths is not null) WriteExtras(dest, paths);
        }
        Prune(folder, keep);
        Log.Write($"Backup written: {Path.GetFileName(target)}");
        return target;
    }

    private static void WriteExtras(SqliteConnection dest, IAppPaths paths)
    {
        Database.Exec(dest, null, $"CREATE TABLE IF NOT EXISTS {ExtrasTable}(Name TEXT PRIMARY KEY, Content TEXT NOT NULL)");
        foreach (var (name, file) in new[] { ("categories.json", paths.CategoryRulesFile), ("settings.json", paths.SettingsFile) })
            if (File.Exists(file))
                Database.Exec(dest, null, $"INSERT OR REPLACE INTO {ExtrasTable} VALUES($n,$c)", ("$n", name), ("$c", File.ReadAllText(file)));
    }

    /// <summary>Backups in the folder, newest first.</summary>
    public static List<string> List(string folder) =>
        Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, Prefix + "*.db").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)]
            : [];

    private static DateTime? TimeOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.StartsWith(Prefix, StringComparison.Ordinal) && DateTime.TryParseExact(name[Prefix.Length..], Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>"Crafting Margins Backup 2026-11-19 210000.db" → "Backup from Nov 19, 2026, 9:00 PM".</summary>
    public static string Describe(string file) =>
        TimeOf(file) is { } t ? "Backup from " + t.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture) : Path.GetFileName(file);

    /// <summary>Deletes all but the newest <paramref name="keep"/> backups (always leaves at least the one just made).</summary>
    public static void Prune(string folder, int keep)
    {
        foreach (var old in List(folder).Skip(Math.Max(1, keep)))
            try { File.Delete(old); }
            catch (IOException) { /* in use; next time */ }
            catch (UnauthorizedAccessException) { /* read-only; leave it */ }
    }

    /// <summary>
    /// Replaces the live database with a backup (the current one is backed up first), then
    /// brings it up to this version's layout, since the backup may be from an older version.
    /// The category rules stored in the backup come back too.  Returns the settings.json text
    /// the backup carried (null for a backup made by an older version), for the caller to
    /// take the shop's own settings from.
    /// </summary>
    public static string? Restore(Database db, string folder, string backupFile, int keep, IAppPaths? paths = null)
    {
        var version = Database.StoredSchemaVersion(backupFile);
        if (version > Database.SchemaVersion) throw new DatabaseTooNewException(version, Database.SchemaVersion);

        MakeBackup(db, folder, keep + 1, paths: paths);
        string? settingsJson = null;
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            source.Open();
            using var dest = db.Open();
            source.BackupDatabase(dest);

            var hasExtras = Database.Query(dest, "SELECT name FROM sqlite_master WHERE type='table' AND name=$n", r => r.GetString(0), ("$n", ExtrasTable)).Count > 0;
            if (hasExtras)
            {
                var extras = Database.Query(dest, $"SELECT Name, Content FROM {ExtrasTable}", r => (Name: r.GetString(0), Content: r.GetString(1)))
                    .ToDictionary(x => x.Name, x => x.Content);
                Database.Exec(dest, null, $"DROP TABLE {ExtrasTable}");   // it belongs in backup files only
                if (paths is not null && extras.TryGetValue("categories.json", out var rules)) File.WriteAllText(paths.CategoryRulesFile, rules);
                extras.TryGetValue("settings.json", out settingsJson);
            }
        }
        db.Upgrade();
        Log.Write($"Restored from backup {Path.GetFileName(backupFile)}");
        return settingsJson;
    }
}
