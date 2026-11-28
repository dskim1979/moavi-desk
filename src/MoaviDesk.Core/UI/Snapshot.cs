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
            settings.Window!.GetLogicalDescendants().OfType<Button>().First(b => b.Name == "TestConnection")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(7000);
            Save(settings, System.IO.Path.Combine(dir, "settings.png"));
            foreach (var (n, file) in new[] { (1, "settings-general.png"), (2, "settings-rdp.png") })
            {
                settings.Window!.GetLogicalDescendants().OfType<Button>().First(b => b.Name == $"Section{n}")
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
                new()
                {
                    Id = "a", Name = "업무용PC-34", Pool = "일반 업무", State = "running", Os = "Windows 11", Protocols = ["rdp", "spice"], Spec = new() { Cpu = 4, MemoryGb = 8 },
                    SignedIn = [new() { User = "ohjihoon", LoginTime = DateTimeOffset.Now.AddHours(-2).ToUnixTimeSeconds() }],
                    LastConnected = DateTimeOffset.Now.AddHours(-2).ToString("o"),
                    Policy = new() { Clipboard = "both", ScreenThumbnail = true, DesktopSso = true, UsbRedirect = true, ClientDrives = true, ClientPrinters = false },
                    Storage = new()
                    {
                        Kind = "smb",
                        Folders = new()
                        {
                            ["home"] = new() { Windows = @"\\nas01\home\ohjihoon", Linux = "/mnt/home", QuotaGb = 50 },
                            ["project"] = new() { Windows = @"\\nas01\project\design", Linux = "/mnt/project", QuotaGb = 500 }
                        }
                    }
                },
                new() { Id = "b", Name = "연구원-GPU-02", Pool = "연구원 GPU", State = "stopped", Os = "Windows 11", Protocols = ["rdp"], Spec = new() { Cpu = 8, MemoryGb = 32, Gpu = "NVIDIA L4" },
                        LastConnected = DateTimeOffset.Now.AddDays(-3).ToString("o"), Resources = [new() { Kind = "card", Name = "NVIDIA L4 #1" }] },
                new() { Id = "c", Name = "ubuntu-build-01", Pool = "빌드 팜", State = "ready", Os = "Ubuntu 24.04", Protocols = ["spice", "vnc"], Spec = new() { Cpu = 16, MemoryGb = 64 },
                        Policy = new() { AllowPower = false } }
            ];
            // a picture stands in for the Windows desktop's screen
            byte[] sample;
            using (var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://MoaviDesk.Core/Backgrounds/thumbs/2.jpg")))
            using (var ms = new MemoryStream()) { s.CopyTo(ms); sample = ms.ToArray(); }
            api.SnapshotShot = id => id == "a" ? sample : null;
            api.SnapshotLeases = [new() { Kind = "usb", Name = "Xilinx JTAG 케이블", EndsAt = DateTimeOffset.Now.AddHours(5).ToString("o"), Desktop = "업무용PC-34" }];
            api.SnapshotBanners = [new() { Id = "n1", Text = "11월 30일(토) 02:00~06:00 정기 점검으로 데스크톱 접속이 끊깁니다.", Severity = "warning" }];
            var main = MainWindow.Create(config, api, null, null);
            main.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            main.Show();
            await Task.Delay(1500);
            Save(main, System.IO.Path.Combine(dir, "main.png"));
            // a phone's width (Android)
            main.Window!.MinWidth = 0;
            main.Window.Width = 400;
            main.Window.Height = 820;
            await Task.Delay(1000);
            Save(main, System.IO.Path.Combine(dir, "main-phone.png"));
            lifetime.Shutdown();
        };
        lifetime.MainWindow = login.Window;
        return true;
    }

    private static void Save(Page p, string path)
    {
        var w = p.Window!;
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
        bmp.Render(w);
        bmp.Save(path);
    }
}
#endif
