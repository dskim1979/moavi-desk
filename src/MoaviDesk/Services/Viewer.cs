/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace MoaviDesk.Services;

/// <summary>
/// Opens a MOAVI Desk desktop in remote-viewer.
/// SPICE: the engine hands a .vv file (through the node's spiceproxy) → write it, launch it.
/// VNC: the engine hands a console websocket path → bridge it to a local TCP port and point
/// remote-viewer at that port with a vnc .vv file. The bridge lives until the viewer exits.
/// RDP: the engine hands an .rdp file → MOAVI Desk's own window with Windows' Remote Desktop control
/// (RdpWindow; mstsc's engine, so UDP and H.264/AVC when the desktop offers them) - always, also
/// through the bundled RD Gateway; Windows' Remote Desktop app is not started.
/// </summary>
internal static class Viewer
{
    /// <summary>The order the client offers: RDP (lightest on a WAN for office work), SPICE (USB,
    /// audio), VNC. RDP needs Windows' mstsc, so another OS starts with SPICE.</summary>
    public static readonly string[] Order = ["rdp", "spice", "vnc"];

    public static List<string> Offered(IEnumerable<string> fromEngine)
    {
        var have = fromEngine.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Order.Where(p => have.Contains(p) && (p != "rdp" || OperatingSystem.IsWindows())).ToList();
    }

    public static async Task<(string Error, Process? Process)> OpenAsync(DeskApi api, ConnectInfo c,
                                                                       string viewerPath, string title, bool fullScreen,
                                                                       MoaviDesk.Config.DeviceChoice? devices = null)
    {
        if (c.Type == "rdp")
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(c.Rdp)) { return (L("RdpNeedsWindows"), null); }
            if (!fullScreen) { c.Rdp = c.Rdp.Replace("screen mode id:i:2", "screen mode id:i:1", StringComparison.Ordinal); }
            if (devices is not null) { c.Rdp = WithDevices(c.Rdp, devices); }
            var (embedded, error, process) = RdpLauncher.Open(c.Rdp, title, fullScreen, SignInPassword(api, c));
            return embedded ? (error, process) : OpenRdp(c.Rdp, fullScreen);
        }

        var viewer = string.IsNullOrWhiteSpace(viewerPath) ? null : RemoteViewer.Resolve(viewerPath);
        if (viewer is null) { return (L("ViewerPathNotSet"), null); }

        if (c.Type == "spice")
        {
            var vv = WriteTemp(AddFullScreen(c.Vv ?? "", fullScreen));
            var (p, err) = RemoteViewer.Launch(viewer, vv);
            return p is null ? (err ?? L("ViewerLaunchFailed"), null) : (string.Empty, p);
        }

        if (c.Type == "vnc" && c.WsPath is { Length: > 0 })
        {
            var bridge = await VncBridge.StartAsync(api, c.WsPath);
            var vv = WriteTemp(VncFile(bridge.Port, c.Password ?? "", title, fullScreen));
            var (p, err) = RemoteViewer.Launch(viewer, vv);
            if (p is null)
            {
                await bridge.DisposeAsync();
                return (err ?? L("ViewerLaunchFailed"), null);
            }
            p.EnableRaisingEvents = true;
            p.Exited += async (_, _) => { try { await bridge.DisposeAsync(); } catch { } };
            return (string.Empty, p);
        }

        return (L("ViewerLaunchFailed"), null);
    }

    /// <summary>
    /// Finds remote-viewer: the configured path, else the copy shipped next to this client
    /// (the installer bundles virt-viewer), else a system-wide VirtViewer install.
    /// </summary>
    public static string FindViewer(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) { return configured; }
        if (!OperatingSystem.IsWindows()) { return string.IsNullOrWhiteSpace(configured) ? "remote-viewer" : configured; }

        var here = AppContext.BaseDirectory;
        string[] candidates =
        [
            System.IO.Path.Combine(here, "virt-viewer", "bin", "remote-viewer.exe"),
            System.IO.Path.Combine(here, "remote-viewer.exe"),
            .. Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                ? Directory.GetDirectories(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VirtViewer*")
                           .OrderDescending()
                           .Select(d => System.IO.Path.Combine(d, "bin", "remote-viewer.exe"))
                : []
        ];
        return candidates.FirstOrDefault(File.Exists) ?? configured;
    }

    /// <summary>
    /// The password the user signed in to MOAVI Desk with, for the desktop's Windows logon - only when
    /// the .rdp names that same account (MOAVI Desk and the desktops share the directory) and goes
    /// straight to the desktop. Through Warpgate the user name is a one-time ticket for the bastion,
    /// so nothing is passed there.
    /// </summary>
    private static string? SignInPassword(DeskApi api, ConnectInfo c)
    {
        if (c.Via == "warpgate" || string.IsNullOrEmpty(api.SignInPassword)) { return null; }
        var user = RdpWindow.Parse(c.Rdp ?? "").GetValueOrDefault("username", "");
        var account = user.Contains('\\') ? user[(user.LastIndexOf('\\') + 1)..] : user.Split('@')[0];
        return account.Length > 0 && string.Equals(account, api.Username, StringComparison.OrdinalIgnoreCase)
            ? api.SignInPassword
            : null;
    }

    private static (string Error, Process? Process) OpenRdp(string rdp, bool fullScreen)
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moavi-{Guid.NewGuid():N}.rdp");
        File.WriteAllText(file, rdp);
        try
        {
            var mstsc = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
            var p = Process.Start(new ProcessStartInfo(File.Exists(mstsc) ? mstsc : "mstsc.exe", $"\"{file}\"") { UseShellExecute = false });
            // mstsc reads the file when it starts; it holds the username and address, so it does not stay behind
            _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => { try { File.Delete(file); } catch { } });
            return p is null ? (L("ViewerLaunchFailed"), null) : (string.Empty, p);
        }
        catch (Exception ex)
        {
            try { File.Delete(file); } catch { }
            return (ex.Message, null);
        }
    }

    /// <summary>The .rdp without the devices the user left out (it can only take away: what the pool does
    /// not allow is not in the file, and the desktop refuses it anyway).</summary>
    public static string WithDevices(string rdp, MoaviDesk.Config.DeviceChoice d)
    {
        var lines = rdp.Replace("\r\n", "\n").Split('\n').ToList();
        void Set(string key, string value)
        {
            var i = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { lines[i] = $"{key}:{value}"; } else { lines.Insert(Math.Max(0, lines.Count - 1), $"{key}:{value}"); }
        }
        if (!d.Drives) { Set("drivestoredirect", "s:"); Set("redirectdrives", "i:0"); }
        if (!d.Printers) { Set("redirectprinters", "i:0"); }
        if (!d.Usb)
        {
            lines.RemoveAll(l => l.StartsWith("usbdevicestoredirect:", StringComparison.OrdinalIgnoreCase)
                                 || l.StartsWith("devicestoredirect:", StringComparison.OrdinalIgnoreCase)
                                 || l.StartsWith("camerastoredirect:", StringComparison.OrdinalIgnoreCase));
        }
        return string.Join("\r\n", lines);
    }

    private static string AddFullScreen(string vv, bool fullScreen)
    {
        if (!fullScreen || vv.Contains("fullscreen=", StringComparison.Ordinal)) { return vv; }
        var i = vv.IndexOf("[virt-viewer]", StringComparison.Ordinal);
        return i < 0 ? vv : vv.Insert(i + "[virt-viewer]".Length, "\nfullscreen=1");
    }

    private static string VncFile(int port, string password, string title, bool fullScreen)
        => string.Join('\n',
                       "[virt-viewer]",
                       "type=vnc",
                       "host=127.0.0.1",
                       $"port={port}",
                       $"password={password}",
                       $"title={title.Replace('\n', ' ')}",
                       "delete-this-file=1",
                       $"fullscreen={(fullScreen ? 1 : 0)}",
                       "toggle-fullscreen=shift+f11",
                       "release-cursor=ctrl+alt",
                       "") ;

    private static string WriteTemp(string content)
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moavi-{Guid.NewGuid():N}.vv");
        File.WriteAllText(file, content);
        return file;
    }

    /// <summary>
    /// One local TCP port ↔ the engine's console websocket (binary RFB frames). Accepts one
    /// viewer connection; the websocket opens when the viewer connects, so the short-lived
    /// console token is used straight away.
    /// </summary>
    private sealed class VncBridge : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public int Port { get; }

        private VncBridge(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public static Task<VncBridge> StartAsync(DeskApi api, string wsPath)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var b = new VncBridge(listener);
            _ = b.RunAsync(api, wsPath);
            return Task.FromResult(b);
        }

        private async Task RunAsync(DeskApi api, string wsPath)
        {
            try
            {
                using var tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                _listener.Stop();
                tcp.NoDelay = true;

                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Origin", api.BaseUri.GetLeftPart(UriPartial.Authority));
                var cookie = api.CookieHeader;
                if (cookie.Length > 0) { ws.Options.SetRequestHeader("Cookie", cookie); }
                if (api.IgnoreCertificateErrors)
                {
                    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
                var wsUri = new UriBuilder(new Uri(api.BaseUri, wsPath.TrimStart('/')))
                {
                    Scheme = api.BaseUri.Scheme == "http" ? "ws" : "wss"
                }.Uri;
                await ws.ConnectAsync(wsUri, _cts.Token);

                var net = tcp.GetStream();
                var up = Task.Run(async () =>
                {
                    var buf = new byte[64 * 1024];
                    int n;
                    while ((n = await net.ReadAsync(buf, _cts.Token)) > 0)
                    {
                        await ws.SendAsync(buf.AsMemory(0, n), WebSocketMessageType.Binary, true, _cts.Token);
                    }
                });
                var down = Task.Run(async () =>
                {
                    var buf = new byte[256 * 1024];
                    while (ws.State == WebSocketState.Open)
                    {
                        var r = await ws.ReceiveAsync(buf.AsMemory(), _cts.Token);
                        if (r.MessageType == WebSocketMessageType.Close) { break; }
                        // a text frame here is the engine refusing (expired token, no permission)
                        if (r.MessageType == WebSocketMessageType.Text) { break; }
                        await net.WriteAsync(buf.AsMemory(0, r.Count), _cts.Token);
                    }
                });
                await Task.WhenAny(up, down);
            }
            catch { /* the viewer shows the dropped connection */ }
            finally { await _cts.CancelAsync(); }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}
