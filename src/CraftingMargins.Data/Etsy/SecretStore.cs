// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace CraftingMargins.Data.Etsy;

/// <summary>
/// Where the API shared secret and tokens are kept.  Never in the database, settings, backups
/// or logs.  A Mac/Linux port supplies its own implementation (Keychain, libsecret).
/// </summary>
public interface ISecretStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
    /// <summary>One sentence for the user on how the secrets are protected here.</summary>
    string Description { get; }
}

/// <summary>Windows DPAPI, current user: only this Windows account on this PC can decrypt the files.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore(string folder) : ISecretStore
{
    // Extra entropy so another program running as the same user can't just call Unprotect on our files by accident.
    // Changing this text makes secrets saved earlier unreadable, so leave it alone once released.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Crafting Margins secret store v1");

    public string Description => "The shared secret is encrypted for your Windows account.";

    private string FileFor(string name) => Path.Combine(folder, name + ".bin");

    public string? Get(string name)
    {
        var f = FileFor(name);
        if (!File.Exists(f)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(f), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            Log.Write($"Secret '{name}' could not be decrypted (different Windows user or PC); it will need to be entered again.");
            return null;
        }
    }

    public void Set(string name, string value)
    {
        Directory.CreateDirectory(folder);
        var f = FileFor(name);
        File.WriteAllBytes(f + ".tmp", ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        File.Move(f + ".tmp", f, overwrite: true);
    }

    public void Delete(string name)
    {
        try { File.Delete(FileFor(name)); } catch (IOException) { }
    }
}

/// <summary>Keeps secrets in memory only (tests, or a platform with no secure store yet).</summary>
public sealed class MemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];
    public string? Get(string name) => _values.GetValueOrDefault(name);
    public void Set(string name, string value) => _values[name] = value;
    public void Delete(string name) => _values.Remove(name);
    public string Description => "The shared secret is kept in memory only on this system, so you will need to enter it and connect again each time the app starts.";
}
