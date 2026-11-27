/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace MoaviDesk.Services;

/// <summary>
/// A desktop in MOAVI Desk's own window: Windows' Remote Desktop ActiveX control (mstscax.dll, the
/// engine mstsc itself uses - same H.264 · UDP, clipboard, drives, printers) hosted in a WinForms
/// window with the MOAVI Desk bar on top. It runs as its own process (moavi-desk.exe --rdp &lt;file&gt;)
/// so the main window keeps tracking it as a Process, exactly like remote-viewer and mstsc before.
/// The engine's .rdp file is read once and deleted; the signed-in user's password, when the client
/// passes one, comes on stdin and only ever goes into the control.
/// </summary>
internal static class RdpWindow
{
    public const string Switch = "--rdp";

    // MsRdpClient10 / 9 / 8 NotSafeForScripting: the newest one this Windows has
    private static readonly string[] Clsids =
    [
        "a0c63c30-f08d-4ab4-907c-34905d770c7d",
        "8b918b82-7985-4c24-89df-c33ad2bbfbcd",
        "54d38bf7-b1ef-4479-9674-1bd6ea465258",
    ];

    /// <summary>moavi-desk.exe --rdp &lt;file&gt; [--title &lt;name&gt;] [--full] [--password-stdin]</summary>
    public static int Run(string[] args)
    {
        string Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        var file = Arg(Switch);
        var title = Arg("--title");
        string rdp;
        try { rdp = File.ReadAllText(file); }
        catch { return 2; }
        finally { try { File.Delete(file); } catch { } }
        var password = args.Contains("--password-stdin") ? Console.In.ReadLine() : null;

        WF.Application.EnableVisualStyles();
        WF.Application.SetHighDpiMode(WF.HighDpiMode.PerMonitorV2);
        using var form = new RdpForm(Parse(rdp), title, password, args.Contains("--full"));
        WF.Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>The .rdp lines as key → value ("full address:s:host:port" → "full address" → "host:port").</summary>
    public static Dictionary<string, string> Parse(string rdp)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in rdp.Split('\n'))
        {
            var line = raw.Trim();
            var a = line.IndexOf(':');
            if (a <= 0 || a + 2 >= line.Length || line[a + 2] != ':') { continue; }
            d[line[..a]] = line[(a + 3)..];
        }
        return d;
    }

    /// <summary>True when the control can open this file. An RD Gateway with an access token
    /// (the bundled rdpgw) is mstsc-only: the control has no way to take the token.</summary>
    public static bool CanEmbed(Dictionary<string, string> s)
        => !s.TryGetValue("gatewayaccesstoken", out var t) || t.Length == 0;

    private sealed class RdpHost : WF.AxHost
    {
        public RdpHost(string clsid) : base(clsid) { }
        public object? Ocx => GetOcx();
    }

    /// <summary>IMsRdpClientNonScriptable3 (mstscax.idl) with its whole vtable: the password and the
    /// CredSSP switch are only on this IUnknown interface, not on the scriptable dispatch one.</summary>
    [ComImport, Guid("B3378D90-0728-45C7-8ED7-B6159FB92219"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpClientNonScriptable3
    {
        // IMsTscNonScriptable
        void SetClearTextPassword([MarshalAs(UnmanagedType.BStr)] string value);
        void SetPortablePassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetPortablePassword();
        void SetPortableSalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetPortableSalt();
        void SetBinaryPassword([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetBinaryPassword();
        void SetBinarySalt([MarshalAs(UnmanagedType.BStr)] string value);
        [return: MarshalAs(UnmanagedType.BStr)] string GetBinarySalt();
        void ResetPassword();
        // IMsRdpClientNonScriptable
        void NotifyRedirectDeviceChange(nuint wParam, nint lParam);
        void SendKeys(int numKeys, IntPtr keyUp, IntPtr keyData);
        // IMsRdpClientNonScriptable2
        void SetUIParentWindowHandle(IntPtr value);
        IntPtr GetUIParentWindowHandle();
        // IMsRdpClientNonScriptable3
        void SetShowRedirectionWarningDialog(short value);
        short GetShowRedirectionWarningDialog();
        void SetPromptForCredentials(short value);
        short GetPromptForCredentials();
        void SetNegotiateSecurityLayer(short value);
        short GetNegotiateSecurityLayer();
        void SetEnableCredSspSupport(short value);
        short GetEnableCredSspSupport();
        void SetRedirectDynamicDrives(short value);
        short GetRedirectDynamicDrives();
        void SetRedirectDynamicDevices(short value);
        short GetRedirectDynamicDevices();
    }

    private sealed class RdpForm : WF.Form
    {
        private static readonly SD.Color Bar = SD.ColorTranslator.FromHtml("#111B30");
        private static readonly SD.Color Ink = SD.ColorTranslator.FromHtml("#F1F5F9");
        private static readonly SD.Color Muted = SD.ColorTranslator.FromHtml("#B8C4D6");
        private static readonly SD.Color Accent = SD.ColorTranslator.FromHtml("#53B1B1");

        private readonly Dictionary<string, string> _s;
        private readonly string _name;
        private readonly string? _password;
        private readonly bool _full;
        private readonly WF.Label _status;
        private readonly WF.Timer _watch = new() { Interval = 500 };
        private readonly WF.Timer _resize = new() { Interval = 400 };
        private RdpHost? _host;
        private dynamic? _rdp;
        private bool _wasConnected;
        private bool _closing;

        public int ExitCode { get; private set; }

        public RdpForm(Dictionary<string, string> settings, string name, string? password, bool full)
        {
            _s = settings;
            _name = name.Length > 0 ? name : "MOAVI Desk";
            _password = password;
            _full = full;
            Text = $"{_name} - MOAVI Desk";
            BackColor = SD.Color.Black;
            StartPosition = WF.FormStartPosition.CenterScreen;
            var area = WF.Screen.PrimaryScreen?.WorkingArea ?? new SD.Rectangle(0, 0, 1600, 900);
            Size = new SD.Size(Math.Min(1600, area.Width * 9 / 10), Math.Min(960, area.Height * 9 / 10));
            try { Icon = SD.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

            var bar = new WF.Panel { Dock = WF.DockStyle.Top, Height = 40, BackColor = Bar, Padding = new WF.Padding(12, 0, 8, 0) };
            var title = new WF.Label
            {
                Text = $"● {_name}", ForeColor = Ink, AutoSize = true, Dock = WF.DockStyle.Left,
                Font = new SD.Font("Malgun Gothic", 10f, SD.FontStyle.Bold), TextAlign = SD.ContentAlignment.MiddleLeft,
                Padding = new WF.Padding(0, 11, 12, 0)
            };
            _status = new WF.Label
            {
                Text = L("RdpConnecting"), ForeColor = Muted, AutoSize = true, Dock = WF.DockStyle.Left,
                Font = new SD.Font("Malgun Gothic", 9f), Padding = new WF.Padding(0, 12, 0, 0)
            };
            var buttons = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Right, AutoSize = true, FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false, BackColor = Bar, Padding = new WF.Padding(0, 5, 0, 0)
            };
            buttons.Controls.Add(BarButton(L("RdpCtrlAltDel"), (_, _) => SendCtrlAltDel()));
            buttons.Controls.Add(BarButton(L("RdpFullScreen"), (_, _) => GoFullScreen()));
            buttons.Controls.Add(BarButton(L("RdpDisconnect"), (_, _) => Close(), Accent));
            bar.Controls.Add(_status);
            bar.Controls.Add(title);
            bar.Controls.Add(buttons);
            Controls.Add(bar);

            _watch.Tick += (_, _) => Watch();
            _resize.Tick += (_, _) => { _resize.Stop(); FitSession(); };
            Resize += (_, _) => { if (_wasConnected) { _resize.Stop(); _resize.Start(); } };
        }

        private static WF.Button BarButton(string text, EventHandler click, SD.Color? fore = null)
        {
            var b = new WF.Button
            {
                Text = text, AutoSize = true, FlatStyle = WF.FlatStyle.Flat, ForeColor = fore ?? Ink, BackColor = Bar,
                Font = new SD.Font("Malgun Gothic", 9f), Margin = new WF.Padding(4, 0, 0, 0), Height = 30, Cursor = WF.Cursors.Hand
            };
            b.FlatAppearance.BorderColor = SD.ColorTranslator.FromHtml("#2E3D5C");
            b.Click += click;
            return b;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            foreach (var clsid in Clsids)
            {
                var host = new RdpHost(clsid) { Dock = WF.DockStyle.Fill };
                try
                {
                    Controls.Add(host);
                    host.BringToFront();
                    host.CreateControl();
                    if (host.Ocx is { } ocx) { _host = host; _rdp = ocx; break; }
                }
                catch { /* this Windows lacks that version */ }
                Controls.Remove(host);
                host.Dispose();
            }
            if (_rdp is null) { Fail(L("RdpNoControl")); return; }
            try { Configure(); _rdp.Connect(); _watch.Start(); }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private string S(string key, string fallback = "") => _s.TryGetValue(key, out var v) ? v : fallback;
        private bool On(string key, bool fallback = false) => _s.TryGetValue(key, out var v) ? v.Trim() == "1" : fallback;

        private void Configure()
        {
            var r = _rdp!;
            var address = S("full address");
            var port = 3389;
            var colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address[(colon + 1)..], out var p)) { port = p; address = address[..colon]; }
            r.Server = address;
            r.AdvancedSettings2.RDPPort = port;
            var user = S("username");
            if (user.Length > 0) { r.UserName = user; }
            if (S("domain").Length > 0) { r.Domain = S("domain"); }
            r.FullScreenTitle = $"{_name} - MOAVI Desk";
            r.ConnectingText = L("RdpConnecting");
            r.ColorDepth = int.TryParse(S("session bpp", "32"), out var bpp) ? bpp : 32;
            var size = _host!.ClientSize;
            r.DesktopWidth = Math.Max(800, size.Width);
            r.DesktopHeight = Math.Max(600, size.Height);

            // what the pool policy put in the file
            r.AdvancedSettings2.RedirectDrives = S("drivestoredirect").Length > 0 || On("redirectdrives");
            r.AdvancedSettings2.RedirectPrinters = On("redirectprinters");
            r.AdvancedSettings2.RedirectSmartCards = On("redirectsmartcards");
            r.AdvancedSettings2.RedirectPorts = On("redirectcomports");
            r.AdvancedSettings6.RedirectClipboard = On("redirectclipboard", true);
            r.SecuredSettings2.AudioRedirectionMode = int.TryParse(S("audiomode", "0"), out var audio) ? audio : 0;
            r.AdvancedSettings5.AuthenticationLevel = int.TryParse(S("authentication level", "2"), out var auth) ? auth : 2;
            r.AdvancedSettings.Compress = 1;
            r.AdvancedSettings2.BitmapPeristence = 1;
            r.AdvancedSettings2.DisplayConnectionBar = true;
            r.AdvancedSettings2.EnableAutoReconnect = true;
            try { r.AdvancedSettings8.NetworkConnectionType = 7; } catch { }        // auto-detect: UDP, H.264 when offered
            try { r.AdvancedSettings9.BandwidthDetection = true; } catch { }

            var gateway = S("gatewayhostname");
            if (gateway.Length > 0)
            {
                r.TransportSettings.GatewayHostname = gateway;
                r.TransportSettings.GatewayUsageMethod = int.TryParse(S("gatewayusagemethod", "1"), out var gu) ? gu : 1;
                r.TransportSettings.GatewayProfileUsageMethod = 1;
                r.TransportSettings.GatewayCredsSource = int.TryParse(S("gatewaycredentialssource", "4"), out var gc) ? gc : 4;
            }

            var ns = (IMsRdpClientNonScriptable3)(object)r;
            // Warpgate's listener is TLS without NLA and reads the ticket from the user name
            if (S("enablecredsspsupport") == "0") { ns.SetEnableCredSspSupport(0); }
            if (!string.IsNullOrEmpty(_password))
            {
                ns.SetClearTextPassword(_password);
                ns.SetPromptForCredentials(0);
            }
            ns.SetShowRedirectionWarningDialog(0);
        }

        private void Watch()
        {
            int state;
            try { state = (int)_rdp!.Connected; } catch { return; }
            if (state == 1)
            {
                if (!_wasConnected)
                {
                    _wasConnected = true;
                    _status.Text = L("RdpConnected");
                    if (_full) { GoFullScreen(); }
                }
                return;
            }
            if (state == 0 && !_closing)
            {
                _watch.Stop();
                if (_wasConnected) { Close(); return; }
                Fail(Reason());
            }
        }

        private string Reason()
        {
            try
            {
                int ext = (int)_rdp!.ExtendedDisconnectReason;
                return ext != 0 ? $"{L("RdpFailed")} ({ext})" : L("RdpFailed");
            }
            catch { return L("RdpFailed"); }
        }

        private void FitSession()
        {
            if (_rdp is null || _host is null) { return; }
            var s = _host.ClientSize;
            if (s.Width < 200 || s.Height < 200) { return; }
            try { _rdp.UpdateSessionDisplaySettings((uint)s.Width, (uint)s.Height, (uint)s.Width, (uint)s.Height, 0u, 100u, 100u); }
            catch { /* older control or the desktop does not resize: it keeps its size */ }
        }

        private void GoFullScreen()
        {
            try { if ((int)_rdp!.Connected == 1) { _rdp.FullScreen = true; } } catch { }
        }

        private void SendCtrlAltDel()
        {
            // the control sends Ctrl+Alt+End as the remote Ctrl+Alt+Del
            try { _host?.Focus(); WF.SendKeys.SendWait("^%{END}"); } catch { }
        }

        private void Fail(string message)
        {
            if (_host is not null) { _host.Visible = false; }   // no blank white control behind the message
            BackColor = Bar;
            _status.Text = message;
            _status.ForeColor = SD.ColorTranslator.FromHtml("#F87171");
            ExitCode = 1;
        }

        protected override void OnFormClosing(WF.FormClosingEventArgs e)
        {
            _closing = true;
            _watch.Stop();
            try { if (_rdp is not null && (int)_rdp.Connected != 0) { _rdp.Disconnect(); } } catch { }
            base.OnFormClosing(e);
        }
    }
}

/// <summary>Starts a desktop in MOAVI Desk's own window (or mstsc when the control cannot open the file).</summary>
internal static class RdpLauncher
{
    /// <summary>Embedded false: this file needs mstsc (an RD Gateway access token).</summary>
    public static (bool Embedded, string Error, Process? Process) Open(string rdp, string title, bool fullScreen, string? password)
    {
        var settings = RdpWindow.Parse(rdp);
        if (!RdpWindow.CanEmbed(settings) || Environment.ProcessPath is not { } self) { return (false, string.Empty, null); }
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moavi-{Guid.NewGuid():N}.rdp");
        File.WriteAllText(file, rdp);
        var psi = new ProcessStartInfo(self) { UseShellExecute = false, RedirectStandardInput = password is not null };
        foreach (var a in new[] { RdpWindow.Switch, file, "--title", title }) { psi.ArgumentList.Add(a); }
        if (fullScreen) { psi.ArgumentList.Add("--full"); }
        if (password is not null) { psi.ArgumentList.Add("--password-stdin"); }
        try
        {
            var p = Process.Start(psi);
            if (p is not null && password is not null)
            {
                p.StandardInput.WriteLine(password);
                p.StandardInput.Close();
            }
            return p is null ? (true, L("ViewerLaunchFailed"), null) : (true, string.Empty, p);
        }
        catch (Exception ex)
        {
            try { File.Delete(file); } catch { }
            return (true, ex.Message, null);
        }
    }
}
