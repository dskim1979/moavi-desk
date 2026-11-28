/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using Avalonia.Platform.Storage;

namespace MoaviDesk.UI;

/// <summary>Closing a page; Cancel keeps it open (as Window.Closing).</summary>
internal sealed class PageClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}

/// <summary>
/// One screen of MOAVI Desk (sign-in, desktops, settings, a question). On Windows and macOS it is a
/// window of its own; on Android, where an app has one view, pages stack in that view (PageStack) and
/// a dialog shows over the page under it. The screens set the same things they set on a window.
/// </summary>
internal sealed class Page
{
    /// <summary>Set by an app with one view (Android) before the first page; null = windows.</summary>
    public static PageStack? Stack { get; set; }

    /// <summary>The window behind this page (Windows, macOS); null in one view.</summary>
    public Window? Window { get; }

    private string _title = "";
    private object? _content;
    private IBrush? _background;
    private double _width = double.NaN;

    public Page()
    {
        if (Stack is not null) { return; }
        Window = new Window();
        Window.Opened += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
        Window.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        Window.Closing += (_, e) =>
        {
            var a = new PageClosingEventArgs();
            Closing?.Invoke(this, a);
            e.Cancel = a.Cancel;
        };
    }

    /// <summary>The back button (Android) first asks the page: true = it closed something of its own.</summary>
    public Func<bool>? OnBack { get; set; }

    public event EventHandler? Opened;
    public event EventHandler<PageClosingEventArgs>? Closing;
    public event EventHandler? Closed;

    public string Title { get => _title; set { _title = value; if (Window is not null) { Window.Title = value; } } }
    public object? Content { get => _content; set { _content = value; if (Window is not null) { Window.Content = value; } else { View.Content = value; } } }
    public IBrush? Background { get => _background; set { _background = value; if (Window is not null) { Window.Background = value; } else { View.Background = value; } } }
    public double Width { get => _width; set { _width = value; if (Window is not null) { Window.Width = value; } } }
    public double Height { set { if (Window is not null) { Window.Height = value; } } }
    public double MinWidth { set { if (Window is not null) { Window.MinWidth = value; } } }
    public double MinHeight { set { if (Window is not null) { Window.MinHeight = value; } } }
    public bool CanResize { set { if (Window is not null) { Window.CanResize = value; } } }
    public SizeToContent SizeToContent { set { if (Window is not null) { Window.SizeToContent = value; } } }
    public WindowIcon? Icon { set { if (Window is not null) { Window.Icon = value; } } }
    public ThemeVariant? RequestedThemeVariant { set { if (Window is not null) { Window.RequestedThemeVariant = value; } } }
    public WindowStartupLocation WindowStartupLocation { set { if (Window is not null) { Window.WindowStartupLocation = value; } } }

    /// <summary>Full screen is a window's; a page in one view already fills the screen.</summary>
    public WindowState WindowState
    {
        get => Window?.WindowState ?? WindowState.FullScreen;
        set { if (Window is not null) { Window.WindowState = value; } }
    }

    public bool IsVisible => Window?.IsVisible ?? View.IsAttached;

    /// <summary>What this page is in one view: a full page or a dialog's card.</summary>
    internal PageView View => _view ??= new PageView(this);
    private PageView? _view;

    private TopLevel? Top => Window ?? TopLevel.GetTopLevel(Stack);
    public IClipboard? Clipboard => Top?.Clipboard;
    public IStorageProvider StorageProvider => Top!.StorageProvider;

    public void Show()
    {
        if (Window is not null) { Window.Show(); return; }
        Stack!.Push(this, dialog: false);
    }

    public async Task ShowDialog(Page owner)
    {
        if (Window is not null && owner.Window is not null) { await Window.ShowDialog(owner.Window); return; }
        var done = new TaskCompletionSource();
        Closed += (_, _) => done.TrySetResult();
        Stack!.Push(this, dialog: true);
        await done.Task;
    }

    public void Close()
    {
        if (Window is not null) { Window.Close(); return; }
        if (!View.IsAttached) { return; }
        var a = new PageClosingEventArgs();
        Closing?.Invoke(this, a);
        if (a.Cancel) { return; }
        Stack!.Remove(this);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);

    /// <summary>The page in one view: its background over the whole screen, or a dialog's card.</summary>
    internal sealed class PageView(Page page) : Border
    {
        public object? Content { get => Child; set => Child = value as Control; }
        public bool IsAttached { get; set; }
        public Page Page { get; } = page;
    }
}

/// <summary>
/// The app's one view (Android): the pages on top of each other. A new full page takes the place
/// of the ones under it once they close (the screens close the page they leave); a dialog shows over
/// the page that asked, the back button closes the top page.
/// </summary>
internal sealed class PageStack : Grid
{
    private readonly List<Page> _pages = [];

    /// <summary>No page left (the user closed the last one): the app ends.</summary>
    public event EventHandler? Empty;

    public Page? Top => _pages.Count > 0 ? _pages[^1] : null;

    public PageStack()
    {
        Background = Brushes.White;
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is { } top)
            {
                top.BackRequested += (_, e) =>
                {
                    if (Top is not { } p) { return; }
                    e.Handled = true;
                    if (p.OnBack?.Invoke() != true) { p.Close(); }
                };
            }
        };
    }

    internal void Push(Page page, bool dialog)
    {
        var view = page.View;
        Control shown = view;
        if (dialog)
        {
            view.Background ??= Brushes.White;
            view.CornerRadius = new CornerRadius(8);
            view.Margin = new Thickness(16);
            view.MaxWidth = double.IsNaN(page.Width) ? 480 : page.Width;
            view.HorizontalAlignment = HorizontalAlignment.Center;
            view.VerticalAlignment = VerticalAlignment.Center;
            shown = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)),
                Child = new ScrollViewer { Content = view, VerticalAlignment = VerticalAlignment.Center }
            };
        }
        shown.Tag = page;
        view.IsAttached = true;
        _pages.Add(page);
        Children.Add(shown);
        Dispatcher.UIThread.Post(page.RaiseOpened, DispatcherPriority.Loaded);
    }

    internal void Remove(Page page)
    {
        page.View.IsAttached = false;
        _pages.Remove(page);
        var shown = Children.FirstOrDefault(c => ReferenceEquals(c.Tag, page));
        if (shown is not null) { Children.Remove(shown); }
        if (shown is Border { Child: ScrollViewer sv }) { sv.Content = null; }
        if (_pages.Count == 0) { Empty?.Invoke(this, EventArgs.Empty); }
    }
}
