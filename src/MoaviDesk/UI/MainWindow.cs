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
/// The signed-in user's launcher, three panels: left the user and the shortcut tiles (refresh,
/// settings, portal, support, update, sign out); middle the desktops as rows with 실행; right the
/// selected desktop's spec and power. Refreshes every 20s. Signing out closes the open viewers and
/// ends their sessions.
/// </summary>
internal static class MainWindow
{
    public static Window Create(AppConfig config, DeskApi api, ClientConfig? clientConfig, string? notice)
    {
        var window = new Window
        {
            Title = $"{Product} · {api.Username}",
            Width = 1080,
            Height = 620,
            MinWidth = 860,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1B4650"), 0),
                    new GradientStop(Color.Parse("#132440"), 0.45),
                    new GradientStop(Color.Parse("#0C1426"), 1)
                }
            },
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Dark
        };
        if (config.Kiosk && config.KioskForceFullScreen) { window.WindowState = WindowState.FullScreen; }

        var open = new List<(Process Process, string SessionId)>();
        var glass = B(Color.Parse("#A60E1830"));      // the panels sit on the gradient

        // --- notices ---------------------------------------------------------------------------
        var banner = new Border
        {
            Background = B(Color.Parse("#173842")),
            BorderBrush = B(Accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            IsVisible = false,
            Child = new TextBlock { Foreground = B(Text), TextWrapping = TextWrapping.Wrap, FontSize = 12 }
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

        static Border Panel(IBrush bg, Control child) => new()
        {
            Background = bg,
            BorderBrush = B(Color.Parse("#402E3D5C")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Child = child
        };

        static TextBlock Heading(string text) => new()
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = B(Text),
            VerticalAlignment = VerticalAlignment.Center
        };

        // --- left: the user and the shortcut tiles ----------------------------------------------
        var initials = new string(api.Username.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        var avatar = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(38),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Accent, 0), new GradientStop(Color.Parse("#2563EB"), 1) }
            },
            BorderBrush = B(Color.Parse("#66FFFFFF")),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = initials.Length > 0 ? initials : "?",
                FontSize = 26,
                FontWeight = FontWeight.Bold,
                Foreground = B(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var userName = new TextBlock { Text = api.Username, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = B(Text), HorizontalAlignment = HorizontalAlignment.Center };
        var deskCount = new TextBlock { Foreground = B(Muted), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };

        Button Tile(string icon, string label, Func<Task> act)
        {
            var b = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new Border
                        {
                            Width = 46,
                            Height = 46,
                            CornerRadius = new CornerRadius(23),
                            Background = B(Color.Parse("#26FFFFFF")),
                            BorderBrush = B(Color.Parse("#40FFFFFF")),
                            BorderThickness = new Thickness(1),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Child = UiHelper.Icon(icon, 20, B(Colors.White))
                        },
                        new TextBlock { Text = label, FontSize = 12, Foreground = B(Text), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            };
            b.Click += async (_, _) => await act();
            return b;
        }

        var supportUrl = clientConfig?.SupportUrl is { Length: > 0 } s ? s : DefaultSupportUrl;
        var tiles = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        var left = Panel(glass, new DockPanel
        {
            Children =
            {
                Docked(new StackPanel
                {
                    Spacing = 8,
                    Margin = new Thickness(0, 6, 0, 18),
                    Children = { avatar, userName, deskCount }
                }, Dock.Top),
                Docked(new TextBlock
                {
                    Text = $"v{DeskApi.AppVersion} · (주)매커스시스템즈",
                    Foreground = B(Muted),
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                }, Dock.Bottom),
                tiles
            }
        });
        left.Width = 260;

        // --- middle: the desktops ---------------------------------------------------------------
        var rows = new StackPanel { Spacing = 8 };
        var empty = new TextBlock { Text = L("MoaviNoDesktops"), Foreground = B(Muted), IsVisible = false, Margin = new Thickness(4, 12), TextWrapping = TextWrapping.Wrap };
        var listTitle = Heading(L("MoaviMyDesktops"));
        var middle = Panel(glass, new DockPanel
        {
            Children =
            {
                Docked(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 0, 0, 12),
                    Children = { UiHelper.Icon(AppIcons.Monitor, 18, B(Accent)), listTitle }
                }, Dock.Top),
                Docked(banner, Dock.Bottom),
                new ScrollViewer { Content = new StackPanel { Children = { empty, rows } } }
            }
        });

        // --- right: the selected desktop --------------------------------------------------------
        var detail = new StackPanel { Spacing = 10 };
        var right = Panel(glass, new DockPanel
        {
            Children =
            {
                Docked(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 0, 0, 12),
                    Children = { UiHelper.Icon(AppIcons.ChartBar, 18, B(Accent)), Heading(L("DesktopInfo")) }
                }, Dock.Top),
                new ScrollViewer { Content = detail }
            }
        });
        right.Width = 290;

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(16),
            Children = { left, WithColumn(middle, 1, new Thickness(14, 0)), WithColumn(right, 2) }
        };
        window.Content = root;

        static Control Docked(Control c, Dock dock)
        {
            DockPanel.SetDock(c, dock);
            return c;
        }

        static Control WithColumn(Control c, int column, Thickness? margin = null)
        {
            Grid.SetColumn(c, column);
            if (margin is { } m) { c.Margin = m; }
            return c;
        }

        // --- behaviour --------------------------------------------------------------------------
        var connectors = new Dictionary<string, Func<Task>>();
        var autoConnected = false;
        var selectedId = config.FavoriteDesktop;
        var rowOf = new Dictionary<string, Border>();
        var last = new List<DeskInfo>();

        (string Text, Color Color) State(DeskInfo d) => d.State switch
        {
            "ready" => (L("MoaviStateReady"), Ok),
            "running" => (L("MoaviStateRunning"), Ok),
            "stopped" => (L("MoaviStateStopped"), Muted),
            "error" => (L("MoaviStateError"), Danger),
            _ => (L("MoaviStateBusy"), Warn)
        };

        async Task Power(DeskInfo d, string action, string? confirmKey)
        {
            if (confirmKey is not null && !await DialogHelper.ConfirmAsync(window, string.Format(L(confirmKey), d.Name))) { return; }
            try { await api.PowerAsync(d.Id, action); await Task.Delay(1500); await Load(); }
            catch (DeskApiException ex) { Notice(ex.Message, Danger); }
        }

        void Select(string id)
        {
            selectedId = id;
            foreach (var (key, row) in rowOf)
            {
                row.BorderBrush = B(key == id ? Accent : Color.Parse("#402E3D5C"));
                row.Background = B(Color.Parse(key == id ? "#E6F1F5F9" : "#D9DCE3EC"));
            }
            ShowDetail(last.FirstOrDefault(x => x.Id == id));
        }

        void ShowDetail(DeskInfo? d)
        {
            detail.Children.Clear();
            if (d is null)
            {
                detail.Children.Add(new TextBlock { Text = L("DesktopPick"), Foreground = B(Muted), TextWrapping = TextWrapping.Wrap });
                return;
            }
            var (stateText, stateColor) = State(d);
            detail.Children.Add(new TextBlock { Text = d.Name, FontSize = 17, FontWeight = FontWeight.Bold, Foreground = B(Text), TextWrapping = TextWrapping.Wrap });
            detail.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Ellipse { Width = 8, Height = 8, Fill = B(stateColor), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = stateText, Foreground = B(Text), FontSize = 12 }
                }
            });

            Control Line(string label, string value) => new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("90,*"),
                Children =
                {
                    new TextBlock { Text = label, Foreground = B(Muted), FontSize = 12 },
                    WithColumn(new TextBlock { Text = value, Foreground = B(Text), FontSize = 12, TextWrapping = TextWrapping.Wrap }, 1)
                }
            };
            var specBox = new StackPanel { Spacing = 7 };
            specBox.Children.Add(Line(L("DetailPool"), d.Pool));
            if (!string.IsNullOrEmpty(d.Os)) { specBox.Children.Add(Line("OS", d.Os)); }
            if (d.Spec?.Cpu is { } cpu) { specBox.Children.Add(Line("CPU", $"{cpu} vCPU")); }
            if (d.Spec?.MemoryGb is { } mem) { specBox.Children.Add(Line(L("DetailMemory"), $"{mem:0.#} GB")); }
            if (!string.IsNullOrEmpty(d.Spec?.Gpu)) { specBox.Children.Add(Line("GPU", d.Spec!.Gpu!)); }
            specBox.Children.Add(Line(L("DetailProtocols"), string.Join(" · ", Viewer.Offered(d.Protocols).Select(p => p.ToUpperInvariant()))));
            detail.Children.Add(new Border
            {
                Background = B(Color.Parse("#1AFFFFFF")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 0),
                Child = specBox
            });

            // power, as round tiles like the left panel
            var power = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (icon, key, action, confirmKey) in new[]
                     {
                         (AppIcons.Play, "Start", "start", (string?)null),
                         (AppIcons.Refresh, "MoaviReboot", "reboot", "MoaviConfirmReboot"),
                         (AppIcons.Stop, "Shutdown", "shutdown", "MoaviConfirmShutdown")
                     })
            {
                power.Children.Add(Tile(icon, L(key), () => Power(d, action, confirmKey)));
            }
            detail.Children.Add(new TextBlock { Text = L("DetailPower"), Foreground = B(Muted), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
            detail.Children.Add(power);
        }

        async Task Load()
        {
            try
            {
                var list = await api.DesktopsAsync();
                last = list.OrderByDescending(x => x.Id == config.FavoriteDesktop).ToList();
                rows.Children.Clear();
                rowOf.Clear();
                connectors.Clear();
                foreach (var d in last) { rows.Children.Add(Row(d)); }
                empty.IsVisible = list.Count == 0;
                listTitle.Text = $"{L("MoaviMyDesktops")}  {list.Count}";
                deskCount.Text = string.Format(L("DesktopCount"), list.Count);
                if (last.All(x => x.Id != selectedId)) { selectedId = last.FirstOrDefault()?.Id ?? ""; }
                Select(selectedId);
                // 자동 접속: the starred desktop, else the only one - once per sign-in
                if (!autoConnected && config.AutoConnect && !config.IsSharedPc)
                {
                    autoConnected = true;
                    var pick = list.FirstOrDefault(x => x.Id == config.FavoriteDesktop) ?? (list.Count == 1 ? list[0] : null);
                    if (pick is not null && connectors.TryGetValue(pick.Id, out var go)) { await go(); }
                }
                // Researcher native SSH: keep the managed ~/.ssh/config block in sync with the
                // Warpgate SSH-enabled desktops (no-op, and clears a stale block, when off). Best effort.
                try { await SshConfigSync.SyncAsync(api); } catch { /* own-tools SSH is optional */ }
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

        Control Row(DeskInfo d)
        {
            var (stateText, stateColor) = State(d);
            var ink = Color.Parse("#0F182C");
            var inkMuted = Color.Parse("#4B5B73");

            var btnConnect = PrimaryButton(L("Launch"));
            btnConnect.MinWidth = 84;
            btnConnect.IsEnabled = d.State is not ("provisioning" or "resetting" or "error");

            // RDP first, SPICE / VNC as the choice (browser RDP is the portal's)
            var offered = Viewer.Offered(d.Protocols);
            if (offered.Count == 0) { offered.Add("vnc"); }
            var protocolBox = new ComboBox
            {
                ItemsSource = offered.Select(p => p.ToUpperInvariant()).ToList(),
                SelectedIndex = Math.Max(0, offered.IndexOf(config.Protocol)),
                IsVisible = offered.Count > 1,
                MinWidth = 84,
                VerticalAlignment = VerticalAlignment.Center
            };

            async Task Connect()
            {
                if (!btnConnect.IsEnabled) { return; }
                btnConnect.IsEnabled = false;
                try
                {
                    var protocol = offered[Math.Max(0, protocolBox.SelectedIndex)];
                    ConnectInfo c;
                    try { c = await api.ConnectAsync(d.Id, protocol); }
                    catch (DeskApiException ex) when (protocol == "rdp" && ex.Code is "NO_RDP" or "NO_IP"
                                                      && offered.Count > 1 && config.Protocol != "rdp")
                    {
                        // a Linux desktop without xrdp, or no address yet: the next one the pool offers
                        var next = offered[1];
                        Notice(string.Format(L("RdpFallback"), next.ToUpperInvariant()), Warn);
                        c = await api.ConnectAsync(d.Id, next);
                    }
                    var (error, process) = await Viewer.OpenAsync(api, c, Viewer.FindViewer(config.ViewerPath),
                                                                       d.Name, config.ViewerFullScreen);
                    if (process is null)
                    {
                        Notice(error, Danger);
                        await api.EndSessionAsync(c.SessionId);
                        return;
                    }
                    open.Add((process, c.SessionId));
                    // through the Warpgate bastion: say so - the desktop's own logon screen asks for
                    // the user's account, nothing is typed for Warpgate and nothing is stored
                    if (c.Via == "warpgate") { Notice(L("RdpViaWarpgate"), Accent); }
                    process.EnableRaisingEvents = true;
                    process.Exited += async (_, _) =>
                    {
                        open.RemoveAll(o => o.Process == process);
                        await api.EndSessionAsync(c.SessionId);
                    };
                }
                catch (DeskApiException ex) { Notice(ex.Message, Danger); }
                finally { btnConnect.IsEnabled = true; }
            }
            btnConnect.Click += async (_, _) => await Connect();
            if (btnConnect.IsEnabled) { connectors[d.Id] = Connect; }

            // the star: one desktop at a time, shown first and opened by 자동 접속
            var starred = d.Id == config.FavoriteDesktop;
            var btnStar = UiHelper.IconButton(AppIcons.Star, "Favorite", foregroundColor: starred ? Color.Parse("#E08A1E") : inkMuted);
            ToolTip.SetTip(btnStar, L(starred ? "FavoriteRemove" : "FavoriteAdd"));
            btnStar.Click += async (_, _) =>
            {
                config.FavoriteDesktop = starred ? string.Empty : d.Id;
                AppConfigManager.Save(config);
                await Load();
            };

            var windows = d.Os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
            var row = new Border
            {
                Background = B(Color.Parse("#D9DCE3EC")),
                BorderBrush = B(Color.Parse("#402E3D5C")),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
                    Children =
                    {
                        UiHelper.Icon(windows ? AppIcons.Windows : AppIcons.Linux, 26, B(Color.Parse("#2563EB"))),
                        WithColumn(new StackPanel
                        {
                            Margin = new Thickness(12, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Children =
                            {
                                new TextBlock { Text = d.Name, FontWeight = FontWeight.Bold, FontSize = 14, Foreground = B(ink), TextTrimming = TextTrimming.CharacterEllipsis },
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal,
                                    Spacing = 6,
                                    Margin = new Thickness(0, 3, 0, 0),
                                    Children =
                                    {
                                        new Ellipse { Width = 7, Height = 7, Fill = B(stateColor == Muted ? inkMuted : stateColor), VerticalAlignment = VerticalAlignment.Center },
                                        new TextBlock { Text = $"{stateText} · {d.Os ?? d.Pool}", FontSize = 11, Foreground = B(inkMuted), TextTrimming = TextTrimming.CharacterEllipsis }
                                    }
                                }
                            }
                        }, 1),
                        WithColumn(btnStar, 2),
                        WithColumn(protocolBox, 3, new Thickness(6, 0, 8, 0)),
                        WithColumn(btnConnect, 4)
                    }
                }
            };
            row.PointerPressed += (_, _) => Select(d.Id);
            row.DoubleTapped += async (_, _) => await Connect();
            rowOf[d.Id] = row;
            return row;
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

        // the tiles call Load / SignOut, so they are filled in once those exist
        tiles.Children.Add(Tile(AppIcons.Refresh, L("Refresh"), () => Load()));
        tiles.Children.Add(Tile(AppIcons.Web, L("TilePortal"), () => { OpenUrl(api.BaseUri.ToString()); return Task.CompletedTask; }));
        tiles.Children.Add(Tile(AppIcons.Info, L("TileSupport"), () => { OpenUrl(supportUrl); return Task.CompletedTask; }));
        tiles.Children.Add(Tile(AppIcons.Settings, L("Settings"), async () =>
        {
            if (!await KioskGuard.CheckAsync(window, config)) { return; }
            await SettingsWindow.Create(config).ShowDialog(window);
        }));
        if (updateText is not null && clientConfig?.DownloadUrl is { Length: > 0 } dl)
        {
            tiles.Children.Add(Tile(AppIcons.Update, L("MoaviDownload"), () => { OpenUrl(dl); return Task.CompletedTask; }));
        }
        tiles.Children.Add(Tile(AppIcons.Login, L("MoaviSignOut"), async () =>
        {
            if (open.Count > 0 && !await DialogHelper.ConfirmAsync(window, L("ConfirmCloseSessions"))) { return; }
            await SignOut();
        }));

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
