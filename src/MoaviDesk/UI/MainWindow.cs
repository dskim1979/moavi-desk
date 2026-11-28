/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Diagnostics;
using Avalonia.Media.Imaging;
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
            Width = 1320,
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
        // the screen pictures (as the portal card shows them), the selected tile, loans and notices
        var shots = new Dictionary<string, (Bitmap? Image, bool Off)>();
        string? selectedId = null;
        string? armedFor = null;   // the desktop whose sign-in was armed ahead: once per pick, not on every refresh
        var leases = new List<LeaseInfo>();
        var banners = new List<BannerInfo>();
        DateTime? refreshedAt = null;

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
        // a closed notice stays closed for this sign-in
        var dismissed = new HashSet<string>();
        string? shownBanner = null;
        infoClose.Click += (_, _) =>
        {
            infoBar.IsVisible = false;
            if (shownBanner is not null) { dismissed.Add(shownBanner); shownBanner = null; }
        };
        void Notice(string? text, Color? tone = null)
        {
            infoText.Text = text ?? "";
            shownBanner = null;
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
        var navRecent = NavItem(AppIcons.Clock, L("NavRecent"));

        TextBlock RailHeading(string key) => new()
        {
            Text = L(key), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6,
            Foreground = B(Subtle), Margin = new Thickness(10, 18, 0, 6)
        };
        // 대여 중: cards, USB devices and desktops on loan, with when they go back
        var leaseList = new StackPanel { Spacing = 2 };
        var leaseHeading = RailHeading("RailLeases");
        // 바로 가기: the web portal and remote support
        Button RailLink(string icon, string key, Action act)
        {
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7),
                Content = UiHelper.WithText(icon, L(key), B(Ink))
            };
            b.Click += (_, _) => act();
            return b;
        }
        var supportUrl = clientConfig?.SupportUrl is { Length: > 0 } s ? s : DefaultSupportUrl;

        var rail = new Border
        {
            Width = 216,
            Background = B(Panel),
            BorderBrush = B(Hair),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 16),
            Child = new ScrollViewer
            {
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = L("NavHeading"), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = B(Subtle), Margin = new Thickness(10, 0, 0, 8) },
                        navAll.Button, navStar.Button, navRunning.Button, navRecent.Button,
                        leaseHeading, leaseList,
                        RailHeading("RailLinks"),
                        RailLink(AppIcons.Web, "TilePortal", () => OpenUrl(api.BaseUri.ToString())),
                        RailLink(AppIcons.Info, "TileSupport", () => OpenUrl(supportUrl))
                    }
                }
            }
        };

        // --- content -------------------------------------------------------------------------------
        var heading = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), VerticalAlignment = VerticalAlignment.Center };
        var sortBox = new ComboBox
        {
            ItemsSource = new[] { L("SortName"), L("SortState"), L("SortRecent") },
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

        // --- the selected desktop: its details, shared folders and actions -------------------------
        var detail = new StackPanel { Spacing = 14 };
        var detailPane = new Border
        {
            Width = 300,
            Background = B(Panel),
            BorderBrush = B(Hair),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = new Border { Padding = new Thickness(18, 18, 16, 18), Child = detail } }
        };

        var body = new DockPanel();
        body.Children.Add(Docked(rail, Dock.Left));
        body.Children.Add(Docked(detailPane, Dock.Right));
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
            var parts = new List<string>();
            if (open.Count > 0) { parts.Add(string.Format(L("StatusSessions"), open.Count)); }
            if (refreshedAt is { } at) { parts.Add(string.Format(L("StatusUpdated"), at.ToString("HH:mm:ss"))); }
            sessionsText.Text = string.Join("   ·   ", parts);
        }

        // "3시간 전" from an ISO time or epoch seconds
        static DateTimeOffset? When(string? iso) => DateTimeOffset.TryParse(iso, out var t) ? t : null;
        static string Ago(DateTimeOffset t)
        {
            var d = DateTimeOffset.Now - t;
            if (d.TotalMinutes < 1) { return L("AgoJustNow"); }
            if (d.TotalHours < 1) { return string.Format(L("AgoMinutes"), (int)d.TotalMinutes); }
            if (d.TotalDays < 1) { return string.Format(L("AgoHours"), (int)d.TotalHours); }
            return string.Format(L("AgoDays"), (int)d.TotalDays);
        }
        static string? SignedInText(DeskInfo d)
        {
            if (d.SignedIn is not { Count: > 0 } who) { return null; }
            var text = who[0].User + (who.Count > 1 ? $" +{who.Count - 1}" : "");
            if (who[0].LoginTime is { } lt) { text += " · " + Ago(DateTimeOffset.FromUnixTimeSeconds((long)lt)); }
            return text;
        }

        void Copy(string text)
        {
            _ = window.Clipboard?.SetTextAsync(text);
            Notice(string.Format(L("Copied"), text), Accent);
        }

        void RenderDetail()
        {
            detail.Children.Clear();
            var d = all.FirstOrDefault(x => x.Id == selectedId);
            if (d is null)
            {
                var hint = UiHelper.Icon(AppIcons.Info, 28, B(Color.Parse("#B6C2D2")));
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                detail.Children.Add(new StackPanel
                {
                    Spacing = 8, Margin = new Thickness(0, 60, 0, 0),
                    Children =
                    {
                        hint,
                        new TextBlock { Text = L("PanelPick"), FontSize = 12.5, Foreground = B(Subtle), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
                    }
                });
                return;
            }
            var (stateText, stateColor) = State(d);
            // 자동 로그인: arm it while the user looks at the desktop, so the connect only checks it
            if (Running(d) && d.Policy?.DesktopSso == true && d.Protocols.Contains("rdp", StringComparer.OrdinalIgnoreCase)
                && armedFor != d.Id)
            {
                // once when it is picked (200 users idling with a desktop picked must not keep the guests busy);
                // a connect much later arms it itself
                armedFor = d.Id;
                _ = api.ArmSignInAsync(d.Id);
            }

            // the screen, larger
            if (shots.TryGetValue(d.Id, out var shot) && shot.Image is not null)
            {
                detail.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(5), ClipToBounds = true, BorderBrush = B(Hair), BorderThickness = new Thickness(1),
                    Child = new Image { Source = shot.Image, Stretch = Stretch.Uniform }
                });
            }
            detail.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = d.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children =
                        {
                            new Ellipse { Width = 7, Height = 7, Fill = B(stateColor), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = stateText, FontSize = 12.5, Foreground = B(Soft) }
                        }
                    }
                }
            });

            Grid Rows(IEnumerable<(string Key, string? Value)> rows)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 7, ColumnSpacing = 14 };
                var i = 0;
                foreach (var (key, value) in rows.Where(r => !string.IsNullOrEmpty(r.Value)))
                {
                    g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var k = new TextBlock { Text = L(key), FontSize = 12, Foreground = B(Subtle) };
                    var v = new TextBlock { Text = value, FontSize = 12, Foreground = B(Ink), TextWrapping = TextWrapping.Wrap };
                    Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                    g.Children.Add(k); g.Children.Add(v);
                    i++;
                }
                return g;
            }
            TextBlock Heading(string key) => new()
            {
                Text = L(key), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.6, Foreground = B(Subtle), Margin = new Thickness(0, 4, 0, 0)
            };

            var spec = string.Join(" · ", new[]
            {
                d.Spec?.Cpu is { } cpu ? $"{cpu} vCPU" : null,
                d.Spec?.MemoryGb is { } mem ? $"{mem:0.#} GB" : null,
                d.Spec?.Gpu
            }.Where(x => !string.IsNullOrEmpty(x)));
            detail.Children.Add(Heading("PanelDetails"));
            detail.Children.Add(Rows(
            [
                ("RowOs", d.Os),
                ("RowPool", d.Pool),
                ("RowSpec", spec),
                ("DetailProtocols", string.Join(", ", d.Protocols.Select(p => p.ToUpperInvariant()))),
                ("RowSignedIn", SignedInText(d)),
                ("RowLast", When(d.LastConnected) is { } lc ? Ago(lc) : null),
                ("RowClipboard", d.Policy?.Clipboard switch
                {
                    "both" => L("ClipBoth"), "to_desktop" => L("ClipIn"), "from_desktop" => L("ClipOut"), "off" => L("ClipOff"), _ => null
                })
            ]));

            if (d.Resources is { Count: > 0 } res)
            {
                detail.Children.Add(Heading("PanelResources"));
                foreach (var r in res)
                {
                    detail.Children.Add(UiHelper.WithText(r.Kind == "usb" ? AppIcons.Ftp : AppIcons.ChartBar, r.Name, B(Ink)));
                }
            }

            // 공유 폴더: the path for this desktop's OS, with a copy button
            if (d.Storage?.Folders is { Count: > 0 } folders)
            {
                detail.Children.Add(Heading("PanelStorage"));
                var windows = d.Os?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
                foreach (var (key, f) in folders)
                {
                    var path = (windows ? f.Windows : f.Linux) ?? f.Linux ?? f.Windows;
                    if (string.IsNullOrEmpty(path)) { continue; }
                    var copy = UiHelper.IconButton(AppIcons.Copy, null, size: 13, foregroundColor: Soft);
                    ToolTip.SetTip(copy, L("CopyPath"));
                    copy.Click += (_, _) => Copy(path);
                    detail.Children.Add(new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children =
                        {
                            new StackPanel
                            {
                                Spacing = 1,
                                Children =
                                {
                                    new TextBlock { Text = L("Folder_" + key) + (f.QuotaGb is { } q ? $" · {q:0.#} GB" : ""), FontSize = 12, Foreground = B(Soft) },
                                    new TextBlock { Text = path, FontSize = 12, Foreground = B(Ink), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, monospace") }
                                }
                            },
                            Col(copy, 1)
                        }
                    });
                }
            }

            // 장치: what this PC passes to the desktop at the next connect (only what the pool allows)
            if (d.Protocols.Contains("rdp", StringComparer.OrdinalIgnoreCase))
            {
                detail.Children.Add(Heading("PanelDevices"));
                var choice = config.Devices.GetValueOrDefault(d.Id) ?? new DeviceChoice();
                var boxes = new WrapPanel();
                CheckBox Box(string key, bool allowed, bool on, Action<DeviceChoice, bool> set)
                {
                    var cb = new CheckBox
                    {
                        Content = L(key), IsChecked = allowed && on, IsEnabled = allowed, FontSize = 12.5,
                        Margin = new Thickness(0, 0, 14, 0), Foreground = B(allowed ? Ink : Subtle)
                    };
                    if (!allowed) { ToolTip.SetTip(cb, L("DeviceNotAllowed")); }
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        var c = config.Devices.GetValueOrDefault(d.Id) ?? new DeviceChoice();
                        set(c, cb.IsChecked == true);
                        config.Devices[d.Id] = c;
                        AppConfigManager.Save(config);
                    };
                    return cb;
                }
                boxes.Children.Add(Box("DeviceUsb", d.Policy?.UsbRedirect != false, choice.Usb, (c, v) => c.Usb = v));
                boxes.Children.Add(Box("DeviceDrives", d.Policy?.ClientDrives != false, choice.Drives, (c, v) => c.Drives = v));
                boxes.Children.Add(Box("DevicePrinters", d.Policy?.ClientPrinters != false, choice.Printers, (c, v) => c.Printers = v));
                detail.Children.Add(boxes);
                detail.Children.Add(new TextBlock { Text = L("DeviceHint"), FontSize = 11, Foreground = B(Subtle), TextWrapping = TextWrapping.Wrap });
            }

            // actions
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            Button Act(string key, Func<Task> act, bool primary = false, bool enabled = true)
            {
                var b = new Button
                {
                    Content = L(key), IsEnabled = enabled, FontSize = 12.5, Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(4),
                    Background = primary ? B(Action) : B(Colors.White), Foreground = primary ? B(Colors.White) : B(Ink),
                    BorderBrush = B(Hair), BorderThickness = new Thickness(primary ? 0 : 1)
                };
                b.Click += async (_, _) => await act();
                return b;
            }
            var canOpen = d.State is not ("provisioning" or "resetting" or "error");
            actions.Children.Add(Act("Connect", () => connectors.TryGetValue(d.Id, out var go) ? go() : Task.CompletedTask, true, canOpen));
            if (d.Policy?.AllowPower != false)
            {
                actions.Children.Add(Act("Start", () => Power(d, "start", null), enabled: !Running(d)));
                actions.Children.Add(Act("MoaviReboot", () => Power(d, "reboot", "MoaviConfirmReboot"), enabled: Running(d)));
                actions.Children.Add(Act("Shutdown", () => Power(d, "shutdown", "MoaviConfirmShutdown"), enabled: Running(d)));
            }
            detail.Children.Add(actions);
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
            foreach (var (key, nav) in new[] { ("all", navAll), ("star", navStar), ("running", navRunning), ("recent", navRecent) })
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
            if (filter == "recent") { list = list.Where(d => When(d.LastConnected) is not null); }
            if (q.Length > 0)
            {
                list = list.Where(d => d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || d.Pool.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            list = sortBox.SelectedIndex switch
            {
                1 => list.OrderBy(d => Running(d) ? 0 : 1).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
                2 => list.OrderByDescending(d => When(d.LastConnected) ?? DateTimeOffset.MinValue),
                _ => list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            var shown = list.ToList();

            heading.Text = filter switch { "star" => L("NavStarred"), "running" => L("NavRunning"), "recent" => L("NavRecent"), _ => L("NavAll") };
            navAll.Count.Text = all.Count.ToString();
            navStar.Count.Text = all.Count(d => d.Id == config.FavoriteDesktop).ToString();
            navRunning.Count.Text = all.Count(Running).ToString();
            navRecent.Count.Text = all.Count(d => When(d.LastConnected) is not null).ToString();

            // the first desktop is selected until the user picks one
            if (selectedId is null || all.All(d => d.Id != selectedId)) { selectedId = shown.FirstOrDefault()?.Id; }

            grid.Children.Clear();
            connectors.Clear();
            foreach (var d in shown) { grid.Children.Add(Tile(d)); }
            empty.IsVisible = shown.Count == 0;
            emptyTitle.Text = all.Count == 0 ? L("MoaviNoDesktops") : L("EmptyFiltered");
            emptyText.Text = all.Count == 0 ? L("EmptyNoDesktopsHint") : L("EmptyFilteredHint");

            leaseHeading.IsVisible = leases.Count > 0;
            leaseList.Children.Clear();
            foreach (var l in leases)
            {
                var ends = When(l.EndsAt);
                leaseList.Children.Add(new StackPanel
                {
                    Margin = new Thickness(10, 3, 6, 3),
                    Spacing = 1,
                    Children =
                    {
                        UiHelper.WithText(l.Kind == "usb" ? AppIcons.Ftp : l.Kind == "desktop" ? AppIcons.Monitor : AppIcons.ChartBar, l.Name, B(Ink)),
                        new TextBlock
                        {
                            Text = string.Join(" · ", new[] { l.Desktop, ends is { } e ? string.Format(L("LeaseEnds"), e.ToLocalTime().ToString("MM-dd HH:mm")) : null }.Where(x => !string.IsNullOrEmpty(x))),
                            FontSize = 11, Foreground = B(Subtle), Margin = new Thickness(22, 0, 0, 0)
                        }
                    }
                });
            }
            RenderDetail();
            Status();
        }

        // the screens of the running desktops, all at once (the engine caches them ~20 s)
        async Task LoadShots()
        {
            var want = all.Where(d => Running(d) && d.Policy?.ScreenThumbnail != false).ToList();
            var got = await Task.WhenAll(want.Select(async d => (d.Id, Shot: await api.ScreenshotAsync(d.Id))));
            var old = shots.Values.Select(v => v.Image).Where(i => i is not null).ToList();
            shots.Clear();
            foreach (var (id, (png, off)) in got)
            {
                Bitmap? img = null;
                if (png is { Length: > 0 })
                {
                    try { using var ms = new MemoryStream(png); img = new Bitmap(ms); } catch { img = null; }
                }
                shots[id] = (img, off);
            }
            Render();
            foreach (var i in old) { i!.Dispose(); }
        }

        async Task Load()
        {
            try
            {
                all = await api.DesktopsAsync();
                serverOk = true;
                refreshedAt = DateTime.Now;
                leases = await api.LeasesAsync();
                Render();
                // a notice from the administrators, unless something else is being said
                banners = await api.BannersAsync();
                if (!infoBar.IsVisible && banners.FirstOrDefault(b => !dismissed.Contains(b.Id)) is { } bn)
                {
                    Notice(bn.Text, bn.Severity is "critical" or "error" ? Danger : bn.Severity == "warning" ? Warn : Accent);
                    shownBanner = bn.Id;
                }
                _ = LoadShots();
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
                // RDP: the window opens now and loads while the engine prepares the desktop
                var early = protocol == "rdp" ? RdpLauncher.Start(d.Name, config.ViewerFullScreen) : null;
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
                                                                       d.Name, config.ViewerFullScreen,
                                                                       config.Devices.GetValueOrDefault(d.Id), early);
                    early = null;
                    if (process is null)
                    {
                        Notice(error, Danger);
                        await api.EndSessionAsync(c.SessionId);
                        return;
                    }
                    open.Add((process, c.SessionId));
                    Status();
                    // the desktop's own logon screen asks for the user's account (nothing is typed or stored here);
                    // which route the connection took is the pool's business and is not shown
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
                finally { early?.Cancel(); btnConnect.IsEnabled = true; }
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
                if (d.Policy?.AllowPower != false)
                {
                    m.Items.Add(new Separator());
                    Add(L("Start"), () => Power(d, "start", null), !Running(d));
                    Add(L("MoaviReboot"), () => Power(d, "reboot", "MoaviConfirmReboot"), Running(d));
                    Add(L("Shutdown"), () => Power(d, "shutdown", "MoaviConfirmShutdown"), Running(d));
                }
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

            // the desktop's screen (as on the portal card), else 화면 꺼짐, else the OS mark;
            // who is signed in rides on it as a small dark chip
            shots.TryGetValue(d.Id, out var shot);
            Control centre = mark;
            if (shot.Image is not null)
            {
                centre = new Image { Source = shot.Image, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            else if (shot.Off)
            {
                centre = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 4,
                    Children =
                    {
                        UiHelper.Icon(AppIcons.Monitor, 26, B(Color.Parse("#94A3B8"))),
                        new TextBlock { Text = L("ScreenOff"), FontSize = 11.5, Foreground = B(Color.Parse("#CBD5E1")), HorizontalAlignment = HorizontalAlignment.Center }
                    }
                };
            }
            var who = SignedInText(d);
            var whoChip = new Border
            {
                IsVisible = who is not null,
                Background = B(Color.Parse("#B80B1220")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3),
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 5,
                    Children =
                    {
                        UiHelper.Icon(AppIcons.Account, 11, B(Colors.White)),
                        new TextBlock { Text = who ?? "", FontSize = 11, Foreground = B(Colors.White), FontWeight = FontWeight.SemiBold }
                    }
                }
            };
            var preview = new Border
            {
                Height = 140,
                Background = B(shot.Image is not null || shot.Off ? Color.Parse("#0F172A") : windows ? Color.Parse("#E7EFFB") : Color.Parse("#ECEFF4")),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                ClipToBounds = true,
                Child = new Grid
                {
                    Children =
                    {
                        centre,
                        new Grid { Margin = new Thickness(8), Children = { chip, star } },
                        whoChip
                    }
                }
            };
            if (shot.Image is not null) { ToolTip.SetTip(preview, L("ScreenTip")); }

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
                                new TextBlock
                                {
                                    Text = When(d.LastConnected) is { } lc ? string.Format(L("LastConnected"), Ago(lc)) : "",
                                    FontSize = 11.5, Foreground = B(Subtle), IsVisible = When(d.LastConnected) is not null
                                },
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
            // a click selects it (정보 on the right), a double-click connects
            var selected = d.Id == selectedId;
            if (selected) { tile.BorderBrush = B(Action); tile.BorderThickness = new Thickness(2); tile.Margin = new Thickness(-1, -1, 11, 11); }
            tile.PointerEntered += (_, _) => { if (!selected) { tile.BorderBrush = B(Color.Parse("#9FB2CB")); } };
            tile.PointerExited += (_, _) => { if (!selected) { tile.BorderBrush = B(Hair); } };
            tile.Tapped += (_, _) => { if (selectedId != d.Id) { selectedId = d.Id; Render(); } };
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
        navRecent.Button.Click += (_, _) => SetFilter("recent");
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
