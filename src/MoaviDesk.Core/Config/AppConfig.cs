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

    /// <summary>Skip TLS certificate checks against <see cref="ServerUrl"/>.</summary>
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

    // Sign-in and desktops

    /// <summary>Keep the password for the next sign-in (encrypted for this Windows user, DPAPI). Never on a
    /// LAB PC or a kiosk.</summary>
    public bool RememberPassword { get; set; } = false;

    /// <summary>The kept password, DPAPI-protected for this Windows user (base64); empty = none.</summary>
    public string SavedPassword { get; set; } = string.Empty;

    /// <summary>Sign in by itself when the client starts (needs a kept password).</summary>
    public bool AutoSignIn { get; set; } = false;

    /// <summary>The desktop marked with the star (its id); one at a time.</summary>
    public string FavoriteDesktop { get; set; } = string.Empty;

    /// <summary>Per desktop: which of the devices the pool allows this PC passes over (the details
    /// pane's 장치 boxes). A desktop not in here passes all the pool allows.</summary>
    public Dictionary<string, DeviceChoice> Devices { get; set; } = [];

    /// <summary>After signing in, open the starred desktop (or the only one) straight away.</summary>
    public bool AutoConnect { get; set; } = false;

    // RDP window (Services/RdpWindow.cs); what the pool policy decides (drives, printers, clipboard) stays the engine's

    /// <summary>Colour depth: 32, 24, 16 or 15.</summary>
    public int RdpColorDepth { get; set; } = 32;

    /// <summary>Sound: <c>local</c> (on this PC), <c>remote</c> (on the desktop) or <c>off</c>.</summary>
    public string RdpAudio { get; set; } = "local";

    /// <summary>Send this PC's microphone to the desktop.</summary>
    public bool RdpMicrophone { get; set; } = false;

    /// <summary>Desktop scale in percent: 0 = follow this PC's display, else 100 / 125 / 150 / 175 / 200.</summary>
    public int RdpScale { get; set; } = 0;

    /// <summary>Network: <c>auto</c>, <c>lan</c>, <c>broadband</c> or <c>wan</c> (auto measures the link).</summary>
    public string RdpNetwork { get; set; } = "auto";

    /// <summary>Picture behind the sign-in and desktops windows: <c>builtin:&lt;key&gt;</c> (Brand.Backgrounds),
    /// a file path of the user's own, or empty = the default built-in one.</summary>
    public string WorkspaceBackground { get; set; } = string.Empty;

    /// <summary>A LAB PC or a kiosk: shared, so no kept password and no auto sign-in.</summary>
    [YamlIgnore]
    public bool IsSharedPc => Kiosk || TerminalKey.Length > 0;
}

/// <summary>The devices a user passes to one desktop (within what its pool allows).</summary>
public sealed class DeviceChoice
{
    public bool Drives { get; set; } = true;
    public bool Printers { get; set; } = true;
    public bool Usb { get; set; } = true;
}
