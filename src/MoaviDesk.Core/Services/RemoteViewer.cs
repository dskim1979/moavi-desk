/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;

namespace MoaviDesk.Services;

/// <summary>Starts remote-viewer (virt-viewer) on a connection file.</summary>
internal static class RemoteViewer
{
    /// <summary>
    /// virt-viewer reads a .vv file as a libvirt domain name, so a path to it is turned into the
    /// remote-viewer beside it. Null when virt-viewer was picked and no remote-viewer is there.
    /// </summary>
    public static string? Resolve(string viewerPath)
    {
        var virt = OperatingSystem.IsWindows() ? "virt-viewer.exe" : "virt-viewer";
        var remote = OperatingSystem.IsWindows() ? "remote-viewer.exe" : "remote-viewer";
        if (!string.Equals(System.IO.Path.GetFileName(viewerPath), virt, StringComparison.OrdinalIgnoreCase)) { return viewerPath; }

        var dir = System.IO.Path.GetDirectoryName(viewerPath);
        if (string.IsNullOrEmpty(dir)) { return remote; }   // through PATH
        var sibling = System.IO.Path.Combine(dir, remote);
        return File.Exists(sibling) ? sibling : null;
    }

    /// <summary>
    /// Starts the viewer directly (no shell) so the process handle can be watched. remote-viewer.exe
    /// is a console-subsystem app: CreateNoWindow keeps an empty console from appearing.
    /// </summary>
    public static (Process? Process, string? Error) Launch(string viewerPath, string vvFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = viewerPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(vvFile);
        try { return (Process.Start(psi), null); }
        catch (Exception ex) { return (null, ex.Message); }
    }
}
