/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Avalonia.Platform.Storage;
using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI.Helpers;
using static MoaviDesk.UI.Brand;

namespace MoaviDesk.UI;

/// <summary>Client settings in sections on the left: 서버, 일반, RDP, LAB · 키오스크, 정보. One Save for all.</summary>
internal static class SettingsWindow
{
    private static readonly string[] Protocols = ["auto", "rdp", "spice", "vnc"];

    public static Window Create(AppConfig config)
    {
        var window = new Window
        {
            Title = $"{Product} · {L("Settings")}",
            Width = 760,
            Height = 560,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B(Bg),
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Light
        };

        var (host, port, https) = DeskApi.ParseServer(config.ServerUrl);
        var txtHost = UiHelper.TextBox(host, L("MoaviServerWatermark"), AppIcons.Server);
        var txtPort = UiHelper.TextBox(port.ToString(), "443");
        txtPort.Width = 96;
        var chkHttps = new CheckBox { Content = L("MoaviUseHttps"), IsChecked = https };
        var chkCert = new CheckBox { Content = L("MoaviIgnoreCert"), IsChecked = config.IgnoreCertificateErrors };
        // the usual port follows the scheme until the user typed another one
        chkHttps.IsCheckedChanged += (_, _) =>
        {
            if (txtPort.Text is "443" or "80" or "") { txtPort.Text = chkHttps.IsChecked == true ? "443" : "80"; }
        };

        var btnTest = GhostButton(L("MoaviTestConnection"), AppIcons.Network);
        btnTest.Name = "TestConnection";
        var lblTest = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };

        // the server fields as a URL, or an error to show
        (string? Url, string? Error) ServerFromFields()
        {
            var h = txtHost.Text?.Trim() ?? "";
            if (h.Length == 0) { return (null, L("MoaviServerRequired")); }
            if (!int.TryParse(txtPort.Text?.Trim(), out var p) || p is < 1 or > 65535) { return (null, L("MoaviPortInvalid")); }
            try
            {
                var url = DeskApi.ComposeServer(h, p, chkHttps.IsChecked == true);
                _ = DeskApi.NormalizeServer(url);
                return (url, null);
            }
            catch { return (null, L("MoaviServerInvalid")); }
        }

        btnTest.Click += async (_, _) =>
        {
            var (url, error) = ServerFromFields();
            lblTest.IsVisible = true;
            if (url is null)
            {
                lblTest.Text = error;
                lblTest.Foreground = B(Danger);
                return;
            }
            btnTest.IsEnabled = false;
            lblTest.Text = L("MoaviServerChecking");
            lblTest.Foreground = B(Muted);
            var (ok, ms, why) = await DeskApi.ProbeAsync(url, chkCert.IsChecked == true, certSha256: config.ServerCertSha256);
            lblTest.Text = ok ? string.Format(L("MoaviTestOk"), ms) : string.Format(L("MoaviTestFail"), why);
            lblTest.Foreground = B(ok ? Ok : Danger);
            btnTest.IsEnabled = true;
        };

        var txtViewer = UiHelper.TextBox(Viewer.FindViewer(config.ViewerPath), L("ViewerPathWatermark"));
        var btnBrowse = UiHelper.IconButton(AppIcons.Folder, "SelectSpiceViewer", foregroundColor: Muted);
        btnBrowse.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L("SelectSpiceViewer"),
                AllowMultiple = false
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { txtViewer.Text = path; }
        };

        var cmbProtocol = new ComboBox
        {
            ItemsSource = new[] { "Auto (RDP → SPICE → VNC)", "RDP", "SPICE", "VNC" },
            SelectedIndex = Math.Max(0, Array.IndexOf(Protocols, config.Protocol)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var chkFull = new CheckBox { Content = L("MoaviFullScreen"), IsChecked = config.ViewerFullScreen };
        // the picture behind the desktops window; empty = the built-in one
        var txtBackground = UiHelper.TextBox(config.WorkspaceBackground, L("BackgroundDefault"));
        var btnBackground = UiHelper.IconButton(AppIcons.Folder, "BackgroundPick", foregroundColor: Muted);
        btnBackground.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L("BackgroundPick"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(L("BackgroundImages")) { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp"] }]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { txtBackground.Text = path; }
        };
        var btnBackgroundReset = GhostButton(L("BackgroundReset"));
        btnBackgroundReset.Click += (_, _) => txtBackground.Text = string.Empty;
        var chkAutoConnect = new CheckBox { Content = L("AutoConnect"), IsChecked = config.AutoConnect, IsEnabled = !config.IsSharedPc };

        // RDP window
        int[] depths = [32, 24, 16, 15];
        var cmbDepth = Combo(depths.Select(d => $"{d} bit").ToArray(), Array.IndexOf(depths, config.RdpColorDepth));
        string[] audios = ["local", "remote", "off"];
        var cmbAudio = Combo([L("RdpAudioLocal"), L("RdpAudioRemote"), L("RdpAudioOff")], Array.IndexOf(audios, config.RdpAudio));
        var chkMic = new CheckBox { Content = L("RdpMicrophone"), IsChecked = config.RdpMicrophone };
        int[] scales = [0, 100, 125, 150, 175, 200];
        var cmbScale = Combo(scales.Select(x => x == 0 ? L("RdpScaleAuto") : $"{x}%").ToArray(), Array.IndexOf(scales, config.RdpScale));
        string[] nets = ["auto", "lan", "broadband", "wan"];
        var cmbNet = Combo([L("RdpNetAuto"), "LAN", L("RdpNetBroadband"), "WAN"], Array.IndexOf(nets, config.RdpNetwork));

        var txtKey = UiHelper.TextBox(config.TerminalKey, "mdt_…", AppIcons.Key);

        var chkKiosk = new CheckBox { Content = L("KioskEnable"), IsChecked = config.Kiosk };
        var chkKioskFull = new CheckBox { Content = L("KioskForceFullScreen"), IsChecked = config.KioskForceFullScreen };
        var txtPass = UiHelper.TextBox(watermark: string.IsNullOrEmpty(config.KioskAdminPasswordHash)
                                                    ? L("KioskAdminPassword")
                                                    : L("KioskPasswordSet"), iconData: AppIcons.Lock);
        txtPass.PasswordChar = '●';
        var txtPass2 = UiHelper.TextBox(watermark: L("KioskConfirmPassword"), iconData: AppIcons.Lock);
        txtPass2.PasswordChar = '●';

        var lblError = new TextBlock { Foreground = B(Danger), TextWrapping = TextWrapping.Wrap, IsVisible = false };

        // what the PC's config decides (%ProgramData%\MOAVI Desk) is shown, not changed here
        void Lock(string setting, params Control[] controls)
        {
            if (!config.IsManaged(setting)) { return; }
            foreach (var c in controls) { c.IsEnabled = false; }
        }
        Lock(nameof(AppConfig.ServerUrl), txtHost, txtPort, chkHttps);
        Lock(nameof(AppConfig.IgnoreCertificateErrors), chkCert);
        if (config.IsManaged(nameof(AppConfig.ServerCertSha256))) { chkCert.IsEnabled = false; }
        Lock(nameof(AppConfig.Protocol), cmbProtocol);
        Lock(nameof(AppConfig.TerminalKey), txtKey);
        Lock(nameof(AppConfig.Kiosk), chkKiosk);
        Lock(nameof(AppConfig.KioskForceFullScreen), chkKioskFull);
        Lock(nameof(AppConfig.KioskAdminPasswordHash), txtPass, txtPass2);
        var lblManaged = new TextBlock
        {
            Text = L("MoaviManagedNote"),
            Foreground = B(Warn),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = config.Managed.Count > 0
        };

        var btnSave = PrimaryButton(L("Save"));
        var btnCancel = GhostButton(L("Cancel"));
        btnCancel.Click += (_, _) => window.Close();

        btnSave.Click += (_, _) =>
        {
            var (server, serverError) = ServerFromFields();
            if (server is null && (txtHost.Text?.Trim().Length ?? 0) > 0) { Show(serverError!); return; }
            var pass = txtPass.Text ?? "";
            if (pass != (txtPass2.Text ?? "")) { Show(L("KioskPasswordMismatch")); return; }
            if (chkKiosk.IsChecked == true && pass.Length == 0 && string.IsNullOrEmpty(config.KioskAdminPasswordHash))
            {
                Show(L("KioskPasswordRequired"));
                return;
            }

            config.ServerUrl = server ?? "";
            config.IgnoreCertificateErrors = chkCert.IsChecked == true;
            config.ViewerPath = txtViewer.Text?.Trim() ?? "";
            config.Protocol = Protocols[Math.Max(0, cmbProtocol.SelectedIndex)];
            config.ViewerFullScreen = chkFull.IsChecked == true;
            config.AutoConnect = chkAutoConnect.IsChecked == true;
            config.WorkspaceBackground = txtBackground.Text?.Trim() ?? "";
            config.RdpColorDepth = depths[Math.Max(0, cmbDepth.SelectedIndex)];
            config.RdpAudio = audios[Math.Max(0, cmbAudio.SelectedIndex)];
            config.RdpMicrophone = chkMic.IsChecked == true;
            config.RdpScale = scales[Math.Max(0, cmbScale.SelectedIndex)];
            config.RdpNetwork = nets[Math.Max(0, cmbNet.SelectedIndex)];
            config.TerminalKey = txtKey.Text?.Trim() ?? "";
            config.Kiosk = chkKiosk.IsChecked == true;
            config.KioskForceFullScreen = chkKioskFull.IsChecked == true;
            if (pass.Length > 0) { config.KioskAdminPasswordHash = PasswordHasher.Hash(pass); }
            AppConfigManager.Save(config);
            window.Close();
        };

        void Show(string message)
        {
            lblError.Text = message;
            lblError.IsVisible = true;
        }

        static Control Cell(Control c, int row, int column, Thickness margin)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            c.Margin = margin;
            return c;
        }

        static TextBlock Section(string text) => new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 1,
            Foreground = B(Accent),
            Margin = new Thickness(0, 8, 0, 0)
        };

        static ComboBox Combo(string[] items, int selected) => new()
        {
            ItemsSource = items,
            SelectedIndex = Math.Max(0, selected),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        static Control Pair(string label, Control c) => new StackPanel { Spacing = 4, Children = { LoginWindow.Label(label), c } };

        var sections = new (string Name, string Icon, Control Body)[]
        {
            (L("MoaviServer"), AppIcons.Server, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        RowDefinitions = new RowDefinitions("Auto,Auto"),
                        Children =
                        {
                            LoginWindow.Label(L("MoaviServerAddress")),
                            Cell(LoginWindow.Label(L("MoaviServerPort")), 0, 1, new Thickness(8, 0, 0, 0)),
                            Cell(txtHost, 1, 0, new Thickness(0, 4, 0, 0)),
                            Cell(txtPort, 1, 1, new Thickness(8, 4, 0, 0))
                        }
                    },
                    chkHttps, chkCert,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { btnTest, lblTest } }
                }
            }),
            (L("SectionGeneral"), AppIcons.Tune, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Pair(L("DefaultProtocol"), cmbProtocol),
                    chkFull, chkAutoConnect,
                    Pair(L("Background"), UiHelper.RowWithButton(txtBackground, btnBackground)),
                    btnBackgroundReset,
                    Pair(L("ViewerPath"), UiHelper.RowWithButton(txtViewer, btnBrowse))
                }
            }),
            ("RDP", AppIcons.Rdp, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Pair(L("RdpColorDepth"), cmbDepth),
                    Pair(L("RdpAudio"), cmbAudio),
                    chkMic,
                    Pair(L("RdpScale"), cmbScale),
                    Pair(L("RdpNetwork"), cmbNet),
                    new TextBlock { Text = L("RdpPolicyNote"), Foreground = B(Muted), FontSize = 12, TextWrapping = TextWrapping.Wrap }
                }
            }),
            (L("SectionLabKiosk"), AppIcons.Lock, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Pair(L("MoaviTerminalKey"), txtKey),
                    chkKiosk, chkKioskFull, txtPass, txtPass2
                }
            }),
            (L("SectionAbout"), AppIcons.Info, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = $"{Product} v{DeskApi.AppVersion}", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = B(Text) },
                    new TextBlock
                    {
                        Text = string.Format(L("MoaviAbout"), DeskApi.AppVersion),
                        Foreground = B(Muted),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            })
        };

        var content = new ContentControl();
        var nav = new StackPanel { Spacing = 2 };
        var navButtons = new List<Button>();
        void Pick(int i)
        {
            content.Content = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = sections[i].Name, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = B(Text) },
                    sections[i].Body
                }
            };
            for (var k = 0; k < navButtons.Count; k++)
            {
                navButtons[k].Background = k == i ? B(Surface) : Brushes.Transparent;
            }
        }
        for (var i = 0; i < sections.Length; i++)
        {
            var index = i;
            var b = new Button
            {
                Content = UiHelper.WithText(sections[i].Icon, sections[i].Name, B(Text)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9)
            };
            b.Name = $"Section{index}";
            b.Click += (_, _) => Pick(index);
            navButtons.Add(b);
            nav.Children.Add(b);
        }
        Pick(0);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { btnCancel, new Border { Width = 120, Child = btnSave } }
        };

        var right = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var foot = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { lblError, buttons } };
        DockPanel.SetDock(foot, Dock.Bottom);
        DockPanel.SetDock(lblManaged, Dock.Top);
        right.Children.Add(foot);
        right.Children.Add(lblManaged);
        right.Children.Add(new ScrollViewer { Content = content });

        var left = new Border
        {
            Width = 200,
            Background = B(Surface2),
            BorderBrush = B(Edge),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 16),
            Child = nav
        };
        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        window.Content = root;

        return window;
    }
}
