/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI;

namespace MoaviDesk;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var config = AppConfigManager.Load();
        ApplyLanguage(config.Language);
        Platform.Vault = KeychainVault.Create();
        // MOAVI Desk's own RDP window (libmoavi-rdp), else FreeRDP's window
        Platform.Rdp = MacRdp.Available() ? new MacRdp(config)
                     : FreeRdpClient.Find() is { } freerdp ? new FreeRdpClient(freerdp, config) : null;
        Action? start = null;
#if MOAVI_TEST
        if (Environment.GetEnvironmentVariable("MOAVI_TEST_RDP") is { Length: > 0 } spec && Platform.Rdp is { } rdp)
        {
            var (file, password) = TestRdp.Of(spec);
            start = () => _ = rdp.OpenAsync(file, "RDP check", false, password, new DevicePolicy(true, true, true));
        }
        // a real .rdp from the engine (direct, 장치 연결 방식 / rdpgw, Warpgate): opened as MOAVI Desk opens it
        else if (Environment.GetEnvironmentVariable("MOAVI_TEST_RDP_FILE") is { Length: > 0 } path && Platform.Rdp is { } rdp2)
        {
            var file = File.ReadAllText(path);
            var password = Environment.GetEnvironmentVariable("MOAVI_TEST_PASSWORD");
            start = () => _ = rdp2.OpenAsync(file, "RDP check", false, string.IsNullOrEmpty(password) ? null : password, DevicePolicy.Of(file));
        }
#endif
        return MoaviApp.RunDesktop(AppBuilder.Configure<Application>()
                                             .UsePlatformDetect()
                                             .With(new MacOSPlatformOptions { ShowInDock = true }),
                                   args, config, start);
    }
}
