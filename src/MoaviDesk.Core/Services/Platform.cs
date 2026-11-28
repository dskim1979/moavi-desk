/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;

namespace MoaviDesk.Services;

/// <summary>An open connection to a desktop: a viewer process, or (Android) the RDP screen.
/// Ending it closes the viewer; Exited comes once, when the user closed it.</summary>
internal abstract class RemoteSession
{
    public abstract bool HasExited { get; }
    public abstract void End();
    public event EventHandler? Exited;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected void RaiseExited()
    {
        if (_exited.TrySetResult()) { Exited?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Until the user closed it.</summary>
    public Task WaitForExitAsync() => _exited.Task;

    public static RemoteSession Of(Process p) => new ProcessSession(p);

    private sealed class ProcessSession : RemoteSession
    {
        private readonly Process _p;
        public ProcessSession(Process p)
        {
            _p = p;
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => RaiseExited();
            try { if (p.HasExited) { RaiseExited(); } } catch { }
        }
        public override bool HasExited { get { try { return _p.HasExited; } catch { return true; } } }
        public override void End() { try { if (!_p.HasExited) { _p.Kill(); } } catch { } }
    }
}

/// <summary>A connection window opened at the click, before the engine answered (Windows): Deliver
/// hands it the .rdp, Cancel closes it.</summary>
internal interface IEarlyRdp
{
    RemoteSession Session { get; }
    void Deliver(string rdp, string? password);
    void Cancel();
}

/// <summary>How this platform opens an .rdp from the engine (Windows: the Remote Desktop control in
/// MOAVI Desk's window; macOS and Android: FreeRDP).</summary>
internal interface IRdpClient
{
    /// <summary>A window at the click while the engine prepares the desktop; null when the platform has none.</summary>
    IEarlyRdp? Start(string title, bool fullScreen);

    /// <summary>Opens the .rdp (gateway token, Warpgate ticket and devices already in it).
    /// password: the user's MOAVI Desk password for the desktop's Windows logon, or null.
    /// allowed: what the pool lets this connection share (the 장치 menu can switch only these during the session).</summary>
    Task<(string Error, RemoteSession? Session)> OpenAsync(string rdp, string title, bool fullScreen, string? password, DevicePolicy allowed);
}

/// <summary>What the pool lets a connection share - read from the engine's .rdp before the user's choice.</summary>
internal sealed record DevicePolicy(bool Drives, bool Printers, bool Clipboard)
{
    public static DevicePolicy Of(string rdp)
    {
        var s = Viewer.ParseRdp(rdp);
        bool On(string key, bool fallback = false) => s.TryGetValue(key, out var v) ? v.Trim() == "1" : fallback;
        return new((s.TryGetValue("drivestoredirect", out var d) && d.Trim().Length > 0) || On("redirectdrives"),
                   On("redirectprinters"), On("redirectclipboard", true));
    }
}

/// <summary>What the running app can do; each platform app fills it in before the first page.</summary>
internal static class Platform
{
    /// <summary>RDP; null = RDP is not offered here.</summary>
    public static IRdpClient? Rdp { get; set; }

    /// <summary>Where the kept sign-in password is protected; null = the password is not kept here.</summary>
    public static IVault? Vault { get; set; } = OperatingSystem.IsWindows() ? new DpapiVault() : null;

    /// <summary>Whether SPICE / VNC open in remote-viewer here (not on Android).</summary>
    public static bool RemoteViewer { get; set; } = true;

    /// <summary>VNC in MOAVI Desk's own page (UI/VncPage, RfbClient) instead of remote-viewer (Android).</summary>
    public static bool InAppVnc { get; set; }

    /// <summary>Hands a SPICE .vv to another app (Android: a SPICE app such as aSPICE); null = remote-viewer.</summary>
    public static Func<string, Task<(string Error, RemoteSession? Session)>>? OpenSpice { get; set; }

    /// <summary>Opens a web page (portal, support, download); the default starts the system's browser.</summary>
    public static Action<string> OpenUrl { get; set; } = url =>
    {
        try
        {
            if (OperatingSystem.IsMacOS()) { Process.Start("open", url); }
            else if (OperatingSystem.IsLinux()) { Process.Start("xdg-open", url); }
            else { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        }
        catch { }
    };
}
