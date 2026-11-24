// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ADY Designs, LLC. Part of Crafting Margins; see LICENSE and NOTICE.md.
using System.Diagnostics;
using System.Runtime.Versioning;

namespace CraftingMargins.Data.Etsy;

/// <summary>
/// macOS: the shared secret and tokens are kept in the user's login keychain, as generic
/// passwords under the service name "Crafting Margins".  It uses the "security" tool that
/// ships with macOS, so there is no native code to build or sign.
///
/// NOT YET RUN ON A MAC.  It was written on Windows from Apple's documentation of the tool;
/// test connect, restart and disconnect on a real Mac before a macOS release.
/// Known trade-off: "security add-generic-password -w" takes the value as a command-line
/// argument, so for the moment the command runs another program of the same user could see
/// it in the process list.  Calling the Keychain Services API directly would avoid that.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainSecretStore(string account) : ISecretStore
{
    private const string Service = "Crafting Margins";
    private const string Tool = "/usr/bin/security";

    public string Description => "The shared secret is kept in your macOS keychain.";

    /// <summary>One data folder = one set of secrets, so a portable or test folder doesn't share the main one's.</summary>
    private string AccountFor(string name) => $"{name}@{account}";

    public string? Get(string name)
    {
        var (code, output) = Run("find-generic-password", "-a", AccountFor(name), "-s", Service, "-w");
        return code == 0 ? output.TrimEnd('\n', '\r') : null;
    }

    public void Set(string name, string value)
    {
        // -U: update the item if it already exists.
        var (code, output) = Run("add-generic-password", "-U", "-a", AccountFor(name), "-s", Service, "-w", value);
        if (code != 0) throw new IOException("The macOS keychain didn't accept the value: " + output.Trim());
    }

    public void Delete(string name) => Run("delete-generic-password", "-a", AccountFor(name), "-s", Service);

    private static (int Code, string Output) Run(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(Tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return (-1, "");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, p.ExitCode == 0 ? stdout : stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write("The macOS keychain tool couldn't be run: " + ex.Message);   // never the value
            return (-1, ex.Message);
        }
    }
}
