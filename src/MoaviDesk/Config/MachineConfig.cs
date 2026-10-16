/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Security.AccessControl;
using System.Security.Principal;
using MoaviDesk.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using SystemIO = System.IO;

namespace MoaviDesk.Config;

/// <summary>
/// The PC's own settings, for every user of it: <c>%ProgramData%\MOAVI Desk\config</c>.
/// The installer writes it (msiexec … SERVER=… KIOSK=1 ENROLL_CODE=…, see <see cref="MachineSetup"/>);
/// users can read it, not change it. Whatever it sets wins over the user's own config and is
/// locked in Settings. A value left out (null) is the user's to choose.
/// </summary>
internal sealed class MachineSettings
{
    public string? ServerUrl { get; set; }
    public bool? IgnoreCertificateErrors { get; set; }
    public string? Protocol { get; set; }
    public bool? Kiosk { get; set; }
    public string? KioskAdminPasswordHash { get; set; }
    public bool? KioskForceFullScreen { get; set; }
    public string? KioskLoginBackground { get; set; }
}

/// <summary>
/// The LAB terminal's enrolment beside it: <c>%ProgramData%\MOAVI Desk\terminal</c>. The installer
/// puts the enrolment code here; the client (running as the signed-in Windows user, who may write
/// this one file) keeps its poll token while it waits and then the kiosk key it got.
/// </summary>
internal sealed class TerminalState
{
    /// <summary>The installer's ENROLL_CODE; cleared once the key is here.</summary>
    public string? EnrollCode { get; set; }

    /// <summary>The engine's poll token while an admin has not approved this PC yet.</summary>
    public string? EnrollToken { get; set; }

    /// <summary>The kiosk key handed over on approval (<c>mdt_…</c>).</summary>
    public string? TerminalKey { get; set; }

    public string? TerminalId { get; set; }
    public string? TerminalName { get; set; }
    public string? Lab { get; set; }
    public string? EnrolledAt { get; set; }
}

internal static class MachineConfig
{
    public static readonly string Dir = SystemIO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MOAVI Desk");

    public static readonly string ConfigFile = SystemIO.Path.Combine(Dir, "config");
    public static readonly string TerminalFile = SystemIO.Path.Combine(Dir, "terminal");

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static MachineSettings? LoadSettings() => Read<MachineSettings>(ConfigFile);

    public static TerminalState LoadTerminal() => Read<TerminalState>(TerminalFile) ?? new TerminalState();

    /// <summary>Writes the terminal file in place (users may write it, not make files beside it).</summary>
    public static bool SaveTerminal(TerminalState state)
    {
        try
        {
            File.WriteAllText(TerminalFile, Serializer.Serialize(state));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Lays the PC's settings and the terminal's key over the user's <paramref name="config"/> and
    /// marks each of them managed (locked in Settings, never written to the user's file).
    /// </summary>
    public static void ApplyTo(AppConfig config)
    {
        if (LoadSettings() is { } m)
        {
            Set(config, nameof(AppConfig.ServerUrl), m.ServerUrl);
            Set(config, nameof(AppConfig.IgnoreCertificateErrors), m.IgnoreCertificateErrors);
            Set(config, nameof(AppConfig.Protocol), m.Protocol);
            Set(config, nameof(AppConfig.Kiosk), m.Kiosk);
            Set(config, nameof(AppConfig.KioskAdminPasswordHash), m.KioskAdminPasswordHash);
            Set(config, nameof(AppConfig.KioskForceFullScreen), m.KioskForceFullScreen);
            Set(config, nameof(AppConfig.KioskLoginBackground), m.KioskLoginBackground);
        }
        var terminal = LoadTerminal();
        if (!string.IsNullOrEmpty(terminal.TerminalKey))
        {
            Set(config, nameof(AppConfig.TerminalKey), terminal.TerminalKey);
        }
    }

    internal static void Set(AppConfig config, string name, object? value)
    {
        if (value is null || value is string { Length: 0 }) { return; }
        typeof(AppConfig).GetProperty(name)!.SetValue(config, value);
        config.Managed.Add(name);
    }

    private static T? Read<T>(string path) where T : class
    {
        try { return File.Exists(path) ? Deserializer.Deserialize<T>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    internal static string Serialize(object o) => Serializer.Serialize(o);
}

/// <summary>
/// <c>moavi-desk.exe --machine-config …</c>: what the MSI runs (as SYSTEM, after the files are in)
/// to write the PC's settings from its public properties, and <c>--machine-config --remove</c> on
/// uninstall. Only the values given are changed; an upgrade without them keeps what is there.
/// <code>
///   --server &lt;address or URL&gt;  --port &lt;n&gt;  --https 0|1  --kiosk 0|1
///   --enroll-code MDE-…  --kiosk-password-hash &lt;iterations.salt.hash&gt;
/// </code>
/// Exit code 0 when written, 1 otherwise (the installation then rolls back); the reason goes to
/// <c>%ProgramData%\MOAVI Desk\setup.log</c>.
/// </summary>
internal static class MachineSetup
{
    public const string Switch = "--machine-config";

    public static int Run(string[] args)
    {
        try
        {
            var a = Parse(args);
            if (a.ContainsKey("--remove"))
            {
                if (Directory.Exists(MachineConfig.Dir)) { Directory.Delete(MachineConfig.Dir, recursive: true); }
                return 0;
            }
            Directory.CreateDirectory(MachineConfig.Dir);
            if (OperatingSystem.IsWindows()) { LockDown(); }

            var m = MachineConfig.LoadSettings() ?? new MachineSettings();
            if (Server(a, m.ServerUrl) is { } url) { m.ServerUrl = url; }
            if (Flag(a, "--kiosk") is { } kiosk) { m.Kiosk = kiosk; }
            if (Value(a, "--kiosk-password-hash") is { } hash)
            {
                if (hash.Split('.').Length != 3) { throw new ArgumentException("KIOSK_PASSWORD_HASH is not iterations.salt.hash"); }
                m.KioskAdminPasswordHash = hash;
            }
            File.WriteAllText(MachineConfig.ConfigFile, MachineConfig.Serialize(m));

            var t = MachineConfig.LoadTerminal();
            // a PC that holds its key already is not enrolled again by a repair or an upgrade
            if (Value(a, "--enroll-code") is { } code && string.IsNullOrEmpty(t.TerminalKey))
            {
                t.EnrollCode = code;
                t.EnrollToken = null;
            }
            File.WriteAllText(MachineConfig.TerminalFile, MachineConfig.Serialize(t));
            if (OperatingSystem.IsWindows()) { OpenTerminalFile(); }
            Log("written: " + string.Join(' ', a.Keys));
            return 0;
        }
        catch (Exception ex)
        {
            Log("failed: " + ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name == Switch) { continue; }
            var next = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
            a[name] = next.Trim();
        }
        return a;
    }

    // an empty value is an MSI property that was not set: leave the setting alone
    private static string? Value(Dictionary<string, string> a, string name)
        => a.TryGetValue(name, out var v) && v.Length > 0 ? v : null;

    private static bool? Flag(Dictionary<string, string> a, string name)
        => Value(a, name)?.ToLowerInvariant() switch
        {
            null => null,
            "1" or "yes" or "true" or "on" => true,
            "0" or "no" or "false" or "off" => false,
            var v => throw new ArgumentException($"{name}: {v} is not 0 or 1")
        };

    /// <summary>SERVER (an address, host:port or a URL), PORT and HTTPS as one URL.</summary>
    internal static string? Server(Dictionary<string, string> a, string? current)
    {
        var server = Value(a, "--server");
        var https = Flag(a, "--https");
        var portText = Value(a, "--port");
        if (server is null && https is null && portText is null) { return null; }
        if (server is null && string.IsNullOrEmpty(current)) { throw new ArgumentException("PORT/HTTPS need SERVER"); }

        var u = DeskApi.NormalizeServer(server is null ? current!
                                        : server.Contains("://", StringComparison.Ordinal) ? server
                                        : (https == false ? "http://" : "https://") + server);
        var useHttps = https ?? (u.Scheme == Uri.UriSchemeHttps);
        var port = u.IsDefaultPort ? (useHttps ? 443 : 80) : u.Port;
        if (portText is not null && (!int.TryParse(portText, out port) || port is < 1 or > 65535))
        {
            throw new ArgumentException($"PORT: {portText}");
        }
        return DeskApi.ComposeServer(u.Host, port, useHttps);
    }

    // the folder: SYSTEM and administrators change it, users read it
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void LockDown()
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.LocalSystemSid), FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinAdministratorsSid), FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinUsersSid), FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(MachineConfig.Dir).SetAccessControl(sec);
    }

    // the terminal file: users may also write it (the client keeps its token and key there), not delete it
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void OpenTerminalFile()
    {
        var file = new FileInfo(MachineConfig.TerminalFile);
        var sec = file.GetAccessControl();
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinUsersSid),
                                                   FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Synchronize,
                                                   AccessControlType.Allow));
        file.SetAccessControl(sec);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static SecurityIdentifier Sid(WellKnownSidType type) => new(type, null);

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(MachineConfig.Dir);
            File.AppendAllText(SystemIO.Path.Combine(MachineConfig.Dir, "setup.log"),
                               $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
