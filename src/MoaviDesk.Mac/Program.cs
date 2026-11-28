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
        return MoaviApp.RunDesktop(AppBuilder.Configure<Application>()
                                             .UsePlatformDetect()
                                             .With(new MacOSPlatformOptions { ShowInDock = true }),
                                   args, config);
    }
}
