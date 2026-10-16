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

/// <summary>Client settings: server, viewer, protocol, LAB terminal key and kiosk lock.</summary>
internal static class SettingsWindow
{
    private static readonly string[] Protocols = ["auto", "spice", "vnc"];

    public static Window Create(AppConfig config)
    {
        var window = new Window
        {
            Title = $"{Product} · {L("Settings")}",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B(Bg),
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Dark
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
            ItemsSource = new[] { "Auto (SPICE → VNC)", "SPICE", "VNC" },
            SelectedIndex = Math.Max(0, Array.IndexOf(Protocols, config.Protocol)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var chkFull = new CheckBox { Content = L("MoaviFullScreen"), IsChecked = config.ViewerFullScreen };

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

        window.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 8,
                Children =
                {
                    lblManaged,
                    Section(L("MoaviServer").ToUpperInvariant()),
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
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { btnTest, lblTest } },
                    Section(L("SectionViewer")),
                    LoginWindow.Label(L("ViewerPath")),
                    UiHelper.RowWithButton(txtViewer, btnBrowse),
                    cmbProtocol, chkFull,
                    Section("LAB"),
                    LoginWindow.Label(L("MoaviTerminalKey")), txtKey,
                    Section(L("TabKiosk").ToUpperInvariant()),
                    chkKiosk, chkKioskFull, txtPass, txtPass2,
                    lblError,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Thickness(0, 10, 0, 0),
                        Children = { btnCancel, new Border { Width = 120, Child = btnSave } }
                    },
                    new TextBlock
                    {
                        Text = string.Format(L("MoaviAbout"), DeskApi.AppVersion),
                        Foreground = B(Muted),
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 12, 0, 0)
                    }
                }
            }
        };

        return window;
    }
}
