/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

#if DEBUG
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using MoaviDesk.Config;

namespace MoaviDesk.UI;

/// <summary>
/// Debug builds only: MOAVI_SNAPSHOT=&lt;folder&gt; renders the sign-in and settings windows to PNGs
/// (settings after pressing 접속 테스트) and exits. For docs and for checking screens without a desktop.
/// </summary>
internal static class Snapshot
{
    public static bool TryStart(AppConfig config, Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime lifetime)
    {
        var dir = Environment.GetEnvironmentVariable("MOAVI_SNAPSHOT");
        if (string.IsNullOrEmpty(dir)) { return false; }
        Directory.CreateDirectory(dir);

        var login = LoginWindow.Create(config);
        login.Opened += async (_, _) =>
        {
            await Task.Delay(5000);                       // the server check
            Save(login, System.IO.Path.Combine(dir, "login.png"));

            var settings = SettingsWindow.Create(config);
            settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            settings.Show();
            await Task.Delay(500);
            settings.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "TestConnection")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(7000);
            Save(settings, System.IO.Path.Combine(dir, "settings.png"));
            lifetime.Shutdown();
        };
        lifetime.MainWindow = login;
        return true;
    }

    private static void Save(Window w, string path)
    {
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
        bmp.Render(w);
        bmp.Save(path);
    }
}
#endif
