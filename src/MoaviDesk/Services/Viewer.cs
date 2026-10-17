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
/// RDP: the engine hands an .rdp file → Windows' own Remote Desktop client (mstsc) opens it; mstsc
/// measures the link and picks UDP and H.264/AVC when the desktop offers them.
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
                                                                       string viewerPath, string title, bool fullScreen)
    {
        if (c.Type == "rdp") { return OpenRdp(c.Rdp ?? "", fullScreen); }

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

    private static (string Error, Process? Process) OpenRdp(string rdp, bool fullScreen)
    {
        if (!OperatingSystem.IsWindows() || rdp.Length == 0) { return (L("RdpNeedsWindows"), null); }
        if (!fullScreen) { rdp = rdp.Replace("screen mode id:i:2", "screen mode id:i:1", StringComparison.Ordinal); }
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
