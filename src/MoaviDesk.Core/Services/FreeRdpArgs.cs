/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.Config;

namespace MoaviDesk.Services;

/// <summary>
/// The engine's .rdp as FreeRDP 3 options (macOS: the bundled sdl3-freerdp, Android: FreeRDP's session
/// screen). Each option is (name, value): "/name:value", value "" = "/name", "+" / "-" = "+name" / "-name".
/// - the bundled RD Gateway (장치 연결 방식): gatewayhostname + gatewayaccesstoken → /gateway:g:…,access-token:…
/// - Warpgate: the single-use ticket is the user name, no NLA (enablecredsspsupport:i:0 → /sec:tls)
/// - devices: what the .rdp still has after the user's choice (Viewer.WithDevices) - drives, printers, clipboard
/// </summary>
internal static class FreeRdpArgs
{
    public static List<(string Name, string Value)> Build(string rdp, string title, bool fullScreen, string? password, AppConfig config,
                                                          bool phone = false)
    {
        var s = Viewer.ParseRdp(rdp);
        string S(string key, string fallback = "") => s.TryGetValue(key, out var v) ? v.Trim() : fallback;
        bool On(string key, bool fallback = false) => s.TryGetValue(key, out var v) ? v.Trim() == "1" : fallback;
        var o = new List<(string, string)>();

        // where: "full address:s:host[:port]" (+ "server port:i:")
        var address = S("full address");
        if (!address.Contains(':') && S("server port") is { Length: > 0 } port && port != "3389") { address += ":" + port; }
        o.Add(("v", address));

        // who: "username:s:DOMAIN\user", "domain:s:" - a Warpgate ticket comes as the whole user name
        if (S("username") is { Length: > 0 } user) { o.Add(("u", user)); }
        if (S("domain") is { Length: > 0 } domain) { o.Add(("d", domain)); }
        if (!string.IsNullOrEmpty(password)) { o.Add(("p", password)); }
        // Warpgate: the single-use ticket is the sign-in, the client's password is not used - a stand-in keeps
        // FreeRDP from asking for one (it asks whenever no password is set; checked on .55, 2026-11-28)
        else if (S("username").StartsWith("ticket-", StringComparison.Ordinal)) { o.Add(("p", "warpgate")); }
        if (S("enablecredsspsupport") == "0") { o.Add(("sec", "tls")); }

        var gateway = S("gatewayhostname");
        var usage = S("gatewayusagemethod", "1");
        if (gateway.Length > 0 && usage != "0")
        {
            var g = $"g:{gateway},type:http";   // rdpgw (and RD Gateway) over HTTPS
            if (S("gatewayaccesstoken") is { Length: > 0 } token) { g += ",access-token:" + token; }
            o.Add(("gateway", g));
        }

        // the desktop's certificate is the pool's (a self-signed one on most desktops): trusted the first
        // time, a changed one is asked about
        o.Add(("cert", "tofu"));

        // devices
        o.Add(("clipboard", On("redirectclipboard", true) ? "+" : "-"));
        if (S("drivestoredirect").Length > 0 || On("redirectdrives"))
        {
            if (phone) { o.Add(("drive", "sdcard")); }   // FreeRDP's Android screen shares the phone's storage
            else { o.Add(("home-drive", "+")); }
        }
        if (On("redirectprinters") && !phone) { o.Add(("printer", "")); }

        // sound and microphone: the client's settings (as in the Windows window)
        switch (config.RdpAudio)
        {
            case "remote": o.Add(("audio-mode", "1")); break;
            case "off": o.Add(("audio-mode", "2")); break;
            default: o.Add(("sound", "")); break;
        }
        if (config.RdpMicrophone) { o.Add(("microphone", "")); }

        o.Add(("network", config.RdpNetwork switch { "lan" => "lan", "broadband" => "broadband", "wan" => "wan", _ => "auto" }));
        if (On("autoreconnection enabled", true)) { o.Add(("auto-reconnect", "")); }
        if (!phone)
        {
            o.Add(("t", title));
            o.Add(("dynamic-resolution", ""));
            if (fullScreen) { o.Add(("f", "")); }
        }
        return o;
    }

    /// <summary>One command-line argument per option (sdl3-freerdp).</summary>
    public static IEnumerable<string> ToArgs(IEnumerable<(string Name, string Value)> options)
        => options.Select(x => x.Value switch
        {
            "" => "/" + x.Name,
            "+" or "-" => x.Value + x.Name,
            _ => $"/{x.Name}:{x.Value}"
        });
}
