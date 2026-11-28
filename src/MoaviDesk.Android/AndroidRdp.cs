/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Android.Content;
using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// Android: a desktop in FreeRDP's session screen (freeRDPCore, inside this APK), opened with a
/// freerdp://host:port/connect?… address - each query item is one FreeRDP option (FreeRdpArgs), so the
/// bundled RD Gateway's token, a Warpgate ticket and the device choice come as on macOS. The session
/// ends when the user leaves that screen.
/// </summary>
internal sealed class AndroidRdp(AppConfig config) : IRdpClient
{
    public const string SessionActivity = "com.freerdp.freerdpcore.presentation.SessionActivity";
    private static bool _ready;

    /// <summary>Whether this APK has FreeRDP (it is built without when the .aar was not there).</summary>
    public static bool Available()
    {
        try { Java.Lang.Class.ForName(SessionActivity); return true; }
        catch { return false; }
    }

    public IEarlyRdp? Start(string title, bool fullScreen) => null;

    public Task<(string Error, RemoteSession? Session)> OpenAsync(string rdp, string title, bool fullScreen, string? password, DevicePolicy allowed)
    {
        try
        {
            if (AndroidHost.Main is not { } activity) { return Task.FromResult<(string, RemoteSession?)>((L("ViewerLaunchFailed"), null)); }
            Prepare(activity);
            // the phone's storage and the clipboard go through the bar's 장치 menu (MoaviBar.java); the clipboard
            // channel is there whenever the pool allows it
            var options = FreeRdpArgs.Build(rdp, title, fullScreen, password, config, phone: true)
                                     .Where(o => o.Name is not ("drive" or "clipboard")).ToList();
            options.Add(("clipboard", allowed.Clipboard ? "+" : "-"));
            // the phone's or tablet's whole screen (not FreeRDP's 1024×768), with Windows' scale for its density so
            // text is not tiny on a high-resolution tablet
            var m = activity.Resources!.DisplayMetrics!;
            var scale = Math.Clamp((int)Math.Round(m.Density * 100 / 25.0) * 25, 100, 300);
            options.Add(("size", $"{m.WidthPixels & ~1}x{m.HeightPixels - (int)(56 * m.Density)}"));   // less the bar
            options.Add(("scale-desktop", scale.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var address = options.First(o => o.Name == "v").Value;
            var query = string.Join('&', options.Where(o => o.Name != "v")
                                                .Select(o => $"{Uri.EscapeDataString(o.Name)}={Uri.EscapeDataString(o.Value)}"));
            var intent = new Intent(activity, Java.Lang.Class.ForName(SessionActivity));
            intent.SetData(Android.Net.Uri.Parse($"freerdp://{address}/connect?{query}"));
            // MOAVI Desk's bar on the session screen (installer/android/moavi/MoaviBar.java)
            var s = Viewer.ParseRdp(rdp);
            bool On(string key, bool fallback = false) => s.TryGetValue(key, out var v) ? v.Trim() == "1" : fallback;
            var drives = (s.TryGetValue("drivestoredirect", out var d) && d.Trim().Length > 0) || On("redirectdrives");
            intent.PutExtra("moavi.title", title);
            intent.PutExtra("moavi.labels", new[] { L("RdpDevices"), L("VncKeyboard"), "한/영", L("RdpCtrlAltDel"), L("RdpDisconnect") });
            intent.PutExtra("moavi.devlabels", new[] { L("RdpDevPhone"), L("RdpDevClipboardItem"), L("RdpDevPolicy") });
            intent.PutExtra("moavi.allow", new[] { allowed.Drives, allowed.Clipboard });
            intent.PutExtra("moavi.on", new[] { drives, On("redirectclipboard", true) });
            var session = new Session();
            AndroidHost.Waiting.Enqueue(session);
            activity.StartActivity(intent);
            return Task.FromResult<(string, RemoteSession?)>((string.Empty, session));
        }
        catch (Exception ex) { return Task.FromResult<(string, RemoteSession?)>((ex.Message, null)); }
    }

    /// <summary>
    /// What FreeRDP's own Application (GlobalApp.onCreate) sets up before a session screen opens: MOAVI Desk is
    /// the Application here, so the session list, the settings and the connection events are set up once, by
    /// reflection (GlobalApp stays FreeRDP's code, unchanged).
    /// </summary>
    private static void Prepare(Context context)
    {
        if (_ready) { return; }
        var global = Java.Lang.Class.ForName("com.freerdp.freerdpcore.application.GlobalApp");
        var map = global.GetDeclaredField("sessionMap");
        map.Accessible = true;
        if (map.Get(null) is null) { map.Set(null, new Java.Util.Concurrent.ConcurrentHashMap()); }

        var settings = Java.Lang.Class.ForName("com.freerdp.freerdpcore.presentation.ApplicationSettingsActivity");
        settings.GetMethod("get", Java.Lang.Class.FromType(typeof(Context)))!.Invoke(null, context.ApplicationContext);

        var lib = Java.Lang.Class.ForName("com.freerdp.freerdpcore.services.LibFreeRDP");
        var listener = Java.Lang.Class.ForName("com.freerdp.freerdpcore.services.LibFreeRDP$EventListener");
        lib.GetMethod("setEventListener", listener)!.Invoke(null, global.NewInstance());
        _ready = true;
    }

    /// <summary>One session screen.</summary>
    internal sealed class Session : RemoteSession
    {
        private Activity? _activity;
        private bool _ended;
        public override bool HasExited => _ended;

        public void Attach(Activity a) { _activity = a; if (_ended) { a.Finish(); } }

        public void Ended()
        {
            if (_ended) { return; }
            _ended = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(RaiseExited);
        }

        public override void End()
        {
            try { _activity?.Finish(); } catch { }
            Ended();
        }
    }
}
