// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Security.Cryptography;
using System.Text;

namespace CraftingMargins.App;

/// <summary>
/// Stops two copies of the app working on the same data folder at once.  Each copy reads the
/// data when it starts and saves whole lists back (every rule, every part, every expense), so
/// with two open, whichever saved last would silently undo what the other saved.
///
/// The lock is a named system mutex made from the folder's path: nothing is written into the
/// data folder (a lock file there would upset OneDrive and other sync tools), and it vanishes
/// by itself if the app is closed or crashes.  A different data folder, and the demo shop, are
/// not affected.  It guards one computer; it can't see a copy running on another PC that
/// shares the folder through a sync service.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _released;

    private InstanceLock(Mutex mutex) => _mutex = mutex;

    /// <summary>The name the lock for a folder goes by: the same for every way of writing the same path.</summary>
    public static string NameFor(string dataFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder));
        if (!OperatingSystem.IsLinux()) full = full.ToUpperInvariant();   // Windows and macOS ignore case in paths
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32];
        return $"CraftingMargins-{hash}";
    }

    /// <summary>Takes the lock for a data folder.  Null = another copy of the app already has it.</summary>
    public static InstanceLock? TryTake(string dataFolder)
    {
        var mutex = new Mutex(false, NameFor(dataFolder));
        try
        {
            if (mutex.WaitOne(0)) return new InstanceLock(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new InstanceLock(mutex);   // the copy that held it crashed; it's ours now
        }
        mutex.Dispose();
        return null;
    }

    /// <summary>Lets go of the folder (when switching to another data folder).  Must be called on the thread that took it.</summary>
    public void Dispose()
    {
        if (_released) return;
        _released = true;
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* not held by this thread: the process ending releases it anyway */ }
        _mutex.Dispose();
    }
}
