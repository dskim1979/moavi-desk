/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using YamlDotNet.Serialization;

namespace MoaviDesk.Config;

/// <summary>
/// The client's settings, kept in %USERPROFILE%\.moavi-desk\config, with the PC's own settings
/// (%ProgramData%\MOAVI Desk, <see cref="MachineConfig"/>) laid over them.
/// </summary>
internal class AppConfig
{
    /// <summary>
    /// Settings the PC's config (or the terminal's enrolment) decides: shown locked in Settings and
    /// never written to the user's file.
    /// </summary>
    [YamlIgnore]
    public HashSet<string> Managed { get; } = [];

    public bool IsManaged(string name) => Managed.Contains(name);

    public const string ThemeSystem = "System";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";

    [YamlIgnore]
    public ThemeVariant ThemeVariant
        => Theme switch
        {
            ThemeLight => ThemeVariant.Light,
            ThemeDark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

    /// <summary>UI theme: System, Light, Dark.</summary>
    public string Theme { get; set; } = ThemeDark;

    /// <summary>UI language: <c>auto</c> (the OS's), <c>ko</c> or <c>en</c>.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>The last username signed in with (filled in on the sign-in window).</summary>
    public string LastUser { get; set; } = string.Empty;

    // Server

    /// <summary>The MOAVI Desk server, e.g. <c>https://desk.example.com</c> (set in Settings).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Skip TLS certificate checks against <see cref="ServerUrl"/> (test servers only).</summary>
    public bool IgnoreCertificateErrors { get; set; } = false;

    /// <summary>
    /// SHA-256 of the server's certificate (hex), set by the PC's config: a private certificate with
    /// this fingerprint is trusted, any other is not.
    /// </summary>
    public string ServerCertSha256 { get; set; } = string.Empty;

    // Viewer

    /// <summary>Path to remote-viewer (virt-viewer) for SPICE and VNC; empty = look for it.</summary>
    public string ViewerPath { get; set; } = string.Empty;

    /// <summary>Preferred console protocol: <c>auto</c> (SPICE when offered), <c>spice</c> or <c>vnc</c>.</summary>
    public string Protocol { get; set; } = "auto";

    /// <summary>Open desktops full screen.</summary>
    public bool ViewerFullScreen { get; set; } = true;

    // LAB and kiosk

    /// <summary>
    /// LAB PC: the terminal key an admin made in 단말 관리. When set, signing in opens a desktop of the
    /// room's current class straight away, and closing it signs the user out for the next one.
    /// </summary>
    public string TerminalKey { get; set; } = string.Empty;

    /// <summary>Kiosk mode: full screen; Settings and closing the window need the admin password.</summary>
    public bool Kiosk { get; set; } = false;

    /// <summary>PBKDF2 hash of the kiosk admin password (<c>iterations.salt.hash</c>); empty = none.</summary>
    public string KioskAdminPasswordHash { get; set; } = string.Empty;

    /// <summary>Optional image behind the sign-in form in kiosk mode.</summary>
    public string KioskLoginBackground { get; set; } = string.Empty;

    /// <summary>Open the windows full screen in kiosk mode (off when a shell replacement sizes them).</summary>
    public bool KioskForceFullScreen { get; set; } = true;
}
