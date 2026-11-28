/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Android.Content;

namespace MoaviDesk;

/// <summary>Which activities are up: MOAVI Desk's own (to start others from, to end the app) and FreeRDP's
/// session screens (AndroidRdp: a session ends when its screen is gone).</summary>
internal sealed class AndroidHost : Java.Lang.Object, Android.App.Application.IActivityLifecycleCallbacks
{
    public static Activity? Main { get; private set; }

    /// <summary>A session screen being opened, waiting for its activity.</summary>
    public static readonly Queue<Services.AndroidRdp.Session> Waiting = new();
    private static readonly Dictionary<Activity, Services.AndroidRdp.Session> Open = [];

    /// <summary>Sessions in another app (SPICE): they end when MOAVI Desk is back in front.</summary>
    public static readonly List<ElsewhereSession> Elsewhere = [];

    internal sealed class ElsewhereSession : Services.RemoteSession
    {
        private bool _ended;
        public bool Left { get; set; }
        public override bool HasExited => _ended;
        public override void End() => Ended();
        public void Ended()
        {
            if (_ended) { return; }
            _ended = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(RaiseExited);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            if (Main is { } a) { a.StartActivity(intent); }
            else { intent.AddFlags(ActivityFlags.NewTask); Android.App.Application.Context.StartActivity(intent); }
        }
        catch { /* no browser */ }
    }

    public void OnActivityCreated(Activity activity, Bundle? savedInstanceState)
    {
        if (activity is MainActivity) { Main = activity; return; }
        if (activity.Class.Name == Services.AndroidRdp.SessionActivity && Waiting.TryDequeue(out var s))
        {
            Open[activity] = s;
            s.Attach(activity);
        }
    }

    public void OnActivityDestroyed(Activity activity)
    {
        if (ReferenceEquals(activity, Main)) { Main = null; return; }
        if (Open.Remove(activity, out var s) && activity.IsFinishing) { s.Ended(); }
    }

    public void OnActivityPaused(Activity activity)
    {
        if (activity is MainActivity) { foreach (var s in Elsewhere) { s.Left = true; } }
    }

    public void OnActivityResumed(Activity activity)
    {
        if (activity is not MainActivity) { return; }
        Main = activity;
        foreach (var s in Elsewhere.Where(s => s.Left).ToList()) { Elsewhere.Remove(s); s.Ended(); }
    }
    public void OnActivitySaveInstanceState(Activity activity, Bundle outState) { }
    public void OnActivityStarted(Activity activity) { }
    public void OnActivityStopped(Activity activity) { }
}
