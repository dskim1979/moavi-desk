/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Android.Content.PM;
using Android.Runtime;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using MoaviDesk.Config;
using MoaviDesk.Services;
using MoaviDesk.UI;
using Application = Avalonia.Application;
using Page = MoaviDesk.UI.Page;

namespace MoaviDesk;

/// <summary>The app's one view: the pages stack (PageStack), sign-in first.</summary>
public sealed class App : Application
{
    public override void Initialize() => MoaviApp.ApplyTheme(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            var config = AppConfigManager.Load();
            ApplyLanguage(config.Language);
            Platform.Rdp = AndroidRdp.Available() ? new AndroidRdp(config) : null;
            Platform.RemoteViewer = false;
            Platform.Vault = new KeystoreVault();
            Platform.InAppVnc = true;
            Platform.OpenSpice = SpiceHandoff.OpenAsync;
            Platform.OpenUrl = AndroidHost.OpenUrl;

            var stack = new PageStack();
            stack.Empty += (_, _) => AndroidHost.Main?.Finish();
            Page.Stack = stack;
            single.MainView = stack;
            LoginWindow.Create(config).Show();
#if MOAVI_TEST
            // adb shell am start -n com.makussystems.moavidesk/com.makussystems.moavidesk.MainActivity --es moavi.test.rdp "host:port|user|password"
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(3000);
                if (AndroidHost.Main?.Intent?.GetStringExtra("moavi.test.rdp") is { Length: > 0 } spec && Platform.Rdp is { } rdp)
                {
                    var (file, password) = TestRdp.Of(spec);
                    var (error, _) = await rdp.OpenAsync(file, "RDP check", false, password, new DevicePolicy(true, true, true));
                    Android.Util.Log.Info("MoaviDesk", $"RDP check open: {(error.Length == 0 ? "ok" : error)}");
                }
            });
#endif
        }
        base.OnFrameworkInitializationCompleted();
    }
}

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        RegisterActivityLifecycleCallbacks(new AndroidHost());
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithMoaviLook();
}

[Activity(Name = "com.makussystems.moavidesk.MainActivity", Label = "MOAVI Desk", Theme = "@style/Theme.AppCompat.Light.NoActionBar", Icon = "@drawable/icon", MainLauncher = true,
          WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout
                                 | ConfigChanges.SmallestScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
