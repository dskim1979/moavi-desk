/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

#pragma warning disable IDE0048   // pixel and flag arithmetic

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MoaviDesk.Config;
using MoaviDesk.UI;
using MoaviDesk.UI.Helpers;
using Page = MoaviDesk.UI.Page;
using static MoaviDesk.UI.Brand;

namespace MoaviDesk.Services;

/// <summary>
/// macOS: a desktop in MOAVI Desk's own window, as on Windows - the bar on top (연결 상태, 장치, Ctrl+Alt+Del,
/// 전체 화면, 연결 끊기) and the desktop drawn below it by FreeRDP 3 through libmoavi-rdp (installer/macos/moavi-rdp,
/// bundled in the app). The desktop follows the window's size (display channel), text copies both ways.
/// Keys go as the keyboard's physical keys (RDP scancodes): ⌘ is Ctrl (⌘C / ⌘V copy and paste in the desktop),
/// right ⌘ is 한/영. Without libmoavi-rdp the app falls back to FreeRDP's own window (FreeRdpClient).
/// </summary>
internal sealed class MacRdp(AppConfig config) : IRdpClient
{
    private const string Lib = "libmoavi-rdp";

    public static bool Available()
    {
        try { return NativeLibrary.TryLoad(System.IO.Path.Combine(AppContext.BaseDirectory, "libmoavi-rdp.dylib"), out _); }
        catch { return false; }
    }

    public IEarlyRdp? Start(string title, bool fullScreen) => null;

    public Task<(string Error, RemoteSession? Session)> OpenAsync(string rdp, string title, bool fullScreen, string? password, DevicePolicy allowed)
    {
        // drives, printers and the clipboard go through the 장치 menu (libmoavi-rdp adds and takes them away live);
        // the clipboard channel is there whenever the pool allows it
        var options = FreeRdpArgs.Build(rdp, title, false, password, config)
                                 .Where(o => o.Name is not ("f" or "t" or "home-drive" or "printer" or "clipboard" or "drive")).ToList();
        options.Add(("clipboard", allowed.Clipboard ? "+" : "-"));
        // a Korean keyboard (type 8): the desktop takes 0x72 / 0x71 as 한/영 and 한자
        if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko") { options.Add(("kbd", "layout:0x412,type:8,subtype:3")); }
        var settings = Viewer.ParseRdp(rdp);
        var view = new RdpSession(title, fullScreen, FreeRdpArgs.ToArgs(options).ToList(), settings, allowed);
        return Task.FromResult<(string, RemoteSession?)>((string.Empty, view));
    }

    // --- libmoavi-rdp ---------------------------------------------------------------------------------
    [DllImport(Lib)] private static extern unsafe IntPtr moavi_rdp_new(int argc, IntPtr argv, delegate* unmanaged<IntPtr, int, int, int, int, void> frame,
                                                                delegate* unmanaged<IntPtr, int, int, void> size,
                                                                delegate* unmanaged<IntPtr, IntPtr, void> clip,
                                                                delegate* unmanaged<IntPtr, int, IntPtr, void> state, IntPtr user);
    [DllImport(Lib)] private static extern int moavi_rdp_run(IntPtr h);
    [DllImport(Lib)] private static extern IntPtr moavi_rdp_buffer(IntPtr h, out int width, out int height, out int stride);
    [DllImport(Lib)] private static extern void moavi_rdp_mouse(IntPtr h, int flags, int x, int y);
    [DllImport(Lib)] private static extern void moavi_rdp_key(IntPtr h, int down, int scancode);
    [DllImport(Lib)] private static extern void moavi_rdp_clipboard(IntPtr h, IntPtr utf8);
    [DllImport(Lib)] private static extern void moavi_rdp_resize(IntPtr h, int width, int height, int scale);
    [DllImport(Lib)] private static extern void moavi_rdp_disconnect(IntPtr h);
    [DllImport(Lib)] private static extern void moavi_rdp_device(IntPtr h, int token, int printer, IntPtr name, IntPtr path, int on);
    [DllImport(Lib)] private static extern void moavi_rdp_clipboard_on(IntPtr h, int on);
    [DllImport(Lib)] private static extern void moavi_rdp_free(IntPtr h);

    [UnmanagedCallersOnly] private static void OnFrame(IntPtr user, int x, int y, int w, int h) => Of(user)?.Frame();
    [UnmanagedCallersOnly] private static void OnSize(IntPtr user, int w, int h) => Of(user)?.Frame();
    [UnmanagedCallersOnly] private static void OnClip(IntPtr user, IntPtr utf8) => Of(user)?.RemoteText(Marshal.PtrToStringUTF8(utf8) ?? "");
    [UnmanagedCallersOnly] private static void OnState(IntPtr user, int state, IntPtr message) => Of(user)?.State(state, Marshal.PtrToStringUTF8(message) ?? "");
    private static RdpSession? Of(IntPtr user) => GCHandle.FromIntPtr(user).Target as RdpSession;

    /// <summary>One desktop: its window, its FreeRDP connection on a thread of its own.</summary>
    private sealed class RdpSession : RemoteSession
    {
        private readonly Page _page;
        private readonly Image _image = new() { Stretch = Stretch.Uniform };
        private readonly TextBlock _status = new() { Foreground = B(Color.Parse("#9CA3AF")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        private readonly Dictionary<string, string> _settings;
        private readonly DevicePolicy _allowed;
        private readonly HashSet<string> _shared = [];   // "drive:<path>" / "printer:<name>" now shared
        private bool _clipOn;
        private WriteableBitmap? _bitmap;
        private IntPtr _h;
        private GCHandle _self;
        private int _paintQueued;
        private bool _ended, _closing, _connected;
        private string _lastLocalText = "";
        private DispatcherTimer? _resize;
        private int _buttons;

        public override bool HasExited => _ended;

        public RdpSession(string title, bool fullScreen, List<string> args, Dictionary<string, string> settings, DevicePolicy allowed)
        {
            _settings = settings;
            _allowed = allowed;
            _page = new Page
            {
                Title = $"{title} · MOAVI Desk",
                Width = 1280,
                Height = 800,
                MinWidth = 640,
                MinHeight = 420,
                Background = Brushes.Black,
                Icon = AppIcon(),
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            _status.Text = L("RdpConnecting");

            var full = BarButton(L("RdpFullScreen"));
            full.Click += (_, _) =>
            {
                var isFull = _page.WindowState == WindowState.FullScreen;
                _page.WindowState = isFull ? WindowState.Normal : WindowState.FullScreen;
                full.Content = L(isFull ? "RdpFullScreen" : "RdpWindowed");
            };
            var cad = BarButton(L("RdpCtrlAltDel"));
            cad.Click += (_, _) => CtrlAltDel();
            var hangul = BarButton("한/영");
            hangul.Click += (_, _) => { Key(0x72, true); Key(0x72, false); };
            var devices = BarButton(L("RdpDevices"));
            devices.Click += (_, _) => DeviceMenu().ShowAt(devices);
            var disconnect = BarButton(L("RdpDisconnect"));
            disconnect.Background = B(Accent);
            disconnect.Click += (_, _) => _page.Close();

            var bar = new Border
            {
                Background = B(Color.Parse("#111827")),
                Height = 34,
                Child = new DockPanel
                {
                    Children =
                    {
                        Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 0, 6, 0),
                                                Children = { devices, hangul, cad, full, disconnect } }, Dock.Right),
                        Docked(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 0, 0, 0),
                                               VerticalAlignment = VerticalAlignment.Center }, Dock.Left),
                        _status
                    }
                }
            };
            var surface = new Border { Background = Brushes.Black, Child = _image, Focusable = true, ClipToBounds = true };
            var root = new DockPanel();
            root.Children.Add(Docked(bar, Dock.Top));
            root.Children.Add(surface);
            _page.Content = root;

            Pointer(surface);
            surface.SizeChanged += (_, _) => QueueResize(surface);
            _page.Opened += (_, _) =>
            {
                if (fullScreen) { _page.WindowState = WindowState.FullScreen; full.Content = L("RdpWindowed"); }
                surface.Focus();
                Connect(args);
            };
            if (_page.Window is { } w)
            {
                w.AddHandler(InputElement.KeyDownEvent, (_, e) => KeyEvent(e, true), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                w.AddHandler(InputElement.KeyUpEvent, (_, e) => KeyEvent(e, false), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                // this Mac's clipboard goes to the desktop when the window comes to the front
                w.Activated += async (_, _) =>
                {
                    try
                    {
                        if (_h == IntPtr.Zero || w.Clipboard is not { } cb) { return; }
                        var text = await cb.TryGetTextAsync() ?? "";
                        if (text.Length == 0 || text == _lastLocalText) { return; }
                        _lastLocalText = text;
                        var p = Marshal.StringToCoTaskMemUTF8(text);
                        try { moavi_rdp_clipboard(_h, p); } finally { Marshal.FreeCoTaskMem(p); }
                    }
                    catch { }
                };
            }
            _page.Closing += (_, _) =>
            {
                _closing = true;
                if (_h != IntPtr.Zero && !_ended) { moavi_rdp_disconnect(_h); }
            };
            _page.Closed += (_, _) => Ended();
            _page.Show();
        }

        private unsafe void Connect(List<string> args)
        {
            _self = GCHandle.Alloc(this);
            var all = new[] { "moavi-desk" }.Concat(args).ToArray();
            var strings = all.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var argv = Marshal.AllocCoTaskMem(IntPtr.Size * strings.Length);
            for (var i = 0; i < strings.Length; i++) { Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]); }
            _h = moavi_rdp_new(strings.Length, argv, &OnFrame, &OnSize, &OnClip, &OnState, GCHandle.ToIntPtr(_self));
            foreach (var s in strings) { Marshal.FreeCoTaskMem(s); }
            Marshal.FreeCoTaskMem(argv);
            if (_h == IntPtr.Zero) { State(2, L("RdpFailed")); return; }
            InitialDevices();
            var h = _h;
            new Thread(() =>
            {
                moavi_rdp_run(h);
                Dispatcher.UIThread.Post(() =>
                {
                    moavi_rdp_free(h);
                    _h = IntPtr.Zero;
                    if (_self.IsAllocated) { _self.Free(); }
                    Ended();
                });
            }) { IsBackground = true, Name = "rdp" }.Start();
        }

        // --- what FreeRDP reports (its thread) ---------------------------------------------------------
        public void Frame()
        {
            if (Interlocked.Exchange(ref _paintQueued, 1) == 0) { Dispatcher.UIThread.Post(Paint, DispatcherPriority.Render); }
        }

        private unsafe void Paint()
        {
            _paintQueued = 0;
            if (_h == IntPtr.Zero) { return; }
            var buf = moavi_rdp_buffer(_h, out var w, out var h, out var stride);
            if (buf == IntPtr.Zero || w <= 0 || h <= 0) { return; }
            if (_bitmap is null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
            {
                _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                _image.Source = _bitmap;
            }
            using (var fb = _bitmap.Lock())
            {
                if (fb.RowBytes == stride) { Buffer.MemoryCopy((void*)buf, (void*)fb.Address, (long)fb.RowBytes * h, (long)stride * h); }
                else
                {
                    var n = Math.Min(stride, fb.RowBytes);
                    for (var y = 0; y < h; y++) { Buffer.MemoryCopy((byte*)buf + y * stride, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, n); }
                }
            }
            _image.InvalidateVisual();
        }

        public void RemoteText(string text) => Dispatcher.UIThread.Post(async () =>
        {
            _lastLocalText = text;
            try { if (_page.Clipboard is { } cb) { await cb.SetTextAsync(text); } } catch { }
        });

        public void State(int state, string message) => Dispatcher.UIThread.Post(() =>
        {
            if (state == 1)
            {
                _connected = true;
                _status.Text = L("RdpConnected");
                return;
            }
            // ended: the user's own disconnect or the desktop's sign-out closes the window; a failure stays on screen
            if (_closing || (_connected && string.IsNullOrEmpty(message))) { if (!_closing) { _page.Close(); } return; }
            _status.Text = string.IsNullOrEmpty(message) ? L("RdpFailed") : $"{L("RdpFailed")} - {message}";
            _status.Foreground = B(Color.Parse("#F87171"));
        });

        private void Ended()
        {
            if (_ended) { return; }
            _ended = true;
            if (_h != IntPtr.Zero) { moavi_rdp_disconnect(_h); }
            RaiseExited();
        }

        public override void End() => _page.Close();

        // --- input ----------------------------------------------------------------------------------------
        private (int X, int Y) At(Point p)
        {
            var b = _image.Bounds;
            if (_bitmap is null || b.Width <= 0 || b.Height <= 0) { return (0, 0); }
            int w = _bitmap.PixelSize.Width, h = _bitmap.PixelSize.Height;
            var s = Math.Min(b.Width / w, b.Height / h);
            var ox = (b.Width - w * s) / 2;
            var oy = (b.Height - h * s) / 2;
            return (Math.Clamp((int)((p.X - ox) / s), 0, w - 1), Math.Clamp((int)((p.Y - oy) / s), 0, h - 1));
        }

        private void Pointer(Control surface)
        {
            const int Move = 0x0800, Down = 0x8000, Left = 0x1000, Right = 0x2000, Middle = 0x4000, Wheel = 0x0200, Negative = 0x0100;
            surface.PointerMoved += (_, e) =>
            {
                if (_h == IntPtr.Zero) { return; }
                var (x, y) = At(e.GetPosition(_image));
                moavi_rdp_mouse(_h, Move, x, y);
            };
            surface.PointerPressed += (_, e) =>
            {
                if (_h == IntPtr.Zero) { return; }
                surface.Focus();
                var pt = e.GetCurrentPoint(_image);
                var (x, y) = At(pt.Position);
                // a Control-click is a right click, as elsewhere on a Mac
                _buttons = pt.Properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control) ? Right
                         : pt.Properties.IsMiddleButtonPressed ? Middle : Left;
                moavi_rdp_mouse(_h, _buttons | Down, x, y);
            };
            surface.PointerReleased += (_, e) =>
            {
                if (_h == IntPtr.Zero || _buttons == 0) { return; }
                var (x, y) = At(e.GetPosition(_image));
                moavi_rdp_mouse(_h, _buttons, x, y);
                _buttons = 0;
            };
            surface.PointerWheelChanged += (_, e) =>
            {
                if (_h == IntPtr.Zero) { return; }
                var (x, y) = At(e.GetPosition(_image));
                var steps = (int)Math.Clamp(Math.Round(e.Delta.Y * 120), -255, 255);
                if (steps == 0) { return; }
                moavi_rdp_mouse(_h, Wheel | (steps < 0 ? Negative | (steps & 0xFF) : steps), x, y);
            };
        }

        private void KeyEvent(KeyEventArgs e, bool down)
        {
            if (_h == IntPtr.Zero) { return; }
            if (Keys.TryGetValue(e.PhysicalKey, out var sc))
            {
                e.Handled = true;
                Key(sc, down);
            }
        }

        private void Key(int scancode, bool down) { if (_h != IntPtr.Zero) { moavi_rdp_key(_h, down ? 1 : 0, scancode); } }

        private void CtrlAltDel()
        {
            Key(0x1D, true); Key(0x38, true); Key(0x153, true);
            Key(0x153, false); Key(0x38, false); Key(0x1D, false);
        }

        private void QueueResize(Control surface)
        {
            _resize ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
            {
                _resize!.Stop();
                if (_h == IntPtr.Zero) { return; }
                var scale = _page.Window?.RenderScaling ?? 1;
                moavi_rdp_resize(_h, (int)(surface.Bounds.Width * scale), (int)(surface.Bounds.Height * scale), (int)Math.Round(scale * 100));
            });
            _resize.Stop();
            _resize.Start();
        }

        // --- 장치: drives (the home folder, USB storage under /Volumes), printers, the clipboard -------------
        private void InitialDevices()
        {
            bool On(string key, bool fallback = false) => _settings.TryGetValue(key, out var v) ? v.Trim() == "1" : fallback;
            // what the panel's choice left in the .rdp: all drives as on Windows (drivestoredirect:s:*), all printers
            if (_allowed.Drives && ((_settings.TryGetValue("drivestoredirect", out var d) && d.Trim().Length > 0) || On("redirectdrives")))
            {
                foreach (var (name, path) in Drives()) { Share(true, false, name, path); }
            }
            if (_allowed.Printers && On("redirectprinters"))
            {
                foreach (var name in Printers()) { Share(true, true, name, null); }
            }
            _clipOn = _allowed.Clipboard && On("redirectclipboard", true);
            moavi_rdp_clipboard_on(_h, _clipOn ? 1 : 0);
        }

        private void Share(bool on, bool printer, string name, string? path)
        {
            if (_h == IntPtr.Zero) { return; }
            var key = printer ? "printer:" + name : "drive:" + path;
            var n = Marshal.StringToCoTaskMemUTF8(name);
            var p = path is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(path);
            try { moavi_rdp_device(_h, Token(key), printer ? 1 : 0, n, p, on ? 1 : 0); }
            finally
            {
                Marshal.FreeCoTaskMem(n);
                if (p != IntPtr.Zero) { Marshal.FreeCoTaskMem(p); }
            }
            if (on) { _shared.Add(key); } else { _shared.Remove(key); }
        }

        private static int Token(string key)
        {
            var h = 17;
            foreach (var ch in key) { h = unchecked((h * 31) + ch); }
            return h & 0x7FFFFFFF;
        }

        /// <summary>The home folder and the volumes plugged in (USB sticks, disks), as the desktop names them.</summary>
        private static List<(string Name, string Path)> Drives()
        {
            var list = new List<(string, string)> { ("Mac", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) };
            try
            {
                foreach (var dir in Directory.GetDirectories("/Volumes"))
                {
                    var info = new DirectoryInfo(dir);
                    if (info.LinkTarget is not null) { continue; }   // the start-up disk is a link to /
                    var name = new string(info.Name.Where(char.IsLetterOrDigit).Take(8).ToArray());
                    list.Add((name.Length > 0 ? name : "USB", dir));
                }
            }
            catch { }
            return list;
        }

        /// <summary>This Mac's printers (CUPS: lpstat -e).</summary>
        private static List<string> Printers()
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/lpstat", "-e")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                })!;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return output.Split((char)10, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
            catch { return []; }
        }

        /// <summary>장치: switch what this connection shares, as far as the pool allows it - at once, no reconnect.</summary>
        private MenuFlyout DeviceMenu()
        {
            var menu = new MenuFlyout();
            MenuItem Title(string text) => new() { Header = text, IsEnabled = false, FontWeight = FontWeight.SemiBold };
            MenuItem Toggle(string text, bool on, bool allowed, Action<bool> act)
            {
                var item = new MenuItem { Header = text, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on, IsEnabled = allowed && _h != IntPtr.Zero };
                item.Click += (_, _) => act(!on);
                return item;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            menu.Items.Add(Title(L("RdpDevDrives")));
            foreach (var (name, path) in Drives())
            {
                var label = path == home ? L("RdpDevHome") : $"{System.IO.Path.GetFileName(path)} ({name})";
                menu.Items.Add(Toggle(label, _shared.Contains("drive:" + path), _allowed.Drives, on => Share(on, false, name, path)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Title(L("RdpDevPrinterList")));
            var printers = Printers();
            foreach (var name in printers)
            {
                menu.Items.Add(Toggle(name, _shared.Contains("printer:" + name), _allowed.Printers, on => Share(on, true, name, null)));
            }
            if (printers.Count == 0) { menu.Items.Add(new MenuItem { Header = L("RdpDevNone"), IsEnabled = false }); }
            menu.Items.Add(new Separator());
            menu.Items.Add(Toggle(L("RdpDevClipboardItem"), _clipOn, _allowed.Clipboard, on =>
            {
                _clipOn = on;
                if (_h != IntPtr.Zero) { moavi_rdp_clipboard_on(_h, on ? 1 : 0); }
            }));
            if (!_allowed.Drives || !_allowed.Printers || !_allowed.Clipboard)
            {
                menu.Items.Add(new MenuItem { Header = L("RdpDevPolicy"), IsEnabled = false });
            }
            return menu;
        }

        private static Button BarButton(string text) => new()
        {
            Content = text,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 4),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static Control Docked(Control c, Dock dock) { DockPanel.SetDock(c, dock); return c; }
    }

    /// <summary>The keyboard's physical keys as RDP scancodes (set 1; 0x100 = extended). ⌘ is Ctrl, right ⌘ is 한/영.</summary>
    private static readonly Dictionary<PhysicalKey, int> Keys = BuildKeys();

    private static Dictionary<PhysicalKey, int> BuildKeys()
    {
        var names = new Dictionary<string, int>
        {
            ["Escape"] = 0x01, ["Digit1"] = 0x02, ["Digit2"] = 0x03, ["Digit3"] = 0x04, ["Digit4"] = 0x05, ["Digit5"] = 0x06,
            ["Digit6"] = 0x07, ["Digit7"] = 0x08, ["Digit8"] = 0x09, ["Digit9"] = 0x0A, ["Digit0"] = 0x0B, ["Minus"] = 0x0C,
            ["Equal"] = 0x0D, ["Backspace"] = 0x0E, ["Tab"] = 0x0F,
            ["Q"] = 0x10, ["W"] = 0x11, ["E"] = 0x12, ["R"] = 0x13, ["T"] = 0x14, ["Y"] = 0x15, ["U"] = 0x16, ["I"] = 0x17,
            ["O"] = 0x18, ["P"] = 0x19, ["BracketLeft"] = 0x1A, ["BracketRight"] = 0x1B, ["Enter"] = 0x1C, ["ControlLeft"] = 0x1D,
            ["A"] = 0x1E, ["S"] = 0x1F, ["D"] = 0x20, ["F"] = 0x21, ["G"] = 0x22, ["H"] = 0x23, ["J"] = 0x24, ["K"] = 0x25,
            ["L"] = 0x26, ["Semicolon"] = 0x27, ["Quote"] = 0x28, ["Backquote"] = 0x29, ["ShiftLeft"] = 0x2A, ["Backslash"] = 0x2B,
            ["Z"] = 0x2C, ["X"] = 0x2D, ["C"] = 0x2E, ["V"] = 0x2F, ["B"] = 0x30, ["N"] = 0x31, ["M"] = 0x32, ["Comma"] = 0x33,
            ["Period"] = 0x34, ["Slash"] = 0x35, ["ShiftRight"] = 0x36, ["NumPadMultiply"] = 0x37, ["AltLeft"] = 0x38,
            ["Space"] = 0x39, ["CapsLock"] = 0x3A,
            ["F1"] = 0x3B, ["F2"] = 0x3C, ["F3"] = 0x3D, ["F4"] = 0x3E, ["F5"] = 0x3F, ["F6"] = 0x40, ["F7"] = 0x41,
            ["F8"] = 0x42, ["F9"] = 0x43, ["F10"] = 0x44, ["NumLock"] = 0x45, ["ScrollLock"] = 0x46,
            ["NumPad7"] = 0x47, ["NumPad8"] = 0x48, ["NumPad9"] = 0x49, ["NumPadSubtract"] = 0x4A, ["NumPad4"] = 0x4B,
            ["NumPad5"] = 0x4C, ["NumPad6"] = 0x4D, ["NumPadAdd"] = 0x4E, ["NumPad1"] = 0x4F, ["NumPad2"] = 0x50,
            ["NumPad3"] = 0x51, ["NumPad0"] = 0x52, ["NumPadDecimal"] = 0x53, ["IntlBackslash"] = 0x56, ["F11"] = 0x57,
            ["F12"] = 0x58, ["IntlRo"] = 0x73, ["IntlYen"] = 0x7D, ["Lang1"] = 0x72, ["Lang2"] = 0x71,
            ["NumPadEnter"] = 0x11C, ["ControlRight"] = 0x11D, ["NumPadDivide"] = 0x135, ["PrintScreen"] = 0x137,
            ["AltRight"] = 0x138, ["Home"] = 0x147, ["ArrowUp"] = 0x148, ["PageUp"] = 0x149, ["ArrowLeft"] = 0x14B,
            ["ArrowRight"] = 0x14D, ["End"] = 0x14F, ["ArrowDown"] = 0x150, ["PageDown"] = 0x151, ["Insert"] = 0x152,
            ["Delete"] = 0x153, ["ContextMenu"] = 0x15D,
            // ⌘ as Ctrl (copy, paste, save as a Mac user types them), right ⌘ as 한/영
            ["MetaLeft"] = 0x1D, ["MetaRight"] = 0x72,
        };
        var keys = new Dictionary<PhysicalKey, int>();
        foreach (var (name, code) in names)
        {
            if (Enum.TryParse<PhysicalKey>(name, out var k)) { keys[k] = code; }
        }
        return keys;
    }
}
