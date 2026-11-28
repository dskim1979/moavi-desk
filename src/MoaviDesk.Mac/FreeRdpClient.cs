/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// macOS: a desktop in FreeRDP's own window (sdl-freerdp (FreeRDP's SDL3 client), bundled in MOAVI Desk.app by installer/macos/build.sh).
/// The options go in through stdin (/args-from:stdin), so the password, the gateway token and the Warpgate ticket
/// are never on a command line other processes can read. One process per desktop; closing its window ends it.
/// </summary>
internal sealed class FreeRdpClient(string path, AppConfig config) : IRdpClient
{
    /// <summary>The client in the app bundle (Contents/MacOS), else a Homebrew one (brew install freerdp).</summary>
    public static string? Find()
    {
        var here = AppContext.BaseDirectory;
        string[] candidates =
        [
            System.IO.Path.Combine(here, "sdl3-freerdp"),
            System.IO.Path.Combine(here, "sdl-freerdp"),
            "/opt/homebrew/bin/sdl3-freerdp",
            "/opt/homebrew/bin/sdl-freerdp",
            "/usr/local/bin/sdl3-freerdp",
            "/usr/local/bin/sdl-freerdp",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public IEarlyRdp? Start(string title, bool fullScreen) => null;

    public async Task<(string Error, RemoteSession? Session)> OpenAsync(string rdp, string title, bool fullScreen, string? password)
    {
        var args = FreeRdpArgs.ToArgs(FreeRdpArgs.Build(rdp, title, fullScreen, password, config)).ToList();
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("/args-from:stdin");
        try
        {
            var p = Process.Start(psi);
            if (p is null) { return (L("ViewerLaunchFailed"), null); }
            // FreeRDP reads one option per line until an empty line
            foreach (var a in args) { await p.StandardInput.WriteLineAsync(a.Replace('\n', ' ')); }
            await p.StandardInput.WriteLineAsync();
            p.StandardInput.Close();
            // its log (kept short: the last lines tell why a connection failed)
            var log = new Queue<string>();
            void Keep(string? line) { if (line is null) { return; } lock (log) { log.Enqueue(line); while (log.Count > 40) { log.Dequeue(); } } }
            p.OutputDataReceived += (_, e) => Keep(e.Data);
            p.ErrorDataReceived += (_, e) => Keep(e.Data);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // a refused connection ends within seconds: say why instead of a window that came and went
            if (await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(4))) is var t && p.HasExited && p.ExitCode != 0)
            {
                string last;
                lock (log) { last = log.LastOrDefault(l => l.Contains("ERROR", StringComparison.Ordinal)) ?? log.LastOrDefault() ?? ""; }
                return ($"{L("ViewerLaunchFailed")} (FreeRDP {p.ExitCode}) {Trim(last)}".Trim(), null);
            }
            return (string.Empty, RemoteSession.Of(p));
        }
        catch (Exception ex) { return (ex.Message, null); }
    }

    private static string Trim(string line)
    {
        var i = line.IndexOf("]: ", StringComparison.Ordinal);
        return i >= 0 ? line[(i + 3)..] : line;
    }
}
