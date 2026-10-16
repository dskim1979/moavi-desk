/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using System.ServiceProcess;
using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// The MOAVI Desk Agent: <c>moavi-desk.exe --agent</c>, installed by the MSI as the Windows service
/// "MoaviDeskAgent" (LocalSystem, automatic). It runs from boot, whether or not anyone signs in or opens
/// the client.
/// <list type="number">
/// <item>With an enrolment code from the installer (%ProgramData%\MOAVI Desk\agent) and no token yet, it
/// enrols the PC (POST /api/vlab/terminals/enroll): the PC shows up under LAB > 단말 관리, waiting for
/// approval (or approved, when the code says so). The agent token it gets replaces nothing else.</item>
/// <item>Every few minutes it sends a heartbeat (POST /api/vlab/terminals/heartbeat): computer name, MAC of
/// the default-route adapter, IP, OS, versions, whether someone is signed in to Windows.</item>
/// <item>Once an admin approves the PC, a heartbeat carries the kiosk key, once. The agent writes it to
/// %ProgramData%\MOAVI Desk\terminal, where the client (kiosk mode) takes it up.</item>
/// </list>
/// The server's certificate is checked (or the pin in the PC's config). Without a code the agent only
/// waits; a PC is never put on the list without one.
/// </summary>
internal static class Agent
{
    public const string Switch = "--agent";
    public const string ServiceName = "MoaviDeskAgent";

    /// <summary>As the service; with <c>--console</c> in the foreground (try it from an elevated prompt).</summary>
    public static int Run(string[] args)
    {
        if (args.Contains("--console"))
        {
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            LoopAsync(stop.Token).GetAwaiter().GetResult();
            return 0;
        }
        if (OperatingSystem.IsWindows()) { ServiceBase.Run(new AgentService()); }
        return 0;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class AgentService : ServiceBase
    {
        private CancellationTokenSource? _stop;
        private Task? _loop;

        public AgentService() => ServiceName = Agent.ServiceName;

        protected override void OnStart(string[] args)
        {
            _stop = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_stop.Token));
        }

        protected override void OnStop()
        {
            _stop?.Cancel();
            try { _loop?.Wait(TimeSpan.FromSeconds(10)); } catch { }
        }
    }

    private static void Log(string line) => MachineSetup.Log(line, "agent.log");

    public static async Task LoopAsync(CancellationToken stop)
    {
        Log($"started {DeskApi.AppVersion}");
        while (!stop.IsCancellationRequested)
        {
            var wait = await OnceAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(wait), stop); }
            catch (TaskCanceledException) { break; }
        }
        Log("stopped");
    }

    /// <summary>One enrolment or heartbeat; returns the seconds until the next.</summary>
    internal static async Task<int> OnceAsync()
    {
        var settings = MachineConfig.LoadSettings();
        var secrets = MachineConfig.LoadAgent();
        var terminal = MachineConfig.LoadTerminal();
        if (string.IsNullOrWhiteSpace(settings?.ServerUrl)) { return 300; }
        if (string.IsNullOrEmpty(secrets.AgentToken) && string.IsNullOrEmpty(secrets.EnrollCode)) { return 600; }   // not a LAB PC

        var wait = 120;
        var (mac, ip, macs) = DeskApi.PrimaryNic();
        var info = new
        {
            hostname = Environment.MachineName,
            mac,
            macs = string.Join(",", macs),
            ip,
            os = RuntimeInformation.OSDescription,
            client_version = DeskApi.AppVersion,
            agent_version = DeskApi.AppVersion,
            user_logged_in = SomeoneSignedIn()
        };
        try
        {
            using var api = new DeskApi(settings.ServerUrl, settings.IgnoreCertificateErrors == true, settings.ServerCertSha256);
            EnrollAnswer a;
            if (string.IsNullOrEmpty(secrets.AgentToken))
            {
                if (mac is null)
                {
                    terminal.Problem = "no network adapter";
                    Save(terminal);
                    return 60;
                }
                a = await api.EnrollAsync(mac, Environment.MachineName, secrets.EnrollCode!, info);
                secrets.AgentToken = a.Token;
                MachineConfig.Write(MachineConfig.AgentFile, secrets);
                Log($"enrolled {mac} as {a.Terminal?.Id}: {a.State}");
                wait = 5;   // the first heartbeat soon: an approved-at-once PC gets its key right away
            }
            else
            {
                a = await api.HeartbeatAsync(secrets.AgentToken, info);
                wait = Math.Clamp(a.HeartbeatSeconds, 30, 3600);
            }
            terminal.TerminalId = a.Terminal?.Id ?? terminal.TerminalId;
            terminal.TerminalName = a.Terminal?.Name ?? terminal.TerminalName;
            terminal.Lab = a.Lab?.Name;
            terminal.State = a.State;
            terminal.Problem = null;
            terminal.LastContact = DateTime.Now.ToString("s");
            if (a.Key is { Length: > 0 } key)
            {
                terminal.TerminalKey = key;
                Log("kiosk key received");
            }
        }
        catch (DeskApiException ex)
        {
            switch (ex.Code)
            {
                case "REJECTED":
                    // refused: the token is gone; the code is not used again on this PC
                    secrets.AgentToken = secrets.EnrollCode = null;
                    MachineConfig.Write(MachineConfig.AgentFile, secrets);
                    terminal.State = "rejected";
                    terminal.TerminalKey = null;
                    terminal.Problem = ex.Message;
                    wait = 3600;
                    break;
                case "BAD_TOKEN" or "TOKEN_EXPIRED":
                    // removed on the server, or waited too long: enrol again with the code (when there is one)
                    secrets.AgentToken = null;
                    MachineConfig.Write(MachineConfig.AgentFile, secrets);
                    terminal.TerminalKey = null;
                    terminal.State = null;
                    terminal.Problem = ex.Message;
                    wait = 30;
                    break;
                case "TOO_MANY":
                    terminal.Problem = ex.Message;
                    wait = 300;
                    break;
                case "network":
                    terminal.Problem = ex.Message;
                    wait = 60;
                    break;
                default:
                    // a refused code (switched off, used up, expired …): an admin has to act
                    terminal.Problem = ex.Message;
                    wait = ex.Code == "BAD_CODE" || ex.Code.StartsWith("CODE_", StringComparison.Ordinal) ? 1800 : 120;
                    break;
            }
            Log($"{ex.Code}: {ex.Message}");
        }
        catch (Exception ex)
        {
            terminal.Problem = ex.Message;
            Log($"error: {ex.Message}");
            wait = 120;
        }
        Save(terminal);
        return wait;
    }

    private static void Save(TerminalState t)
    {
        try { MachineConfig.Write(MachineConfig.TerminalFile, t); }
        catch (Exception ex) { Log($"cannot write the terminal file: {ex.Message}"); }
    }

    /// <summary>Someone is signed in to Windows: their shell or the client runs outside session 0.</summary>
    private static bool SomeoneSignedIn()
    {
        try
        {
            return Process.GetProcessesByName("explorer").Concat(Process.GetProcessesByName("moavi-desk"))
                          .Any(p => p.SessionId != 0);
        }
        catch { return false; }
    }
}
