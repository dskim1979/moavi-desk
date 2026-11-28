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

    /// <summary>moavi-desk.exe --rdp &lt;file&gt; [--title &lt;name&gt;] [--full] [--password-stdin] [--wait]
    /// With --wait the window opens at once ('연결하는 중') and the file comes when the engine answered
    /// (RdpLauncher.Start / Pending.Deliver): the control loads meanwhile, the user sees the window right away.</summary>
    public static int Run(string[] args, MoaviDesk.Config.AppConfig config)
    {
        string Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        var file = Arg(Switch);
        var title = Arg("--title");
        var wait = args.Contains("--wait");
        Dictionary<string, string>? settings = null;
        if (!wait)
        {
            try { settings = Parse(File.ReadAllText(file)); }
            catch { return 2; }
            finally { try { File.Delete(file); } catch { } }
        }
        // the password line (empty = none) comes when the parent knows the .rdp
        var password = args.Contains("--password-stdin") ? Task.Run(() => Console.In.ReadLine()) : Task.FromResult<string?>(null);

        WF.Application.EnableVisualStyles();
        WF.Application.SetHighDpiMode(WF.HighDpiMode.PerMonitorV2);
        using var form = new RdpForm(settings, wait ? file : null, title, password, args.Contains("--full"), config);
        WF.Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>The .rdp lines as key → value ("full address:s:host:port" → "full address" → "host:port").</summary>
    public static Dictionary<string, string> Parse(string rdp) => Viewer.ParseRdp(rdp);

    /// <summary>Every connection opens in MOAVI Desk's own window - Windows' Remote Desktop app is never
    /// started. The bundled RD Gateway's access token goes into the control as its pre-authentication
    /// cookie (RdpForm.Gateway). RemoteFX USB (usbdevicestoredirect / camerastoredirect) is the one
    /// thing only mstsc has: here USB storage comes as drives and phones / cameras as plug-and-play
    /// devices, as far as the pool allows them.</summary>
    public static bool CanEmbed(Dictionary<string, string> s) => true;

    /// <summary>The RD Gateway access token as the control takes it (GatewayEncryptedAuthCookie): the
    /// token in UTF-16 with its terminating zero, protected with DPAPI for this Windows user, base64.</summary>
    public static string GatewayCookie(string token)
    {
        var clear = System.Text.Encoding.Unicode.GetBytes(token + '\0');
        try
        {
            return Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
                clear, null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        }
        finally { Array.Clear(clear); }
    }

    private static readonly System.Drawing.Text.PrivateFontCollection Fonts = LoadFonts();

    private static System.Drawing.Text.PrivateFontCollection LoadFonts()
    {
        var pfc = new System.Drawing.Text.PrivateFontCollection();
        foreach (var f in new[] { "Paperlogy-4Regular.ttf", "Paperlogy-7Bold.ttf" })
        {
            try
            {
                using var stream = typeof(RdpWindow).Assembly.GetManifestResourceStream(f);
                if (stream is null) { continue; }
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                // GDI+ keeps reading the font from this memory: it stays allocated for the process
                var mem = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, mem, bytes.Length);
                pfc.AddMemoryFont(mem, bytes.Length);
            }
            catch { /* Malgun Gothic then */ }
        }
        return pfc;
    }

    private static SD.Font UiFont(float size, SD.FontStyle style = SD.FontStyle.Regular)
        => Fonts.Families.Length > 0 ? new SD.Font(Fonts.Families[0], size, style) : new SD.Font("Malgun Gothic", size, style);

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
        IMsRdpDeviceCollection GetDeviceCollection();
        IMsRdpDriveCollection GetDriveCollection();
    }

    // the plug-and-play devices and the drives this PC passes, switched on and off during the session
    // (mstscax.idl; IUnknown interfaces, so declared with their whole vtables)
    [ComImport, Guid("56540617-d281-488c-8738-6a8fdf64a118"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpDeviceCollection
    {
        void RescanDevices(short dynRedir);
        IMsRdpDevice GetDeviceByIndex(uint index);
        IMsRdpDevice GetDeviceById([MarshalAs(UnmanagedType.BStr)] string id);
        uint GetDeviceCount();
    }

    [ComImport, Guid("60c3b9c8-9e92-4f5e-a3e7-604a912093ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpDevice
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetDeviceInstanceId();
        [return: MarshalAs(UnmanagedType.BStr)] string GetFriendlyName();
        [return: MarshalAs(UnmanagedType.BStr)] string GetDeviceDescription();
        void SetRedirectionState(short value);
        short GetRedirectionState();
    }

    [ComImport, Guid("7ff17599-da2c-4677-ad35-f60c04fe1585"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpDriveCollection
    {
        void RescanDrives(short dynRedir);
        IMsRdpDrive GetDriveByIndex(uint index);
        uint GetDriveCount();
    }

    [ComImport, Guid("d28b5458-f694-47a8-8e61-40356a767e46"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMsRdpDrive
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        void SetRedirectionState(short value);
        short GetRedirectionState();
    }

    private sealed class RdpForm : WF.Form
    {
        private static readonly SD.Color Bar = SD.ColorTranslator.FromHtml("#111B30");
        private static readonly SD.Color Ink = SD.ColorTranslator.FromHtml("#F1F5F9");
        private static readonly SD.Color Muted = SD.ColorTranslator.FromHtml("#B8C4D6");
        private static readonly SD.Color Accent = SD.ColorTranslator.FromHtml("#53B1B1");

        private Dictionary<string, string> _s;
        private readonly string? _pending;                  // --wait: the .rdp still to come
        private readonly Task<string?> _passwordLine;
        private readonly WF.Timer _arrive = new() { Interval = 100 };
        private readonly string _name;
        private string? _password;
        private readonly bool _full;
        private readonly MoaviDesk.Config.AppConfig _config;
        private readonly WF.Label _status;
        private readonly WF.Timer _watch = new() { Interval = 500 };
        private readonly WF.Timer _resize = new() { Interval = 400 };
        private readonly WF.Button _fullBtn;
        private bool _isFull;
        private SD.Rectangle _windowed;
        private RdpHost? _host;
        private dynamic? _rdp;
        private bool _wasConnected;
        private bool _closing;

        public int ExitCode { get; private set; }

        public RdpForm(Dictionary<string, string>? settings, string? pending, string name, Task<string?> password, bool full, MoaviDesk.Config.AppConfig config)
        {
            _s = settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _pending = pending;
            _passwordLine = password;
            _name = name.Length > 0 ? name : "MOAVI Desk";
            _full = full;
            _config = config;
            Text = $"{_name} - MOAVI Desk";
            BackColor = SD.Color.Black;
            StartPosition = WF.FormStartPosition.CenterScreen;
            var area = WF.Screen.PrimaryScreen?.WorkingArea ?? new SD.Rectangle(0, 0, 1600, 900);
            Size = new SD.Size(Math.Min(1600, area.Width * 9 / 10), Math.Min(960, area.Height * 9 / 10));
            // full screen is this window itself (borderless over the whole screen, the MOAVI Desk bar on top) -
            // never the control's own full screen, which put up Windows' Remote Desktop window and its blue bar
            if (_full) { Load += (_, _) => SetFull(true); }
            try { Icon = SD.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }

            var bar = new WF.Panel { Dock = WF.DockStyle.Top, Height = 40, BackColor = Bar, Padding = new WF.Padding(12, 0, 8, 0) };
            var title = new WF.Label
            {
                Text = $"● {_name}", ForeColor = Ink, AutoSize = true, Dock = WF.DockStyle.Left,
                Font = UiFont(10f, SD.FontStyle.Bold), TextAlign = SD.ContentAlignment.MiddleLeft,
                Padding = new WF.Padding(0, 11, 12, 0)
            };
            _status = new WF.Label
            {
                Text = L("RdpConnecting"), ForeColor = Muted, AutoSize = true, Dock = WF.DockStyle.Left,
                Font = UiFont(9f), Padding = new WF.Padding(0, 12, 0, 0)
            };
            var buttons = new WF.FlowLayoutPanel
            {
                Dock = WF.DockStyle.Right, AutoSize = true, FlowDirection = WF.FlowDirection.LeftToRight,
                WrapContents = false, BackColor = Bar, Padding = new WF.Padding(0, 5, 0, 0)
            };
            var devices = BarButton(L("RdpDevices"), (_, _) => { });
            devices.Click += (_, _) => ShowDevices(devices);
            buttons.Controls.Add(devices);
            buttons.Controls.Add(BarButton(L("RdpCtrlAltDel"), (_, _) => SendCtrlAltDel()));
            _fullBtn = BarButton(L("RdpFullScreen"), (_, _) => SetFull(!_isFull));
            buttons.Controls.Add(_fullBtn);
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
                Font = UiFont(9f), Margin = new WF.Padding(4, 0, 0, 0), Height = 30, Cursor = WF.Cursors.Hand
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
            if (_pending is null) { Go(); return; }
            // the window is up and the control loaded: the .rdp follows when the engine answered
            var until = DateTime.UtcNow.AddSeconds(90);
            _arrive.Tick += (_, _) =>
            {
                if (File.Exists(_pending))
                {
                    _arrive.Stop();
                    try { _s = Parse(File.ReadAllText(_pending)); }
                    catch (Exception ex) { Fail(ex.Message); return; }
                    finally { try { File.Delete(_pending); } catch { } }
                    Go();
                }
                else if (DateTime.UtcNow > until) { _arrive.Stop(); Fail(L("RdpFailed")); }
            };
            _arrive.Start();
        }

        private async void Go()
        {
            try
            {
                var line = await _passwordLine;
                _password = string.IsNullOrEmpty(line) ? null : line;
                Configure(); _rdp!.Connect(); _watch.Start();
            }
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
            r.ColorDepth = _config.RdpColorDepth is 32 or 24 or 16 or 15 ? _config.RdpColorDepth : 32;   // Settings › RDP
            var size = _host!.ClientSize;
            r.DesktopWidth = Math.Max(800, size.Width);
            r.DesktopHeight = Math.Max(600, size.Height);

            // what the pool policy put in the file
            r.AdvancedSettings2.RedirectDrives = S("drivestoredirect").Length > 0 || On("redirectdrives");
            r.AdvancedSettings2.RedirectPrinters = On("redirectprinters");
            r.AdvancedSettings2.RedirectSmartCards = On("redirectsmartcards");
            r.AdvancedSettings2.RedirectPorts = On("redirectcomports");
            r.AdvancedSettings6.RedirectClipboard = On("redirectclipboard", true);
            r.SecuredSettings2.AudioRedirectionMode = _config.RdpAudio switch { "remote" => 1, "off" => 2, _ => 0 };
            try { r.AdvancedSettings8.AudioCaptureRedirectionMode = _config.RdpMicrophone; } catch { }
            r.AdvancedSettings5.AuthenticationLevel = int.TryParse(S("authentication level", "2"), out var auth) ? auth : 2;
            r.AdvancedSettings.Compress = 1;
            r.AdvancedSettings2.BitmapPeristence = 1;
            r.AdvancedSettings2.DisplayConnectionBar = false;   // the control's own full screen is not used
            r.AdvancedSettings2.EnableAutoReconnect = true;
            // auto measures the link and picks UDP and H.264 when offered; the others pin the experience level
            var net = _config.RdpNetwork switch { "lan" => 6, "broadband" => 4, "wan" => 5, _ => 7 };
            try { r.AdvancedSettings8.NetworkConnectionType = net; } catch { }
            try { r.AdvancedSettings9.BandwidthDetection = true; } catch { }

            var gateway = S("gatewayhostname");
            if (gateway.Length > 0)
            {
                r.TransportSettings.GatewayHostname = gateway;
                r.TransportSettings.GatewayUsageMethod = int.TryParse(S("gatewayusagemethod", "1"), out var gu) ? gu : 1;
                r.TransportSettings.GatewayProfileUsageMethod = 1;
                var token = S("gatewayaccesstoken");
                if (token.Length > 0)
                {
                    // the bundled rdpgw (장치 연결 방식): a short-lived token the engine signed instead of a gateway sign-in
                    r.TransportSettings.GatewayCredsSource = 5;   // cookie-based (pre-authentication)
                    var cookie = GatewayCookie(token);
                    r.TransportSettings.GatewayEncryptedAuthCookie = cookie;
                    r.TransportSettings.GatewayEncryptedAuthCookieSize = (uint)cookie.Length;
                }
                else
                {
                    r.TransportSettings.GatewayCredsSource = int.TryParse(S("gatewaycredentialssource", "4"), out var gc) ? gc : 4;
                }
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
            // drives the policy allows: a USB stick or disk plugged in during the session shows up too
            if (S("drivestoredirect").Length > 0 || On("redirectdrives")) { ns.SetRedirectDynamicDrives(1); }
            // plug-and-play devices (phones, cameras as MTP/PTP) when the file allows devices
            if (S("devicestoredirect").Length > 0) { ns.SetRedirectDynamicDevices(1); }
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
                    FitSession();
                }
                _status.Text = L("RdpConnected");
                _status.ForeColor = Muted;
                return;
            }
            // the control reconnects by itself after a dropped link (EnableAutoReconnect)
            if (state == 2 && _wasConnected) { _status.Text = L("RdpReconnecting"); _status.ForeColor = SD.ColorTranslator.FromHtml("#F0A35E"); return; }
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
            var scale = (uint)(_config.RdpScale is >= 100 and <= 500 ? _config.RdpScale : Math.Clamp(DeviceDpi * 100 / 96, 100, 500));
            var device = scale >= 175 ? 180u : scale >= 125 ? 140u : 100u;
            try { _rdp.UpdateSessionDisplaySettings((uint)s.Width, (uint)s.Height, (uint)s.Width, (uint)s.Height, 0u, scale, device); }
            catch { /* older control or the desktop does not resize: it keeps its size */ }
        }

        // 장치: the drives and USB / plug-and-play devices of this PC, each switched on or off now; printers
        // and the clipboard as the connection has them (those are set when it connects)
        private void ShowDevices(WF.Control anchor)
        {
            var menu = new WF.ContextMenuStrip { Font = UiFont(9.5f), ShowCheckMargin = true, ShowImageMargin = false };
            WF.ToolStripMenuItem Title(string text) => new(text) { Enabled = false, Font = UiFont(9f, SD.FontStyle.Bold) };
            try
            {
                if (_rdp is null) { throw new InvalidOperationException(); }
                var ns = (IMsRdpClientNonScriptable3)(object)_rdp;
                menu.Items.Add(Title(L("RdpDevDrives")));
                var drives = ns.GetDriveCollection();
                drives.RescanDrives(1);
                var nd = drives.GetDriveCount();
                for (uint i = 0; i < nd; i++)
                {
                    var drive = drives.GetDriveByIndex(i);
                    var item = new WF.ToolStripMenuItem(drive.GetName()) { Checked = drive.GetRedirectionState() != 0, CheckOnClick = true };
                    item.CheckedChanged += (_, _) => { try { drive.SetRedirectionState((short)(item.Checked ? -1 : 0)); } catch { } };
                    menu.Items.Add(item);
                }
                if (nd == 0) { menu.Items.Add(new WF.ToolStripMenuItem(L("RdpDevNone")) { Enabled = false }); }
                menu.Items.Add(new WF.ToolStripSeparator());
                menu.Items.Add(Title(L("RdpDevUsb")));
                var devs = ns.GetDeviceCollection();
                devs.RescanDevices(1);
                var nv = devs.GetDeviceCount();
                for (uint i = 0; i < nv; i++)
                {
                    var dev = devs.GetDeviceByIndex(i);
                    var name = dev.GetFriendlyName();
                    if (string.IsNullOrWhiteSpace(name)) { name = dev.GetDeviceDescription(); }
                    var item = new WF.ToolStripMenuItem(name) { Checked = dev.GetRedirectionState() != 0, CheckOnClick = true };
                    item.CheckedChanged += (_, _) => { try { dev.SetRedirectionState((short)(item.Checked ? -1 : 0)); } catch { } };
                    menu.Items.Add(item);
                }
                if (nv == 0) { menu.Items.Add(new WF.ToolStripMenuItem(L("RdpDevNone")) { Enabled = false }); }
            }
            catch
            {
                menu.Items.Clear();
                menu.Items.Add(new WF.ToolStripMenuItem(L("RdpDevUnread")) { Enabled = false });
            }
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem(string.Format(L("RdpDevPrinters"), On("redirectprinters") ? L("RdpDevOn") : L("RdpDevOff"))) { Enabled = false });
            menu.Items.Add(new WF.ToolStripMenuItem(string.Format(L("RdpDevClipboard"), On("redirectclipboard", true) ? L("RdpDevOn") : L("RdpDevOff"))) { Enabled = false });
            menu.Show(anchor, new SD.Point(0, anchor.Height));
        }

        // full screen: this window without its frame over the whole screen it is on; again = back to the window
        private void SetFull(bool on)
        {
            if (on == _isFull) { return; }
            if (on)
            {
                _windowed = WindowState == WF.FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = WF.FormWindowState.Normal;
                FormBorderStyle = WF.FormBorderStyle.None;
                Bounds = WF.Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = WF.FormBorderStyle.Sizable;
                Bounds = _windowed.Width > 0 ? _windowed : new SD.Rectangle(100, 100, 1280, 800);
            }
            _isFull = on;
            _fullBtn.Text = L(on ? "RdpWindowed" : "RdpFullScreen");
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
    /// <summary>A window opened before the engine answered: Deliver hands it the .rdp, Cancel closes it.</summary>
    internal sealed class Pending(Process process, string path) : IEarlyRdp
    {
        public Process Process { get; } = process;
        public RemoteSession Session { get; } = RemoteSession.Of(process);

        public void Deliver(string rdp, string? password)
        {
            var part = path + ".part";
            File.WriteAllText(part, rdp);
            File.Move(part, path, overwrite: true);          // the window sees it whole or not at all
            try { Process.StandardInput.WriteLine(password ?? ""); Process.StandardInput.Close(); } catch { }
        }

        public void Cancel()
        {
            try { if (!Process.HasExited) { Process.Kill(); } } catch { }
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>The window at the click: it shows '연결하는 중' and loads the Remote Desktop control while
    /// the engine prepares the desktop. Null when it cannot start (the normal Open then).</summary>
    public static Pending? Start(string title, bool fullScreen)
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } self) { return null; }
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"moavi-{Guid.NewGuid():N}.rdp");
        var psi = new ProcessStartInfo(self) { UseShellExecute = false, RedirectStandardInput = true };
        foreach (var a in new[] { RdpWindow.Switch, file, "--title", title, "--wait", "--password-stdin" }) { psi.ArgumentList.Add(a); }
        if (fullScreen) { psi.ArgumentList.Add("--full"); }
        try { return Process.Start(psi) is { } p ? new Pending(p, file) : null; }
        catch { return null; }
    }

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

/// <summary>Windows: the Remote Desktop control in MOAVI Desk's own window (RdpWindow, a process per desktop).</summary>
internal sealed class WindowsRdp : IRdpClient
{
    public IEarlyRdp? Start(string title, bool fullScreen) => RdpLauncher.Start(title, fullScreen);

    public Task<(string Error, RemoteSession? Session)> OpenAsync(string rdp, string title, bool fullScreen, string? password, DevicePolicy allowed)
    {
        var (embedded, error, process) = RdpLauncher.Open(rdp, title, fullScreen, password);
        if (!embedded) { (error, process) = OpenMstsc(rdp); }
        return Task.FromResult((error, process is null ? null : RemoteSession.Of(process)));
    }

    private static (string Error, Process? Process) OpenMstsc(string rdp)
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
}
