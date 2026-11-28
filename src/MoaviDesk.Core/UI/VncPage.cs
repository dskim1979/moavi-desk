/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

#pragma warning disable IDE0048   // pixel and protocol arithmetic reads plainer without the extra brackets

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MoaviDesk.Services;
using MoaviDesk.UI.Helpers;
using static MoaviDesk.UI.Brand;

namespace MoaviDesk.UI;

/// <summary>
/// A desktop over VNC in MOAVI Desk's own page (Android, where there is no remote-viewer): the bar on top
/// (키보드, Ctrl+Alt+Del, 닫기) and the screen scaled to fit. A tap is a click, a drag moves with the button
/// held, a long press is a right click; 키보드 brings up the phone's keyboard.
/// </summary>
internal static class VncPage
{
    public static async Task<(string Error, RemoteSession? Session)> OpenAsync(DeskApi api, ConnectInfo c, string title)
    {
        RfbClient rfb;
        try { rfb = await RfbClient.ConnectAsync(api, c.WsPath!, c.Password ?? ""); }
        catch (Exception ex) { return (ex.Message, null); }

        var session = new PageSession();
        var page = new Page { Title = title, Width = 1280, Height = 800, Background = Brushes.Black, Icon = AppIcon() };

        var image = new Image { Stretch = Stretch.Uniform };
        WriteableBitmap? bitmap = null;
        var screen = new Border { Background = Brushes.Black, Child = image, ClipToBounds = true };

        // the phone's keyboard: a TextBox off to the side takes what is typed and sends it on
        var input = new TextBox { Width = 1, Height = 1, Opacity = 0, AcceptsReturn = true, IsTabStop = false };
        input.TextChanged += async (_, _) =>
        {
            var t = input.Text;
            if (string.IsNullOrEmpty(t)) { return; }
            input.Text = string.Empty;
            await rfb.TypeAsync(t);
        };
        input.KeyDown += async (_, e) =>
        {
            if (Keysym(e.Key) is { } sym)
            {
                e.Handled = true;
                await rfb.KeyAsync(sym, true);
                await rfb.KeyAsync(sym, false);
            }
        };

        Button BarButton(string icon, string text, Func<Task> act)
        {
            var b = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4),
                Content = UiHelper.WithText(icon, text, Brushes.White)
            };
            b.Click += async (_, _) => await act();
            return b;
        }
        var bar = new Border
        {
            Background = B(Color.Parse("#1F2937")),
            Padding = new Thickness(10, 4),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        Children =
                        {
                            BarButton(AppIcons.Edit, L("VncKeyboard"), () => { input.Focus(); return Task.CompletedTask; }),
                            BarButton(AppIcons.Key, "Ctrl+Alt+Del", rfb.CtrlAltDelAsync),
                            BarButton(AppIcons.Close, L("VncClose"), () => { page.Close(); return Task.CompletedTask; })
                        }
                    }, Dock.Right),
                    new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                }
            }
        };
        var root = new DockPanel();
        root.Children.Add(Docked(bar, Dock.Top));
        root.Children.Add(Docked(input, Dock.Bottom));
        root.Children.Add(screen);
        page.Content = root;

        // the screen: the frame buffer into the bitmap after each update
        void Paint()
        {
            lock (rfb.Sync)
            {
                if (rfb.Width == 0 || rfb.Height == 0) { return; }
                if (bitmap is null || bitmap.PixelSize.Width != rfb.Width || bitmap.PixelSize.Height != rfb.Height)
                {
                    bitmap = new WriteableBitmap(new PixelSize(rfb.Width, rfb.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    image.Source = bitmap;
                }
                using var fb = bitmap.Lock();
                Marshal.Copy(rfb.Pixels, 0, fb.Address, Math.Min(rfb.Pixels.Length, fb.RowBytes * rfb.Height));
            }
            image.InvalidateVisual();
        }
        var pending = 0;
        rfb.Updated += () => { if (Interlocked.Exchange(ref pending, 1) == 0) { Dispatcher.UIThread.Post(() => { pending = 0; Paint(); }); } };
        rfb.Closed += error => Dispatcher.UIThread.Post(() =>
        {
            if (!page.IsVisible) { return; }
            page.Close();
            if (!string.IsNullOrEmpty(error)) { session.Error = error; }
        });

        // pointer: the screen's place for a point on the image
        (int X, int Y) At(Point p)
        {
            var b = image.Bounds;
            if (rfb.Width == 0 || b.Width <= 0 || b.Height <= 0) { return (0, 0); }
            var s = Math.Min(b.Width / rfb.Width, b.Height / rfb.Height);
            var ox = (b.Width - rfb.Width * s) / 2;
            var oy = (b.Height - rfb.Height * s) / 2;
            return ((int)((p.X - ox) / s), (int)((p.Y - oy) / s));
        }
        var buttons = 0;
        DateTime pressedAt = default;
        image.PointerPressed += async (_, e) =>
        {
            var pt = e.GetCurrentPoint(image);
            var (x, y) = At(pt.Position);
            buttons = pt.Properties.IsRightButtonPressed ? 4 : pt.Properties.IsMiddleButtonPressed ? 2 : 1;
            pressedAt = DateTime.UtcNow;
            if (e.Pointer.Type == PointerType.Touch) { await rfb.PointerAsync(x, y, 0); return; }   // a touch clicks when it lifts
            await rfb.PointerAsync(x, y, buttons);
        };
        image.PointerMoved += async (_, e) =>
        {
            var (x, y) = At(e.GetPosition(image));
            var down = e.Pointer.Type == PointerType.Touch ? (buttons != 0 ? 1 : 0) : buttons;
            await rfb.PointerAsync(x, y, down);
        };
        image.PointerReleased += async (_, e) =>
        {
            var (x, y) = At(e.GetPosition(image));
            if (e.Pointer.Type == PointerType.Touch && buttons != 0)
            {
                // a long press (over 0.6 s) is a right click
                var b = DateTime.UtcNow - pressedAt > TimeSpan.FromMilliseconds(600) ? 4 : 1;
                await rfb.PointerAsync(x, y, b);
            }
            buttons = 0;
            await rfb.PointerAsync(x, y, 0);
        };
        image.PointerWheelChanged += async (_, e) =>
        {
            var (x, y) = At(e.GetPosition(image));
            var b = e.Delta.Y > 0 ? 8 : 16;
            await rfb.PointerAsync(x, y, b);
            await rfb.PointerAsync(x, y, 0);
        };

        page.Closed += async (_, _) =>
        {
            await rfb.DisposeAsync();
            session.Ended();
        };
        session.Page = page;
        page.Show();
        rfb.Start();
        Paint();
        return (string.Empty, session);
    }

    private static uint? Keysym(Key key) => key switch
    {
        Key.Back => 0xff08,
        Key.Tab => 0xff09,
        Key.Enter => 0xff0d,
        Key.Escape => 0xff1b,
        Key.Delete => 0xffff,
        Key.Home => 0xff50,
        Key.Left => 0xff51,
        Key.Up => 0xff52,
        Key.Right => 0xff53,
        Key.Down => 0xff54,
        Key.PageUp => 0xff55,
        Key.PageDown => 0xff56,
        Key.End => 0xff57,
        Key.Insert => 0xff63,
        >= Key.F1 and <= Key.F12 => 0xffbeU + (uint)(key - Key.F1),
        _ => null
    };

    private static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    /// <summary>The viewer page as a session: it ends when the page closes.</summary>
    private sealed class PageSession : RemoteSession
    {
        private bool _ended;
        public Page? Page { get; set; }
        public string? Error { get; set; }
        public override bool HasExited => _ended;
        public override void End() => Page?.Close();
        public void Ended()
        {
            if (_ended) { return; }
            _ended = true;
            RaiseExited();
        }
    }
}
