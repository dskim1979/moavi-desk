/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Android.Content;
using AndroidX.Core.Content;

namespace MoaviDesk.Services;

/// <summary>
/// Android: SPICE in a SPICE app (aSPICE opens .vv files), as remote-viewer does it on a computer. The .vv goes
/// through this app's FileProvider (read-only, to that app only); without such an app the user is sent to it in
/// the store. The session ends when the user comes back to MOAVI Desk.
/// </summary>
internal static class SpiceHandoff
{
    private const string Store = "market://details?id=com.iiordanov.freeaSPICE";

    public static Task<(string Error, RemoteSession? Session)> OpenAsync(string vv)
    {
        try
        {
            if (AndroidHost.Main is not { } activity) { return Done(L("ViewerLaunchFailed"), null); }
            var dir = new Java.IO.File(activity.CacheDir, "vv");
            dir.Mkdirs();
            var file = new Java.IO.File(dir, "desktop.vv");
            File.WriteAllText(file.AbsolutePath, vv);
            var uri = FileProvider.GetUriForFile(activity, "com.makussystems.moavidesk.files", file);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/x-virt-viewer");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            if (intent.ResolveActivity(activity.PackageManager!) is null)
            {
                try { activity.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(Store))); } catch { }
                return Done(L("SpiceAppNeeded"), null);
            }
            var session = new AndroidHost.ElsewhereSession();
            AndroidHost.Elsewhere.Add(session);
            activity.StartActivity(intent);
            return Done(string.Empty, session);
        }
        catch (Exception ex) { return Done(ex.Message, null); }
    }

    private static Task<(string, RemoteSession?)> Done(string error, RemoteSession? s) => Task.FromResult((error, s));
}
