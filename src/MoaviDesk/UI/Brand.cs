/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.UI.Helpers;

namespace MoaviDesk.UI;

/// <summary>MOAVI Desk look: light surfaces over a picture, a deep-teal accent, the M mark.</summary>
internal static class Brand
{
    public const string Product = "MOAVI Desk";
    public const string DefaultSupportUrl = "https://support.makussystems.co.kr/";

    public static readonly Color Bg = Color.Parse("#F3F6FA");
    public static readonly Color Surface = Color.Parse("#FFFFFF");
    public static readonly Color Surface2 = Color.Parse("#F5F8FB");
    public static readonly Color Edge = Color.Parse("#DCE3EC");
    public static readonly Color Text = Color.Parse("#0F172A");
    public static readonly Color Muted = Color.Parse("#5B6B82");
    public static readonly Color Accent = Color.Parse("#12807C");
    public static readonly Color OnAccent = Color.Parse("#FFFFFF");
    public static readonly Color Ok = Color.Parse("#16A34A");
    public static readonly Color Warn = Color.Parse("#D97706");
    public static readonly Color Danger = Color.Parse("#DC2626");

    /// <summary>The built-in backgrounds, in the order Settings shows them: key and short Korean / English name.
    /// The photos are Unsplash licence photos (credits in 3rd-party-licenses.md); "moavi" is MOAVI's own drawing.</summary>
    public static readonly (string Key, string Ko, string En)[] Backgrounds =
    [
        ("1", "노을 산 호수", "Alpine lake at dusk"),
        ("2", "푸른 호수", "Blue lake"),
        ("3", "숲 호수", "Forest lake"),
        ("4", "고요한 호수", "Still lake"),
        ("5", "구름 하늘", "Cloudy sky"),
        ("6", "해 뜨는 호수", "Lake at sunrise"),
        ("moavi", "MOAVI", "MOAVI"),
    ];

    public const string DefaultBackground = "1";

    /// <summary>The key of a built-in choice, or null when the setting names a file of the user's own.</summary>
    public static string? BuiltinKey(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) { return DefaultBackground; }
        return setting.StartsWith("builtin:", StringComparison.Ordinal) ? setting["builtin:".Length..] : null;
    }

    public static Avalonia.Media.Imaging.Bitmap BuiltinBitmap(string key, bool thumb = false)
        => new(Avalonia.Platform.AssetLoader.Open(new Uri($"avares://moavi-desk/Backgrounds/{(thumb ? "thumbs/" : "")}{key}.jpg")));

    /// <summary>The picture behind the windows (설정 › 일반 › 배경): a built-in one or the user's own file.</summary>
    public static IBrush Backdrop(Config.AppConfig config)
    {
        try
        {
            var key = BuiltinKey(config.WorkspaceBackground);
            var bmp = key is null && File.Exists(config.WorkspaceBackground)
                ? new Avalonia.Media.Imaging.Bitmap(config.WorkspaceBackground)
                : BuiltinBitmap(Backgrounds.Any(b => b.Key == key) ? key! : DefaultBackground);
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch { return B(Bg); }
    }

    public static IBrush B(Color c) => new SolidColorBrush(c);

    public static WindowIcon AppIcon()
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://moavi-desk/icon.ico"));
        return new WindowIcon(stream);
    }

    /// <summary>The M mark: navy→blue tile, white M, teal dots (same as the console's favicon).</summary>
    public static Control Mark(double size)
    {
        var canvas = new Canvas { Width = 64, Height = 64 };
        canvas.Children.Add(new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(15),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0A2A66"), 0), new GradientStop(Color.Parse("#1668E3"), 1) }
            }
        });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M16,46 V20 L32,37 L48,20 V46"),
            Stroke = Brushes.White,
            StrokeThickness = 6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        });
        foreach (var (x, y, r, c) in new[] { (16d, 20d, 3.4, "#2FD4C4"), (48, 20, 3.4, "#2FD4C4"), (32, 37, 7, "#FFFFFF"), (32, 37, 5.2, "#2FD4C4") })
        {
            var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(Color.Parse(c)) };
            Canvas.SetLeft(dot, x - r);
            Canvas.SetTop(dot, y - r);
            canvas.Children.Add(dot);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>Mark + "MOAVI Desk" wordmark.</summary>
    public static Control Lockup(double markSize = 32, double fontSize = 20, Color? ink = null)
    {
        var word = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            Foreground = B(ink ?? Text),
            Inlines = []
        };
        word.Inlines.Add(new Avalonia.Controls.Documents.Run("MOAVI "));
        word.Inlines.Add(new Avalonia.Controls.Documents.Run("Desk") { Foreground = B(Accent) });
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { Mark(markSize), word }
        };
    }

    public static Button PrimaryButton(string text)
        => new()
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12, 9),
            FontWeight = FontWeight.SemiBold,
            Background = B(Accent),
            Foreground = B(OnAccent),
            CornerRadius = new CornerRadius(8)
        };

    public static Button GhostButton(string text, string? icon = null)
        => new()
        {
            Content = icon is null ? text : UiHelper.WithText(icon, text, B(Muted)),
            Padding = new Thickness(10, 6),
            Background = Brushes.Transparent,
            Foreground = B(Muted),
            BorderBrush = B(Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }
}
