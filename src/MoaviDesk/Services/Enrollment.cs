/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// A LAB PC installed with an enrolment code (msiexec … ENROLL_CODE=MDE-…) makes itself known on
/// its first start, without anyone signing in: it sends its MAC, name and the code, keeps the poll
/// token it gets in %ProgramData%\MOAVI Desk\terminal and asks again until an admin approves it
/// (or the code approves at once). The kiosk key it then gets is kept beside the token, which the
/// engine does not take again. Runs once per process, on the UI thread, while the app is open.
/// </summary>
internal static class Enrollment
{
    private static bool _started;

    /// <summary>What the sign-in window shows about it; null when there is nothing to say.</summary>
    public static string? Status { get; private set; }

    public static bool IsProblem { get; private set; }

    public static event Action? Changed;

    /// <summary>Is this PC still to be enrolled? (no key yet, a code or a token from the installer)</summary>
    public static bool Needed(AppConfig config)
    {
        if (config.TerminalKey.Length > 0 || string.IsNullOrWhiteSpace(config.ServerUrl)) { return false; }
        var t = MachineConfig.LoadTerminal();
        return !string.IsNullOrEmpty(t.EnrollCode) || !string.IsNullOrEmpty(t.EnrollToken);
    }

    public static void Start(AppConfig config)
    {
        if (_started || !Needed(config)) { return; }
        _started = true;
        _ = RunAsync(config);
    }

    private static void Show(string? text, bool problem = false)
    {
        Status = text;
        IsProblem = problem;
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    private static async Task RunAsync(AppConfig config)
    {
        while (config.TerminalKey.Length == 0)
        {
            var t = MachineConfig.LoadTerminal();
            var wait = 30;
            try
            {
                using var api = new DeskApi(config.ServerUrl, config.IgnoreCertificateErrors);
                EnrollAnswer a;
                if (!string.IsNullOrEmpty(t.EnrollToken))
                {
                    a = await api.EnrollStatusAsync(t.EnrollToken);
                }
                else if (!string.IsNullOrEmpty(t.EnrollCode))
                {
                    if (DeskApi.PrimaryMac() is not { } mac)
                    {
                        Show(L("MoaviEnrollNoMac"), problem: true);
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        continue;
                    }
                    Show(L("MoaviEnrolling"));
                    a = await api.EnrollAsync(mac, Environment.MachineName, t.EnrollCode);
                    t.EnrollToken = a.Token;
                    t.TerminalId = a.Terminal?.Id;
                }
                else { break; }

                t.TerminalName = a.Terminal?.Name ?? t.TerminalName;
                t.Lab = a.Lab?.Name;
                if (a.Key is { Length: > 0 } key)
                {
                    t.TerminalKey = key;
                    t.EnrollToken = t.EnrollCode = null;
                    t.EnrolledAt = DateTime.Now.ToString("s");
                    if (MachineConfig.SaveTerminal(t))
                    {
                        MachineConfig.Set(config, nameof(AppConfig.TerminalKey), key);
                    }
                    else
                    {
                        // the PC's folder is not writable (installed without the installer's setup):
                        // the key goes to this user's settings so it is not lost
                        config.TerminalKey = key;
                        AppConfigManager.Save(config);
                    }
                    Show(string.Format(L("MoaviEnrollDone"), t.Lab ?? "-"));
                    return;
                }
                MachineConfig.SaveTerminal(t);
                Show(t.Lab is { Length: > 0 } lab ? string.Format(L("MoaviEnrollPendingLab"), lab) : L("MoaviEnrollPending"));
                wait = Math.Clamp(a.PollSeconds, 10, 300);
            }
            catch (DeskApiException ex)
            {
                switch (ex.Code)
                {
                    case "TOO_MANY":
                        Show(L("MoaviEnrollTooMany"), problem: true);
                        wait = 300;
                        break;
                    case "BAD_TOKEN" or "TOKEN_EXPIRED" or "GONE":
                        // the wait ended on the engine's side (removed, run out): enrol again with the code
                        t.EnrollToken = null;
                        MachineConfig.SaveTerminal(t);
                        if (string.IsNullOrEmpty(t.EnrollCode))
                        {
                            Show(L("MoaviKioskBadKey"), problem: true);
                            return;
                        }
                        wait = 5;
                        break;
                    case "network":
                        Show(L("MoaviEnrollRetry"), problem: true);
                        break;
                    case var c when c == "BAD_CODE" || c.StartsWith("CODE_", StringComparison.Ordinal):
                        // tried again on the next start (an admin may switch the code back on)
                        Show(string.Format(L("MoaviEnrollCodeRefused"), ex.Message), problem: true);
                        return;
                    default:
                        Show(ex.Message, problem: true);
                        wait = 60;
                        break;
                }
            }
            catch (Exception ex)
            {
                Show(ex.Message, problem: true);
                wait = 60;
            }
            await Task.Delay(TimeSpan.FromSeconds(wait));
        }
    }
}
