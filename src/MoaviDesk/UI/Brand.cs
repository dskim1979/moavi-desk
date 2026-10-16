/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using MoaviDesk.UI.Helpers;

namespace MoaviDesk.UI;

/// <summary>MOAVI Desk look: the console's dark navy palette and the M mark.</summary>
internal static class Brand
{
    public const string Product = "MOAVI Desk";
    public const string DefaultSupportUrl = "https://support.makussystems.co.kr/";

    public static readonly Color Bg = Color.Parse("#111B30");
    public static readonly Color Surface = Color.Parse("#1A2742");
    public static readonly Color Surface2 = Color.Parse("#101A2E");
    public static readonly Color Edge = Color.Parse("#2E3D5C");
    public static readonly Color Text = Color.Parse("#F1F5F9");
    public static readonly Color Muted = Color.Parse("#B8C4D6");
    public static readonly Color Accent = Color.Parse("#53B1B1");
    public static readonly Color OnAccent = Color.Parse("#0F182C");
    public static readonly Color Ok = Color.Parse("#4ADE80");
    public static readonly Color Warn = Color.Parse("#F0A35E");
    public static readonly Color Danger = Color.Parse("#F87171");

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
    public static Control Lockup(double markSize = 32, double fontSize = 20)
    {
        var word = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            Foreground = B(Text),
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
