/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using MoaviDesk.Config;

namespace MoaviDesk.UI;

/// <summary>The start every MOAVI Desk app shares: the typeface, the light look, the sign-in page.</summary>
internal static class MoaviApp
{
    /// <summary>The console's typeface everywhere (Paperlogy, bundled under Fonts).</summary>
    public static AppBuilder WithMoaviLook(this AppBuilder builder)
        => builder.ConfigureFonts(fonts => fonts.AddFontCollection(
                      new Avalonia.Media.Fonts.EmbeddedFontCollection(new Uri("fonts:Moavi", UriKind.Absolute),
                                                                      new Uri("avares://MoaviDesk.Core/Fonts", UriKind.Absolute))))
                  .With(new FontManagerOptions { DefaultFamilyName = "fonts:Moavi#Paperlogy" });

    public static void ApplyTheme(Application app)
    {
        app.Styles.Add(new FluentTheme());
        app.RequestedThemeVariant = ThemeVariant.Light;   // the light MOAVI Desk look everywhere (Brand)
    }

    /// <summary>Windows and macOS: one window per page, the app ends with the last one.
    /// The builder comes from the app (UsePlatformDetect is Avalonia.Desktop's).</summary>
    public static int RunDesktop(AppBuilder builder, string[] args, AppConfig config)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose
        };
        builder.WithMoaviLook().SetupWithLifetime(lifetime);
        ApplyTheme(builder.Instance!);

#if DEBUG
        if (!Snapshot.TryStart(config, lifetime))
#endif
        lifetime.MainWindow = LoginWindow.Create(config).Window;
        return lifetime.Start(args);
    }
}
