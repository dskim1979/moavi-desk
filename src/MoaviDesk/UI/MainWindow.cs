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
/// The signed-in user's workspace. App bar (server, search, refresh, user menu), a filter rail
/// (all / starred / running), the desktops as tiles (state, star, spec, 연결 with the protocol, ⋯ for
/// power; double-click opens, right-click has the same menu) and a status bar (server, version).
/// Refreshes every 20s. Signing out closes the open viewers and ends their sessions.
/// </summary>
internal static class MainWindow
{
    // light surfaces over a picture (설정 › 일반 › 배경 이미지), hairline borders, one deep-teal accent
    private static readonly Color Panel = Color.Parse("#EBFFFFFF");
    private static readonly Color Raised = Color.Parse("#E6EEF5");
    private static readonly Color Hair = Color.Parse("#DCE3EC");
    private static readonly Color Ink = Color.Parse("#0F172A");
    private static readonly Color Soft = Color.Parse("#475569");
    private static readonly Color Subtle = Color.Parse("#64748B");
    private static readonly Color Action = Accent;



    public static Window Create(AppConfig config, DeskApi api, ClientConfig? clientConfig, string? notice)
    {
        var window = new Window
        {
            Title = $"{Product} · {api.Username}",
            Width = 1180,
            Height = 720,
            MinWidth = 860,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Backdrop(config),
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Light
        };
        if (config.Kiosk && config.KioskForceFullScreen) { window.WindowState = WindowState.FullScreen; }

        var open = new List<(Process Process, string SessionId)>();
        var connectors = new Dictionary<string, Func<Task>>();
        var autoConnected = false;
        var signedOut = false;
        var all = new List<DeskInfo>();
        var filter = "all";
        var serverOk = true;

        static T Col<T>(T c, int column) where T : Control { Grid.SetColumn(c, column); return c; }
        static T Docked<T>(T c, Dock dock) where T : Control { DockPanel.SetDock(c, dock); return c; }

        // --- info bar (Windows-style): one line, closable ----------------------------------------
        var infoText = new TextBlock { Foreground = B(Ink), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var infoClose = UiHelper.IconButton(AppIcons.Close, null, size: 12, foregroundColor: Subtle);
        var infoBar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = B(Hair),
            Padding = new Thickness(20, 8, 12, 8),
            IsVisible = false,
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { infoText, Col(infoClose, 1) } }
        };
        infoClose.Click += (_, _) => infoBar.IsVisible = false;
        void Notice(string? text, Color? tone = null)
        {
            infoText.Text = text ?? "";
            var c = tone ?? Accent;
            infoBar.Background = B(Color.FromArgb(0xF2, (byte)(255 - ((255 - c.R) / 8)), (byte)(255 - ((255 - c.G) / 8)), (byte)(255 - ((255 - c.B) / 8))));
            infoBar.IsVisible = !string.IsNullOrEmpty(text);
        }
        var updateText = clientConfig?.LatestVersion is { Length: > 0 } latest && LoginWindow.IsOlder(DeskApi.AppVersion, latest)
                            ? string.Format(L("MoaviUpdateAvailable"), latest)
                            : null;
        Notice(notice ?? updateText, notice is null ? Accent : Warn);

        // --- app bar -------------------------------------------------------------------------------
        var search = new TextBox
        {
            Watermark = L("SearchDesktops"),
            Width = 320,
            Height = 32,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerLeftContent = new Border { Padding = new Thickness(10, 0, 4, 0), Child = UiHelper.Icon(AppIcons.Search, 14, B(Subtle)) }
        };
        var btnRefresh = UiHelper.IconButton(AppIcons.Refresh, "Refresh", foregroundColor: Soft);

        var userMenu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var userButton = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            Flyout = userMenu,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 26,
                        Height = 26,
                        CornerRadius = new CornerRadius(4),
                        Background = B(Raised),
                        BorderBrush = B(Hair),
                        BorderThickness = new Thickness(1),
                        Child = new TextBlock
                        {
                            Text = api.Username.Length > 0 ? api.Username[..1].ToUpperInvariant() : "?",
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = B(Ink),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    },
                    new TextBlock { Text = api.Username, FontSize = 13, Foreground = B(Ink), VerticalAlignment = VerticalAlignment.Center },
                    new PathIcon { Data = Geometry.Parse("M7,10L12,15L17,10H7Z"), Width = 10, Height = 10, Foreground = B(Subtle) }
                }
            }
        };

        var appBar = new Border
        {
            Background = B(Panel),
            BorderBrush = B(Hair),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0, 12, 0),
            Height = 52,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            Lockup(24, 15, Ink),
                            new Border { Width = 1, Height = 18, Background = B(Hair), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = api.BaseUri.Host, FontSize = 12, Foreground = B(Subtle), VerticalAlignment = VerticalAlignment.Center }
                        }
                    },
                    Col(new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = search }, 1),
                    Col(btnRefresh, 2),
                    Col(userButton, 3)
                }
            }
        };

        // --- filter rail ---------------------------------------------------------------------------
        (Button Button, TextBlock Count) NavItem(string icon, string label)
        {
            var count = new TextBlock { FontSize = 12, Foreground = B(Subtle), VerticalAlignment = VerticalAlignment.Center };
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8),
                Content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    Children =
                    {
                        UiHelper.Icon(icon, 15, B(Soft)),
                        Col(new TextBlock { Text = label, FontSize = 13, Foreground = B(Ink), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, 1),
                        Col(count, 2)
                    }
                }
            };
            return (b, count);
        }
        var navAll = NavItem(AppIcons.ViewGrid, L("NavAll"));
        var navStar = NavItem(AppIcons.Star, L("NavStarred"));
        var navRunning = NavItem(AppIcons.Play, L("NavRunning"));

        var rail = new Border
        {
            Width = 216,
            Background = B(Panel),
            BorderBrush = B(Hair),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 16),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = L("NavHeading"), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = B(Subtle), Margin = new Thickness(10, 0, 0, 8) },
                    navAll.Button, navStar.Button, navRunning.Button
                }
            }
        };

        // --- content -------------------------------------------------------------------------------
        var heading = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), VerticalAlignment = VerticalAlignment.Center };
        var sortBox = new ComboBox
        {
            ItemsSource = new[] { L("SortName"), L("SortState") },
            SelectedIndex = 0,
            MinWidth = 120,
            FontSize = 12.5
        };
        var grid = new WrapPanel { Orientation = Orientation.Horizontal };
        var emptyTitle = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), HorizontalAlignment = HorizontalAlignment.Center };
        var emptyText = new TextBlock { FontSize = 12.5, Foreground = B(Subtle), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        var emptyIcon = UiHelper.Icon(AppIcons.Monitor, 40, B(Color.Parse("#B6C2D2")));
        emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
        var empty = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 80, 0, 0),
            IsVisible = false,
            Children = { emptyIcon, emptyTitle, emptyText }
        };
        var content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20, 12, 20),
                Spacing = 16,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Margin = new Thickness(0, 0, 12, 0),
                        Children = { heading, Col(sortBox, 1) }
                    },
                    empty,
                    grid
                }
            }
        };

        // --- status bar ----------------------------------------------------------------------------
        var statusDot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        var statusText = new TextBlock { FontSize = 11.5, Foreground = B(Subtle), VerticalAlignment = VerticalAlignment.Center };
        var sessionsText = new TextBlock { FontSize = 11.5, Foreground = B(Subtle), VerticalAlignment = VerticalAlignment.Center };
        var statusBar = new Border
        {
            Background = B(Panel),
            BorderBrush = B(Hair),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 0),
            Height = 26,
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { statusDot, statusText } },
                    Col(new Border { Margin = new Thickness(18, 0, 0, 0), Child = sessionsText }, 1),
                    Col(new TextBlock { Text = $"MOAVI Desk {DeskApi.AppVersion}", FontSize = 11.5, Foreground = B(Subtle), VerticalAlignment = VerticalAlignment.Center }, 2)
                }
            }
        };

        var body = new DockPanel();
        body.Children.Add(Docked(rail, Dock.Left));
        body.Children.Add(content);
        var root = new DockPanel();
        root.Children.Add(Docked(appBar, Dock.Top));
        root.Children.Add(Docked(infoBar, Dock.Top));
        root.Children.Add(Docked(statusBar, Dock.Bottom));
        root.Children.Add(body);
        window.Content = root;

        // --- behaviour -----------------------------------------------------------------------------
        void Status()
        {
            statusDot.Fill = B(serverOk ? Ok : Danger);
            statusText.Text = serverOk ? string.Format(L("StatusConnected"), api.BaseUri.Host) : L("MoaviServerOffline");
            sessionsText.Text = open.Count > 0 ? string.Format(L("StatusSessions"), open.Count) : "";
        }

        (string Text, Color Color) State(DeskInfo d) => d.State switch
        {
            "ready" => (L("MoaviStateReady"), Ok),
            "running" => (L("MoaviStateRunning"), Ok),
            "stopped" => (L("MoaviStateStopped"), Subtle),
            "error" => (L("MoaviStateError"), Danger),
            _ => (L("MoaviStateBusy"), Warn)
        };
        static bool Running(DeskInfo d) => d.State is "running" or "ready";

        void SetFilter(string f)
        {
            filter = f;
            foreach (var (key, nav) in new[] { ("all", navAll), ("star", navStar), ("running", navRunning) })
            {
                nav.Button.Background = key == f ? B(Raised) : Brushes.Transparent;
            }
            Render();
        }

        void Render()
        {
            var q = search.Text?.Trim() ?? "";
            IEnumerable<DeskInfo> list = all;
            if (filter == "star") { list = list.Where(d => d.Id == config.FavoriteDesktop); }
            if (filter == "running") { list = list.Where(Running); }
            if (q.Length > 0)
            {
                list = list.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || d.Pool.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            list = sortBox.SelectedIndex == 1
                ? list.OrderBy(d => Running(d) ? 0 : 1).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                : list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);
            var shown = list.ToList();

            heading.Text = filter switch { "star" => L("NavStarred"), "running" => L("NavRunning"), _ => L("NavAll") };
            navAll.Count.Text = all.Count.ToString();
            navStar.Count.Text = all.Count(d => d.Id == config.FavoriteDesktop).ToString();
            navRunning.Count.Text = all.Count(Running).ToString();

            grid.Children.Clear();
            connectors.Clear();
            foreach (var d in shown) { grid.Children.Add(Tile(d)); }
            empty.IsVisible = shown.Count == 0;
            emptyTitle.Text = all.Count == 0 ? L("MoaviNoDesktops") : L("EmptyFiltered");
            emptyText.Text = all.Count == 0 ? L("EmptyNoDesktopsHint") : L("EmptyFilteredHint");
            Status();
        }

        async Task Load()
        {
            try
            {
                all = await api.DesktopsAsync();
                serverOk = true;
                Render();
                // 자동 접속: the starred desktop, else the only one - once per sign-in
                if (!autoConnected && config.AutoConnect && !config.IsSharedPc)
                {
                    autoConnected = true;
                    var pick = all.FirstOrDefault(x => x.Id == config.FavoriteDesktop) ?? (all.Count == 1 ? all[0] : null);
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
                serverOk = ex.Status != 0;
                Status();
                Notice(ex.Message, Danger);
            }
        }

        async Task Power(DeskInfo d, string action, string? confirmKey)
        {
            if (confirmKey is not null && !await DialogHelper.ConfirmAsync(window, string.Format(L(confirmKey), d.Name))) { return; }
            try { await api.PowerAsync(d.Id, action); await Task.Delay(1500); await Load(); }
            catch (DeskApiException ex) { Notice(ex.Message, Danger); }
        }

        Control Tile(DeskInfo d)
        {
            var (stateText, stateColor) = State(d);
            var windows = d.Os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
            var canOpen = d.State is not ("provisioning" or "resetting" or "error");

            // RDP first, SPICE / VNC as the choice (browser RDP is the portal's)
            var offered = Viewer.Offered(d.Protocols);
            if (offered.Count == 0) { offered.Add("vnc"); }
            var protocol = offered[Math.Max(0, offered.IndexOf(config.Protocol))];

            var btnConnect = new SplitButton
            {
                Content = L("Connect"),
                IsEnabled = canOpen,
                Background = B(Action),
                Foreground = B(Colors.White),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12.5,
                Padding = new Thickness(14, 5),
                CornerRadius = new CornerRadius(4)
            };

            async Task Connect()
            {
                if (!btnConnect.IsEnabled) { return; }
                btnConnect.IsEnabled = false;
                try
                {
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
                    Status();
                    // through the Warpgate bastion: say so - the desktop's own logon screen asks for
                    // the user's account, nothing is typed for Warpgate and nothing is stored
                    if (c.Via == "warpgate") { Notice(L("RdpViaWarpgate"), Accent); }
                    process.EnableRaisingEvents = true;
                    process.Exited += async (_, _) =>
                    {
                        open.RemoveAll(o => o.Process == process);
                        Dispatcher.UIThread.Post(Status);
                        await api.EndSessionAsync(c.SessionId);
                    };
                }
                catch (DeskApiException ex) { Notice(ex.Message, Danger); }
                finally { btnConnect.IsEnabled = true; }
            }
            btnConnect.Click += async (_, _) => await Connect();
            if (canOpen) { connectors[d.Id] = Connect; }

            // the split part picks the protocol for this connection
            var protoMenu = new MenuFlyout();
            foreach (var p in offered)
            {
                var item = new MenuItem { Header = string.Format(L("ConnectWith"), p.ToUpperInvariant()) };
                item.Click += async (_, _) => { protocol = p; await Connect(); };
                protoMenu.Items.Add(item);
            }
            btnConnect.Flyout = protoMenu;
            ToolTip.SetTip(btnConnect, $"{L("DetailProtocols")}: {protocol.ToUpperInvariant()}");

            // the star: one desktop at a time, opened by 자동 접속
            var starred = d.Id == config.FavoriteDesktop;
            Task ToggleStar()
            {
                config.FavoriteDesktop = starred ? string.Empty : d.Id;
                AppConfigManager.Save(config);
                Render();
                return Task.CompletedTask;
            }

            // ⋯ and right-click: the same menu
            MenuFlyout Menu()
            {
                var m = new MenuFlyout();
                void Add(string header, Func<Task> act, bool enabled = true)
                {
                    var item = new MenuItem { Header = header, IsEnabled = enabled };
                    item.Click += async (_, _) => await act();
                    m.Items.Add(item);
                }
                Add(L("Connect"), Connect, canOpen);
                Add(L(starred ? "FavoriteRemove" : "FavoriteAdd"), ToggleStar);
                m.Items.Add(new Separator());
                Add(L("Start"), () => Power(d, "start", null));
                Add(L("MoaviReboot"), () => Power(d, "reboot", "MoaviConfirmReboot"));
                Add(L("Shutdown"), () => Power(d, "shutdown", "MoaviConfirmShutdown"));
                return m;
            }
            var btnMore = UiHelper.IconButton(AppIcons.DotsVertical, "More", size: 15, foregroundColor: Soft);
            btnMore.Flyout = Menu();

            // the preview: the OS mark on a muted field, the state chip and the star on top
            var star = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Content = UiHelper.Icon(AppIcons.Star, 15, B(starred ? Color.Parse("#E59E0B") : Color.Parse("#A3B0C2")))
            };
            ToolTip.SetTip(star, L(starred ? "FavoriteRemove" : "FavoriteAdd"));
            star.Click += async (_, _) => await ToggleStar();

            var chip = new Border
            {
                Background = B(Color.Parse("#F2FFFFFF")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new Ellipse { Width = 6, Height = 6, Fill = B(stateColor), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = stateText, FontSize = 11, Foreground = B(Ink) }
                    }
                }
            };

            var mark = UiHelper.Icon(windows ? AppIcons.Windows : AppIcons.Linux, 40, B(windows ? Color.Parse("#2F6FE4") : Color.Parse("#334155")));
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Opacity = Running(d) ? 0.95 : 0.4;
            var preview = new Border
            {
                Height = 118,
                Background = B(windows ? Color.Parse("#E7EFFB") : Color.Parse("#ECEFF4")),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                Child = new Grid { Margin = new Thickness(8), Children = { mark, chip, star } }
            };

            var spec = string.Join("  ·  ", new[]
            {
                d.Spec?.Cpu is { } cpu ? $"{cpu} vCPU" : null,
                d.Spec?.MemoryGb is { } mem ? $"{mem:0.#} GB" : null,
                d.Spec?.Gpu
            }.Where(x => !string.IsNullOrEmpty(x)));

            var tile = new Border
            {
                Width = 252,
                Margin = new Thickness(0, 0, 12, 12),
                Background = B(Colors.White),
                BoxShadow = BoxShadows.Parse("0 1 3 0 #140F172A"),
                BorderBrush = B(Hair),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ContextFlyout = Menu(),
                Child = new StackPanel
                {
                    Children =
                    {
                        preview,
                        new StackPanel
                        {
                            Margin = new Thickness(14, 12, 10, 12),
                            Spacing = 3,
                            Children =
                            {
                                new TextBlock { Text = d.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), TextTrimming = TextTrimming.CharacterEllipsis },
                                new TextBlock { Text = string.Join("  ·  ", new[] { d.Pool, d.Os }.Where(x => !string.IsNullOrEmpty(x))), FontSize = 12, Foreground = B(Subtle), TextTrimming = TextTrimming.CharacterEllipsis },
                                new TextBlock { Text = spec, FontSize = 12, Foreground = B(Subtle), IsVisible = spec.Length > 0 },
                                new Grid
                                {
                                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                                    Margin = new Thickness(0, 10, 0, 0),
                                    Children = { btnConnect, Col(btnMore, 1) }
                                }
                            }
                        }
                    }
                }
            };
            tile.PointerEntered += (_, _) => tile.BorderBrush = B(Color.Parse("#9FB2CB"));
            tile.PointerExited += (_, _) => tile.BorderBrush = B(Hair);
            tile.DoubleTapped += async (_, _) => await Connect();
            return tile;
        }

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

        // the user menu
        var supportUrl = clientConfig?.SupportUrl is { Length: > 0 } s ? s : DefaultSupportUrl;
        void MenuAdd(string header, Func<Task> act)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await act();
            userMenu.Items.Add(item);
        }
        MenuAdd(L("Settings"), async () =>
        {
            if (!await KioskGuard.CheckAsync(window, config)) { return; }
            await SettingsWindow.Create(config).ShowDialog(window);
            window.Background = Backdrop(config);   // a new picture shows at once
        });
        MenuAdd(L("TilePortal"), () => { OpenUrl(api.BaseUri.ToString()); return Task.CompletedTask; });
        MenuAdd(L("TileSupport"), () => { OpenUrl(supportUrl); return Task.CompletedTask; });
        if (updateText is not null && clientConfig?.DownloadUrl is { Length: > 0 } dl)
        {
            MenuAdd(L("MoaviDownload"), () => { OpenUrl(dl); return Task.CompletedTask; });
        }
        userMenu.Items.Add(new Separator());
        MenuAdd(L("MoaviSignOut"), async () =>
        {
            if (open.Count > 0 && !await DialogHelper.ConfirmAsync(window, L("ConfirmCloseSessions"))) { return; }
            await SignOut();
        });

        btnRefresh.Click += async (_, _) => await Load();
        navAll.Button.Click += (_, _) => SetFilter("all");
        navStar.Button.Click += (_, _) => SetFilter("star");
        navRunning.Button.Click += (_, _) => SetFilter("running");
        search.TextChanged += (_, _) => Render();
        sortBox.SelectionChanged += (_, _) => Render();
        SetFilter("all");

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
