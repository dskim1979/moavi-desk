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
            foreach (var (n, file) in new[] { (1, "settings-general.png"), (2, "settings-rdp.png") })
            {
                settings.GetLogicalDescendants().OfType<Button>().First(b => b.Name == $"Section{n}")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(600);
                Save(settings, System.IO.Path.Combine(dir, file));
            }
            settings.Close();

            // the launcher with sample desktops (no server needed)
            var api = new Services.DeskApi("https://127.0.0.1:1", true);
            api.SnapshotUser("ohjihoon");
            api.SnapshotDesktops =
            [
                new() { Id = "a", Name = "업무용PC-34", Pool = "일반 업무", State = "running", Os = "Windows 11", Protocols = ["rdp", "spice"], Spec = new() { Cpu = 4, MemoryGb = 8 } },
                new() { Id = "b", Name = "연구원-GPU-02", Pool = "연구원 GPU", State = "stopped", Os = "Windows 11", Protocols = ["rdp"], Spec = new() { Cpu = 8, MemoryGb = 32, Gpu = "NVIDIA L4" } },
                new() { Id = "c", Name = "ubuntu-build-01", Pool = "빌드 팜", State = "ready", Os = "Ubuntu 24.04", Protocols = ["spice", "vnc"], Spec = new() { Cpu = 16, MemoryGb = 64 } }
            ];
            var main = MainWindow.Create(config, api, null, null);
            main.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            main.Show();
            await Task.Delay(1500);
            Save(main, System.IO.Path.Combine(dir, "main.png"));
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
