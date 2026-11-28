/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
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

    /// <summary>SHA-256 of the server's certificate (hex): a private certificate is trusted by this pin.</summary>
    public string? ServerCertSha256 { get; set; }

    public string? Protocol { get; set; }
    public bool? Kiosk { get; set; }
    public string? KioskAdminPasswordHash { get; set; }
    public bool? KioskForceFullScreen { get; set; }
    public string? KioskLoginBackground { get; set; }
}

/// <summary>
/// The agent's secrets: <c>%ProgramData%\MOAVI Desk\agent</c>, readable by SYSTEM and administrators only.
/// The MOAVI Desk Agent (a service, SYSTEM) keeps the enrolment code from the installer and the
/// agent token the engine gave it here.
/// </summary>
internal sealed class AgentSecrets
{
    public string? EnrollCode { get; set; }
    public string? AgentToken { get; set; }
}

/// <summary>
/// What the agent tells the client: <c>%ProgramData%\MOAVI Desk\terminal</c>, written by the agent,
/// readable by users. The kiosk key the engine handed over on approval, and where the PC stands.
/// </summary>
internal sealed class TerminalState
{
    /// <summary>The kiosk key (<c>mdt_…</c>) the agent got once an admin approved this PC.</summary>
    public string? TerminalKey { get; set; }

    public string? TerminalId { get; set; }
    public string? TerminalName { get; set; }
    public string? Lab { get; set; }

    /// <summary>pending | approved | rejected (the engine's), or empty before enrolment.</summary>
    public string? State { get; set; }

    /// <summary>When the agent last reached the engine.</summary>
    public string? LastContact { get; set; }

    /// <summary>Why the agent could not (a refused code, no network …); null when all is well.</summary>
    public string? Problem { get; set; }
}

internal static class MachineConfig
{
    public static readonly string Dir = SystemIO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MOAVI Desk");

    public static readonly string ConfigFile = SystemIO.Path.Combine(Dir, "config");
    public static readonly string AgentFile = SystemIO.Path.Combine(Dir, "agent");
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

    public static AgentSecrets LoadAgent() => Read<AgentSecrets>(AgentFile) ?? new AgentSecrets();

    public static void Write(string path, object o) => File.WriteAllText(path, Serializer.Serialize(o));

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
            Set(config, nameof(AppConfig.ServerCertSha256), m.ServerCertSha256);
            Set(config, nameof(AppConfig.Protocol), m.Protocol);
            Set(config, nameof(AppConfig.Kiosk), m.Kiosk);
            Set(config, nameof(AppConfig.KioskAdminPasswordHash), m.KioskAdminPasswordHash);
            Set(config, nameof(AppConfig.KioskForceFullScreen), m.KioskForceFullScreen);
            Set(config, nameof(AppConfig.KioskLoginBackground), m.KioskLoginBackground);
        }
        Refresh(config);
    }

    /// <summary>Takes up a kiosk key the agent got since; returns the terminal file.</summary>
    public static TerminalState Refresh(AppConfig config)
    {
        var terminal = LoadTerminal();
        if (!string.IsNullOrEmpty(terminal.TerminalKey))
        {
            Set(config, nameof(AppConfig.TerminalKey), terminal.TerminalKey);
        }
        else if (config.IsManaged(nameof(AppConfig.TerminalKey)))
        {
            // the agent dropped it (the terminal was removed on the server)
            config.TerminalKey = string.Empty;
            config.Managed.Remove(nameof(AppConfig.TerminalKey));
        }
        return terminal;
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
}

/// <summary>
/// <c>moavi-desk.exe --machine-config …</c>: what the MSI runs (as SYSTEM, after the files are in, before
/// the agent service starts) to write the PC's settings, and <c>--machine-config --remove</c> on uninstall.
/// <code>
///   --server &lt;address or URL&gt;  --port &lt;n&gt;  --https 0|1  --kiosk 0|1  --cert-sha256 &lt;hex&gt;
///   --enroll-code MDE-…  (or --enroll-token)  --kiosk-password-hash &lt;iterations.salt.hash&gt;
///   --msi &lt;path of the .msi&gt;   reads moavi-desk-enroll.json beside it for what the command line leaves out
/// </code>
/// Only the values given change; an upgrade without them keeps what is there. Exit code 0 when
/// written, 1 otherwise (the installation then rolls back); the reason goes to
/// <c>%ProgramData%\MOAVI Desk\setup.log</c>.
/// </summary>
internal static class MachineSetup
{
    public const string Switch = "--machine-config";
    public const string EnrollFileName = "moavi-desk-enroll.json";

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
            var fromFile = FromEnrollFile(Value(a, "--msi"));
            foreach (var (k, v) in fromFile)
            {
                if (Value(a, k) is null) { a[k] = v; }    // the command line wins
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
            if (Value(a, "--cert-sha256") is { } pin)
            {
                pin = pin.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
                if (pin.Length != 64 || !pin.All(Uri.IsHexDigit)) { throw new ArgumentException("SERVER_CERT_SHA256 is not 64 hex digits"); }
                m.ServerCertSha256 = pin;
            }
            MachineConfig.Write(MachineConfig.ConfigFile, m);

            var agent = MachineConfig.LoadAgent();
            var terminal = MachineConfig.LoadTerminal();
            // a PC that holds its key already is not enrolled again by a repair or an upgrade
            if ((Value(a, "--enroll-code") ?? Value(a, "--enroll-token")) is { } code && string.IsNullOrEmpty(terminal.TerminalKey))
            {
                agent.EnrollCode = code;
                agent.AgentToken = null;
            }
            MachineConfig.Write(MachineConfig.AgentFile, agent);
            if (OperatingSystem.IsWindows()) { AdminsOnly(MachineConfig.AgentFile); }
            if (!File.Exists(MachineConfig.TerminalFile)) { MachineConfig.Write(MachineConfig.TerminalFile, terminal); }
            Log("written: " + string.Join(' ', a.Keys) + (fromFile.Count > 0 ? $" ({EnrollFileName})" : ""));
            return 0;
        }
        catch (Exception ex)
        {
            Log("failed: " + ex.Message);
            return 1;
        }
    }

    // the MSI's short switches (Package.wxs), each followed by its value, empty or not
    private static readonly Dictionary<string, string> Short = new(StringComparer.Ordinal)
    {
        ["-s"] = "--server", ["-p"] = "--port", ["-h"] = "--https", ["-k"] = "--kiosk", ["-c"] = "--enroll-code",
        ["-t"] = "--enroll-token", ["-f"] = "--cert-sha256", ["-w"] = "--kiosk-password-hash", ["-m"] = "--msi",
    };

    private static Dictionary<string, string> Parse(string[] args)
    {
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (Short.TryGetValue(args[i], out var longName))
            {
                a[longName] = i + 1 < args.Length ? args[++i].Trim() : "";
                continue;
            }
            var name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name == Switch) { continue; }
            var next = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
            a[name] = next.Trim();
        }
        return a;
    }

    /// <summary>
    /// moavi-desk-enroll.json beside the MSI (the installer's OriginalDatabase), as the switches above:
    /// {"server", "port", "https", "enroll_code" | "enroll_token", "kiosk", "server_cert_sha256", "kiosk_password_hash"}.
    /// </summary>
    internal static Dictionary<string, string> FromEnrollFile(string? msiPath)
    {
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(msiPath)) { return a; }
        var path = SystemIO.Path.Combine(SystemIO.Path.GetDirectoryName(msiPath) ?? "", EnrollFileName);
        if (!File.Exists(path)) { return a; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var key = p.Name.ToLowerInvariant() switch
            {
                "server" => "--server",
                "port" => "--port",
                "https" => "--https",
                "kiosk" => "--kiosk",
                "enroll_code" or "enroll_token" => "--enroll-code",
                "server_cert_sha256" => "--cert-sha256",
                "kiosk_password_hash" => "--kiosk-password-hash",
                _ => null
            };
            if (key is null) { continue; }
            a[key] = p.Value.ValueKind switch
            {
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                JsonValueKind.Number => p.Value.GetRawText(),
                JsonValueKind.String => p.Value.GetString() ?? "",
                _ => ""
            };
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

    // the agent's file (enrolment code, agent token): SYSTEM and administrators only
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AdminsOnly(string path)
    {
        var sec = new FileSecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.LocalSystemSid), FileSystemRights.FullControl, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinAdministratorsSid), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(sec);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static SecurityIdentifier Sid(WellKnownSidType type) => new(type, null);

    internal static void Log(string line, string file = "setup.log")
    {
        try
        {
            Directory.CreateDirectory(MachineConfig.Dir);
            var path = SystemIO.Path.Combine(MachineConfig.Dir, file);
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) { File.Delete(path); }
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
