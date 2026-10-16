/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI.Helpers;
using static MoaviDesk.UI.Brand;

namespace MoaviDesk.UI;

/// <summary>
/// The signed-in user's desktops as cards: state, spec, Connect, power. Refreshes every 20s.
/// Signing out closes the open viewers and ends their sessions.
/// </summary>
internal static class MainWindow
{
    public static Window Create(AppConfig config, DeskApi api, ClientConfig? clientConfig, string? notice)
    {
        var window = new Window
        {
            Title = $"{Product} · {api.Username}",
            Width = 1100,
            Height = 720,
            MinWidth = 520,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = B(Bg),
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Dark
        };
        if (config.Kiosk && config.KioskForceFullScreen) { window.WindowState = WindowState.FullScreen; }

        var open = new List<(Process Process, string SessionId)>();

        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        var empty = new TextBlock
        {
            Text = L("MoaviNoDesktops"),
            Foreground = B(Muted),
            IsVisible = false,
            Margin = new Thickness(4, 12)
        };
        var banner = new Border
        {
            Background = B(Color.Parse("#173842")),
            BorderBrush = B(Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            IsVisible = false,
            Child = new TextBlock { Foreground = B(Text), TextWrapping = TextWrapping.Wrap }
        };
        void Notice(string? text, Color? border = null)
        {
            ((TextBlock)banner.Child!).Text = text ?? "";
            banner.BorderBrush = B(border ?? Accent);
            banner.IsVisible = !string.IsNullOrEmpty(text);
        }

        var updateText = clientConfig?.LatestVersion is { Length: > 0 } latest && LoginWindow.IsOlder(DeskApi.AppVersion, latest)
                            ? string.Format(L("MoaviUpdateAvailable"), latest)
                            : null;
        Notice(notice ?? updateText, notice is null ? Accent : Warn);

        var btnRefresh = GhostButton(L("Refresh"), AppIcons.Refresh);
        var btnSupport = GhostButton(L("MoaviRemoteSupport"), AppIcons.Info);
        var btnSignOut = GhostButton(L("MoaviSignOut"), AppIcons.Login);
        var supportUrl = clientConfig?.SupportUrl is { Length: > 0 } s ? s : DefaultSupportUrl;
        btnSupport.Click += (_, _) => OpenUrl(supportUrl);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { btnRefresh, btnSupport, btnSignOut } };
        if (updateText is not null && clientConfig?.DownloadUrl is { Length: > 0 } dl)
        {
            var btnUpdate = GhostButton(L("MoaviDownload"), AppIcons.Update);
            btnUpdate.Click += (_, _) => OpenUrl(dl);
            actions.Children.Insert(0, btnUpdate);
        }

        var top = new Border
        {
            Background = B(Surface2),
            BorderBrush = B(Edge),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 12),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    Lockup(28, 18),
                    actions
                }
            }
        };
        Grid.SetColumn(actions, 1);

        var title = new TextBlock
        {
            Text = L("MoaviMyDesktops"),
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = B(Text)
        };
        var user = new TextBlock { Text = api.Username, Foreground = B(Muted), VerticalAlignment = VerticalAlignment.Bottom };

        var body = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                banner,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { title, user } },
                empty,
                cards
            }
        };

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = body });
        window.Content = dock;

        async Task Load()
        {
            try
            {
                var list = await api.DesktopsAsync();
                cards.Children.Clear();
                foreach (var d in list) { cards.Children.Add(Card(d)); }
                empty.IsVisible = list.Count == 0;
            }
            catch (DeskApiException ex) when (ex.Status == 401)
            {
                await SignOut();
            }
            catch (DeskApiException ex)
            {
                Notice(ex.Message, Danger);
            }
        }

        Control Card(DeskInfo d)
        {
            var (stateText, stateColor) = d.State switch
            {
                "ready" => (L("MoaviStateReady"), Ok),
                "running" => (L("MoaviStateRunning"), Ok),
                "stopped" => (L("MoaviStateStopped"), Muted),
                "error" => (L("MoaviStateError"), Danger),
                _ => (L("MoaviStateBusy"), Warn)
            };

            var spec = string.Join(" · ", new[]
            {
                d.Os,
                d.Spec?.Cpu is { } cpu ? $"{cpu} vCPU" : null,
                d.Spec?.MemoryGb is { } mem ? $"{mem:0.#} GB" : null,
                d.Spec?.Gpu
            }.Where(x => !string.IsNullOrEmpty(x)));

            var btnConnect = PrimaryButton(L("Connect"));
            btnConnect.IsEnabled = d.State is not ("provisioning" or "resetting" or "error");

            var protocolBox = new ComboBox
            {
                ItemsSource = d.Protocols.Select(p => p.ToUpperInvariant()).ToList(),
                SelectedIndex = Math.Max(0, d.Protocols.IndexOf(
                    config.Protocol is "spice" or "vnc" && d.Protocols.Contains(config.Protocol)
                        ? config.Protocol
                        : d.Protocols.Contains("spice") ? "spice" : d.Protocols.FirstOrDefault() ?? "vnc")),
                IsVisible = d.Protocols.Count > 1,
                MinWidth = 90
            };

            var btnPower = UiHelper.IconButton(AppIcons.DotsVertical, "More", foregroundColor: Muted);
            var menu = new MenuFlyout();
            foreach (var (key, action, confirmKey) in new[]
                     {
                         ("Start", "start", (string?)null),
                         ("MoaviReboot", "reboot", "MoaviConfirmReboot"),
                         ("Shutdown", "shutdown", "MoaviConfirmShutdown")
                     })
            {
                var item = new MenuItem { Header = L(key) };
                item.Click += async (_, _) =>
                {
                    if (confirmKey is not null && !await DialogHelper.ConfirmAsync(window, string.Format(L(confirmKey), d.Name))) { return; }
                    try { await api.PowerAsync(d.Id, action); await Task.Delay(1500); await Load(); }
                    catch (DeskApiException ex) { Notice(ex.Message, Danger); }
                };
                menu.Items.Add(item);
            }
            btnPower.Flyout = menu;

            btnConnect.Click += async (_, _) =>
            {
                btnConnect.IsEnabled = false;
                try
                {
                    var protocol = d.Protocols.Count > 0 ? d.Protocols[Math.Max(0, protocolBox.SelectedIndex)] : "vnc";
                    var c = await api.ConnectAsync(d.Id, protocol);
                    var (error, process) = await Viewer.OpenAsync(api, c, Viewer.FindViewer(config.ViewerPath),
                                                                       d.Name, config.ViewerFullScreen);
                    if (process is null)
                    {
                        Notice(error, Danger);
                        await api.EndSessionAsync(c.SessionId);
                        return;
                    }
                    open.Add((process, c.SessionId));
                    process.EnableRaisingEvents = true;
                    process.Exited += async (_, _) =>
                    {
                        open.RemoveAll(o => o.Process == process);
                        await api.EndSessionAsync(c.SessionId);
                    };
                }
                catch (DeskApiException ex) { Notice(ex.Message, Danger); }
                finally { btnConnect.IsEnabled = true; }
            };

            var stateDot = new Ellipse { Width = 8, Height = 8, Fill = B(stateColor), VerticalAlignment = VerticalAlignment.Center };

            return new Border
            {
                Width = 300,
                Margin = new Thickness(0, 0, 14, 14),
                Background = B(Surface),
                BorderBrush = B(Edge),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                            Children =
                            {
                                UiHelper.Icon(d.Os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true ? AppIcons.Windows : AppIcons.Monitor, 22, B(Accent)),
                                WithColumn(new TextBlock
                                {
                                    Text = d.Name,
                                    FontWeight = FontWeight.Bold,
                                    FontSize = 15,
                                    Foreground = B(Text),
                                    Margin = new Thickness(10, 0, 0, 0),
                                    VerticalAlignment = VerticalAlignment.Center,
                                    TextTrimming = TextTrimming.CharacterEllipsis
                                }, 1),
                                WithColumn(btnPower, 2)
                            }
                        },
                        new TextBlock { Text = d.Pool, Foreground = B(Muted), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children = { stateDot, new TextBlock { Text = stateText, Foreground = B(Text), FontSize = 12 } }
                        },
                        new TextBlock { Text = spec, Foreground = B(Muted), FontSize = 12, TextWrapping = TextWrapping.Wrap },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Margin = new Thickness(0, 6, 0, 0),
                            Children = { btnConnect, WithColumn(protocolBox, 1, new Thickness(8, 0, 0, 0)) }
                        }
                    }
                }
            };
        }

        static Control WithColumn(Control c, int column, Thickness? margin = null)
        {
            Grid.SetColumn(c, column);
            if (margin is { } m) { c.Margin = m; }
            return c;
        }

        var signedOut = false;
        async Task SignOut()
        {
            if (signedOut) { return; }
            signedOut = true;
            foreach (var (p, sid) in open.ToList())
            {
                try { if (!p.HasExited) { p.Kill(); } } catch { }
                await api.EndSessionAsync(sid);
            }
            await api.LogoutAsync();
            api.Dispose();
            KioskGuard.ResetAdmin();
            LoginWindow.Create(config).Show();
            window.Close();
        }

        btnRefresh.Click += async (_, _) => await Load();
        btnSignOut.Click += async (_, _) =>
        {
            if (open.Count > 0 && !await DialogHelper.ConfirmAsync(window, L("ConfirmCloseSessions"))) { return; }
            await SignOut();
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += async (_, _) => await Load();
        window.Opened += async (_, _) => { await Load(); timer.Start(); };
        window.Closing += async (_, e) =>
        {
            if (signedOut) { return; }
            e.Cancel = true;
            if (config.Kiosk && !await KioskGuard.CheckAsync(window, config)) { return; }
            if (open.Count > 0 && !await DialogHelper.ConfirmAsync(window, L("ConfirmCloseSessions"))) { return; }
            timer.Stop();
            foreach (var (p, sid) in open.ToList())
            {
                try { if (!p.HasExited) { p.Kill(); } } catch { }
                await api.EndSessionAsync(sid);
            }
            await api.LogoutAsync();
            signedOut = true;
            window.Close();
        };
        window.Closed += (_, _) => timer.Stop();

        return window;
    }
}
