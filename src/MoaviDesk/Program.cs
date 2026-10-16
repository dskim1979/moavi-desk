/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using MoaviDesk.Config;
using MoaviDesk.UI;

namespace MoaviDesk;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
        lifetime.Start(args);
    }
}
