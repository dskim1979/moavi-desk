/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI;

namespace MoaviDesk;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // the installer writing the PC's settings (msiexec … SERVER=… ENROLL_CODE=…): no window
        if (args.Contains(MachineSetup.Switch)) { return MachineSetup.Run(args); }
        // the MOAVI Desk Agent service (enrolment, heartbeat, kiosk key): no window
        if (args.Contains(Agent.Switch)) { return Agent.Run(args); }

        var config = AppConfigManager.Load();
        ApplyLanguage(config.Language);

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose
        };

        var builder = AppBuilder.Configure<Application>()
                                .UsePlatformDetect()
                                .SetupWithLifetime(lifetime);

        builder.Instance!.Styles.Add(new FluentTheme());
        builder.Instance.RequestedThemeVariant = config.ThemeVariant;

#if DEBUG
        if (!Snapshot.TryStart(config, lifetime))
#endif
        lifetime.MainWindow = LoginWindow.Create(config);
        return lifetime.Start(args);
    }
}
