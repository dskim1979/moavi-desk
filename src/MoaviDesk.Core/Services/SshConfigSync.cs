/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.IO;
using System.Text;
using Path = System.IO.Path;

namespace MoaviDesk.Services;

/// <summary>
/// Writes the researcher's SSH config for Warpgate native access into the user's own
/// <c>~/.ssh/config</c> (<c>%USERPROFILE%\.ssh\config</c> on Windows), inside a clearly delimited
/// managed block so it only ever rewrites its own lines and never touches anything the user added.
/// Then PuTTY / scp / WinSCP / VS Code Remote-SSH "just work" by picking the desktop host alias.
///
/// Warpgate is not a jump host: each entry selects its target through the SSH username
/// (<c>User &lt;wguser&gt;:&lt;target&gt;</c>), so no ProxyJump and the target IP stays hidden.
/// </summary>
internal static class SshConfigSync
{
    private const string Begin = "# >>> MOAVI Desk (managed) >>>";
    private const string End = "# <<< MOAVI Desk (managed) <<<";

    private static string SshDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public static string ConfigPath => Path.Combine(SshDir, "config");

    /// <summary>
    /// Replace (or remove, when <paramref name="managedBlock"/> is empty) the MOAVI-managed block in
    /// the user's ssh config, keeping every other line byte-for-byte. Returns the config path.
    /// </summary>
    public static string WriteManagedBlock(string managedBlock)
    {
        Directory.CreateDirectory(SshDir);
        var existing = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "";
        var outside = StripManaged(existing);

        var sb = new StringBuilder();
        if (outside.Length > 0)
        {
            sb.Append(outside.TrimEnd('\n'));
            sb.Append('\n');
        }
        var block = (managedBlock ?? "").Trim();
        if (block.Length > 0)
        {
            if (sb.Length > 0) { sb.Append('\n'); }
            sb.Append(Begin).Append('\n');
            sb.Append("# Do not edit inside this block; MOAVI Desk rewrites it.\n");
            sb.Append(block).Append('\n');
            sb.Append(End).Append('\n');
        }

        File.WriteAllText(ConfigPath, sb.ToString());
        TryRestrictPermissions(ConfigPath);
        return ConfigPath;
    }

    /// <summary>Everything in the file except the MOAVI-managed block (and its trailing newline).</summary>
    private static string StripManaged(string text)
    {
        var start = text.IndexOf(Begin, StringComparison.Ordinal);
        if (start < 0) { return text; }
        var endIdx = text.IndexOf(End, start, StringComparison.Ordinal);
        if (endIdx < 0)
        {
            // malformed (no end marker): drop from the begin marker on, keep what came before
            return text[..start];
        }
        endIdx += End.Length;
        if (endIdx < text.Length && text[endIdx] == '\r') { endIdx++; }
        if (endIdx < text.Length && text[endIdx] == '\n') { endIdx++; }
        return text[..start] + text[endIdx..];
    }

    /// <summary>
    /// Fetch the user's Warpgate ssh config and write the managed block. Does nothing (and removes an
    /// old block) when Warpgate is off. Returns the number of host entries written.
    /// </summary>
    public static async Task<int> SyncAsync(DeskApi api)
    {
        var cfg = await api.SshConfigAsync();
        if (!cfg.Enabled)
        {
            WriteManagedBlock("");   // clear a stale block when the feature is turned off
            return 0;
        }
        WriteManagedBlock(cfg.ManagedBlock);
        return cfg.Entries.Count;
    }

    /// <summary>
    /// Upload the user's default public key to Warpgate (if one exists locally) so their own tools can
    /// authenticate, then write the config block. Never reads or sends a private key. Returns the
    /// number of host entries written; -1 when no local public key was found.
    /// </summary>
    public static async Task<int> AutoProvisionAsync(DeskApi api)
    {
        var pub = FindDefaultPublicKey();
        if (pub is not null)
        {
            try { await api.AddSshKeyAsync($"MOAVI Desk ({Environment.MachineName})", pub); }
            catch { /* already present or Warpgate rejected a duplicate; config write still proceeds */ }
        }
        var n = await SyncAsync(api);
        return pub is null ? -1 : n;
    }

    /// <summary>The contents of the user's preferred public key (id_ed25519.pub, else id_rsa.pub, else
    /// any *.pub), or null. Only ever reads <c>.pub</c> files — never a private key.</summary>
    public static string? FindDefaultPublicKey()
    {
        if (!Directory.Exists(SshDir)) { return null; }
        foreach (var name in new[] { "id_ed25519.pub", "id_ecdsa.pub", "id_rsa.pub" })
        {
            var p = Path.Combine(SshDir, name);
            if (File.Exists(p)) { return File.ReadAllText(p).Trim(); }
        }
        foreach (var p in Directory.EnumerateFiles(SshDir, "*.pub"))
        {
            return File.ReadAllText(p).Trim();
        }
        return null;
    }

    private static void TryRestrictPermissions(string path)
    {
        // ssh refuses a world-readable config on Unix; on Windows OpenSSH tolerates it. Best effort only.
        if (OperatingSystem.IsWindows()) { return; }
        try
        {
#pragma warning disable CA1416
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
        }
        catch { /* best effort */ }
    }
}
