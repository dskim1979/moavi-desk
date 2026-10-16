/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI.Helpers;
using static MoaviDesk.UI.Brand;

namespace MoaviDesk.UI;

/// <summary>
/// MOAVI Desk sign-in: username and password (+ OTP when the account asks for it). The server
/// is set in the settings, not here; the window only says whether it answers.
/// A PC with a terminal key (LAB) opens the class's desktop straight after sign-in;
/// any other PC goes on to the desktop list.
/// </summary>
internal static class LoginWindow
{
    private static readonly (string Code, string Label)[] Languages =
    [
        ("auto", "Auto"),
        ("ko", "한국어"),
        ("en", "English"),
    ];

    public static Window Create(AppConfig config)
    {
        var window = new Window
        {
            Title = $"{Product} · {L("Login")}",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = B(Bg),
            Icon = Brand.AppIcon(),
            RequestedThemeVariant = ThemeVariant.Dark
        };

        // whether the configured server answers (it is never shown here)
        var probing = false;
        var statusDot = new Ellipse { Width = 9, Height = 9, Fill = B(Muted), VerticalAlignment = VerticalAlignment.Center };
        var statusText = new TextBlock { Foreground = B(Muted), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var statusRow = new Border
        {
            Background = B(Surface2),
            BorderBrush = B(Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { statusDot, statusText } }
        };
        Grid.SetColumn(statusText, 1);
        statusText.Margin = new Thickness(8, 0, 0, 0);
        statusDot.VerticalAlignment = VerticalAlignment.Top;
        statusDot.Margin = new Thickness(0, 4, 0, 0);
        // a LAB PC: what the MOAVI Desk Agent (Services/Agent.cs) wrote to %ProgramData%\MOAVI Desk\terminal,
        // and the kiosk key it got since this window opened
        var enrollText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        void ShowAgent()
        {
            var t = MachineConfig.Refresh(config);
            var text = t.State switch
            {
                "rejected" => L("MoaviEnrollRejected"),
                "approved" when !string.IsNullOrEmpty(t.TerminalKey) => string.Format(L("MoaviEnrollDone"), t.Lab ?? "-"),
                "pending" or "approved" => t.Lab is { Length: > 0 } lab ? string.Format(L("MoaviEnrollPendingLab"), lab) : L("MoaviEnrollPending"),
                _ => null
            };
            if (t.Problem is { Length: > 0 } problem) { text = string.Format(L("MoaviEnrollProblem"), problem); }
            enrollText.IsVisible = text is not null;
            enrollText.Text = text;
            enrollText.Foreground = B(t.Problem is { Length: > 0 } || t.State == "rejected" ? Warn : Muted);
        }
        ShowAgent();

        var txtUser = UiHelper.TextBox(config.LastUser, L("Username"), AppIcons.Account);
        var txtPassword = UiHelper.TextBox(watermark: L("Password"), iconData: AppIcons.Lock);
        txtPassword.PasswordChar = '●';
        var txtOtp = UiHelper.TextBox(watermark: L("OtpWatermark"), iconData: AppIcons.Key);
        var otpLabel = Label(L("OtpLabel"));
        otpLabel.IsVisible = txtOtp.IsVisible = false;

        var lblError = new TextBlock
        {
            Foreground = B(Danger),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        if (AppConfigManager.UnreadableConfigBackup is { } backup)
        {
            lblError.Text = string.Format(L("ConfigUnreadable"), backup);
            lblError.IsVisible = true;
        }

        var btnLogin = PrimaryButton(L("Login"));
        btnLogin.IsDefault = true;

        var progress = new ProgressBar { IsIndeterminate = true, IsVisible = false, Foreground = B(Accent) };

        var (cmbLanguage, cmbLanguageWithIcon) = UiHelper.ComboBoxWithIcon(
            Languages.Select(l => l.Label).ToList(),
            AppIcons.Globe,
            Languages[Math.Max(0, Array.FindIndex(Languages, l => l.Code == (config.Language ?? "auto")))].Label);
        cmbLanguage.MinWidth = 120;
        cmbLanguage.Background = Brushes.Transparent;
        cmbLanguage.BorderBrush = Brushes.Transparent;

        var btnSettings = UiHelper.IconButton(AppIcons.Settings, "Settings", foregroundColor: Muted);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Children = { Lockup(34, 21), cmbLanguageWithIcon, btnSettings }
        };
        Grid.SetColumn(cmbLanguageWithIcon, 1);
        Grid.SetColumn(btnSettings, 2);

        var support = new Button
        {
            Content = UiHelper.WithText(AppIcons.Info, L("MoaviRemoteSupport"), B(Muted)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0)
        };
        support.Click += (_, _) => OpenUrl(DefaultSupportUrl);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                support,
                new TextBlock
                {
                    Text = $"v{DeskApi.AppVersion} · (주)매커스시스템즈",
                    Foreground = B(Muted),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        Grid.SetColumn(footer.Children[1], 1);

        var card = new Border
        {
            Background = B(Surface),
            BorderBrush = B(Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    header,
                    new TextBlock { Text = L("MoaviSignInTitle"), Foreground = B(Muted), Margin = new Thickness(0, 4, 0, 6) },
                    statusRow,
                    enrollText,
                    Label(L("Username")), txtUser,
                    Label(L("Password")), txtPassword,
                    otpLabel, txtOtp,
                    lblError,
                    progress,
                    btnLogin,
                }
            }
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children = { card, footer }
        };
        window.Content = root;

        // kiosk: full screen, closing needs the admin password
        var unlocked = false;
        if (config.Kiosk)
        {
            if (config.KioskForceFullScreen)
            {
                root.Width = 460;
                root.HorizontalAlignment = HorizontalAlignment.Center;
                root.VerticalAlignment = VerticalAlignment.Center;
                window.SizeToContent = SizeToContent.Manual;
                window.WindowState = WindowState.FullScreen;
            }
            if (!string.IsNullOrEmpty(config.KioskLoginBackground) && File.Exists(config.KioskLoginBackground))
            {
                try { window.Background = new ImageBrush(new Avalonia.Media.Imaging.Bitmap(config.KioskLoginBackground)) { Stretch = Stretch.UniformToFill }; }
                catch { /* keep the plain background */ }
            }
            window.Closing += async (_, e) =>
            {
                if (unlocked) { return; }
                e.Cancel = true;
                if (await KioskGuard.CheckAsync(window, config))
                {
                    unlocked = true;
                    window.Close();
                }
            };
        }

        cmbLanguage.SelectionChanged += (_, _) =>
        {
            var lang = Languages[cmbLanguage.SelectedIndex].Code;
            if (lang == config.Language) { return; }
            config.Language = lang;
            AppConfigManager.Save(config);
            ApplyLanguage(lang);
            unlocked = true;
            Create(config).Show();
            window.Close();
        };

        btnSettings.Click += async (_, _) =>
        {
            if (!await KioskGuard.CheckAsync(window, config)) { return; }
            await SettingsWindow.Create(config).ShowDialog(window);
            await Probe();
        };

        async Task Probe()
        {
            if (probing) { return; }
            probing = true;
            ShowAgent();
            try
            {
                if (string.IsNullOrWhiteSpace(config.ServerUrl))
                {
                    statusDot.Fill = B(Warn);
                    statusText.Text = L("MoaviServerNotSet");
                    return;
                }
                statusDot.Fill = B(Muted);
                statusText.Text = L("MoaviServerChecking");
                var (ok, _, _) = await DeskApi.ProbeAsync(config.ServerUrl, config.IgnoreCertificateErrors, timeoutSeconds: 4,
                                                          certSha256: config.ServerCertSha256);
                statusDot.Fill = B(ok ? Ok : Danger);
                statusText.Text = L(ok ? "MoaviServerOnline" : "MoaviServerOffline");
            }
            finally { probing = false; }
        }

        var probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        probeTimer.Tick += async (_, _) => await Probe();
        window.Closed += (_, _) => probeTimer.Stop();

        void Fail(string message)
        {
            lblError.Text = message;
            lblError.IsVisible = true;
        }

        void Busy(bool on)
        {
            progress.IsVisible = on;
            btnLogin.IsEnabled = txtUser.IsEnabled = txtPassword.IsEnabled = !on;
        }

        async Task SignIn()
        {
            lblError.IsVisible = false;
            var server = config.ServerUrl;
            var user = txtUser.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(server))
            {
                Fail(L("MoaviServerNotSet"));
                if (await KioskGuard.CheckAsync(window, config))
                {
                    await SettingsWindow.Create(config).ShowDialog(window);
                    await Probe();
                }
                return;
            }
            if (user.Length == 0 || string.IsNullOrEmpty(txtPassword.Text)) { Fail(L("MoaviErrInvalid")); return; }

            DeskApi api;
            try { api = new DeskApi(server, config.IgnoreCertificateErrors, config.ServerCertSha256); }
            catch (Exception ex) { Fail(string.Format(L("MoaviErrNetwork"), ex.Message)); return; }

            Busy(true);
            try
            {
                var r = await api.LoginAsync(user, txtPassword.Text!, txtOtp.IsVisible ? txtOtp.Text : null);
                switch (r)
                {
                    case LoginResult.SecondFactor:
                        otpLabel.IsVisible = txtOtp.IsVisible = true;
                        Fail(L("MoaviOtpNeeded"));
                        txtOtp.Focus();
                        api.Dispose();
                        return;
                    case LoginResult.Invalid: Fail(L("MoaviErrInvalid")); api.Dispose(); return;
                    case LoginResult.Locked: Fail(L("MoaviErrLocked")); api.Dispose(); return;
                    case LoginResult.Network:
                        Fail(L("MoaviServerOffline"));
                        statusDot.Fill = B(Danger);
                        statusText.Text = L("MoaviServerOffline");
                        api.Dispose();
                        return;
                    case LoginResult.Other: Fail(api.LastError); api.Dispose(); return;
                }

                config.LastUser = user;
                AppConfigManager.Save(config);

                var clientConfig = await api.ClientConfigAsync();
                if (clientConfig?.MinVersion is { Length: > 0 } min && IsOlder(DeskApi.AppVersion, min))
                {
                    Fail(string.Format(L("MoaviUpdateRequired"), min));
                    if (clientConfig.DownloadUrl is { Length: > 0 } url) { OpenUrl(url); }
                    await api.LogoutAsync();
                    api.Dispose();
                    return;
                }

                // a room's PC makes itself known (approved under 단말 관리)
                var terminalState = config.Kiosk || config.TerminalKey.Length > 0 ? await api.RegisterTerminalAsync() : null;

                if (config.TerminalKey.Length > 0)
                {
                    await KioskSessionAsync(api, config, Fail);
                    txtPassword.Text = txtOtp.Text = string.Empty;
                    otpLabel.IsVisible = txtOtp.IsVisible = false;
                    txtUser.Text = string.Empty;
                    return;
                }

                var main = MainWindow.Create(config, api, clientConfig,
                                                  terminalState == "pending" ? L("MoaviTerminalPending") : null);
                main.Show();
                unlocked = true;
                window.Close();
            }
            finally
            {
                Busy(false);
                // after a refusal, straight back to the password (the OTP when it was asked for)
                if (lblError.IsVisible && window.IsVisible)
                {
                    if (txtOtp.IsVisible && string.IsNullOrEmpty(txtOtp.Text)) { txtOtp.Focus(); }
                    else { txtPassword.Text = string.Empty; txtPassword.Focus(); }
                }
            }
        }

        btnLogin.Click += async (_, _) => await SignIn();
        window.Opened += async (_, _) =>
        {
            if (string.IsNullOrEmpty(txtUser.Text)) { txtUser.Focus(); }
            else { txtPassword.Focus(); }
            probeTimer.Start();
            await Probe();
        };

        return window;
    }

    /// <summary>
    /// LAB PC: a desktop of the class running in this terminal's room, full screen; when the
    /// viewer closes the session ends and the user is signed out for the next person.
    /// </summary>
    private static async Task KioskSessionAsync(DeskApi api, AppConfig config, Action<string> fail)
    {
        try
        {
            var (desktopId, desktopName, lab) = await api.KioskStartAsync(config.TerminalKey);
            var c = await ConnectBestAsync(api, desktopId, config.Protocol, ["spice", "vnc"]);
            var viewer = Viewer.FindViewer(config.ViewerPath);
            var (error, process) = await Viewer.OpenAsync(api, c, viewer, $"{desktopName} · {lab}", fullScreen: true);
            if (process is null)
            {
                fail(error);
                await api.EndSessionAsync(c.SessionId);
                return;
            }
            await process.WaitForExitAsync();
            await api.EndSessionAsync(c.SessionId);
        }
        catch (DeskApiException ex)
        {
            fail(ex.Code switch
            {
                "NO_SESSION" => L("MoaviKioskNoSession"),
                "FULL" => L("MoaviKioskFull"),
                "BAD_KEY" or "NO_LAB" => L("MoaviKioskBadKey"),
                _ => ex.Message
            });
        }
        finally
        {
            await api.LogoutAsync();
            api.Dispose();
        }
    }

    /// <summary>SPICE when the desktop offers it (USB, audio, multi-monitor), else VNC; or what the user picked.</summary>
    internal static Task<ConnectInfo> ConnectBestAsync(DeskApi api, string desktopId, string preferred, IReadOnlyCollection<string> offered)
    {
        var protocol = preferred is "spice" or "vnc" && offered.Contains(preferred)
                        ? preferred
                        : offered.Contains("spice") ? "spice" : "vnc";
        return api.ConnectAsync(desktopId, protocol);
    }

    internal static bool IsOlder(string current, string other)
        => Version.TryParse(current, out var a) && Version.TryParse(other, out var b) && a < b;

    internal static TextBlock Label(string text)
        => new() { Text = text, FontWeight = FontWeight.SemiBold, Foreground = B(Text), FontSize = 13 };
}
