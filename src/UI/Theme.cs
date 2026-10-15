using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MNIPTV.UI;

public static class MN
{
    private static SolidColorBrush B(byte r, byte g, byte b, byte a = 255)
    {
        var x = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        x.Freeze();
        return x;
    }

    public static readonly SolidColorBrush Black = B(0, 0, 0);
    public static readonly SolidColorBrush Surface = B(15, 17, 21);
    public static readonly SolidColorBrush Card = B(22, 25, 32);
    public static readonly SolidColorBrush CardHover = B(22, 25, 32, 150);
    public static readonly SolidColorBrush CardFocus = B(28, 38, 56);
    public static readonly SolidColorBrush Poster = B(26, 30, 39);
    public static readonly SolidColorBrush Blue = B(59, 139, 255);
    public static readonly SolidColorBrush BlueSoft = B(59, 139, 255, 115);
    public static readonly SolidColorBrush White = B(255, 255, 255);
    public static readonly SolidColorBrush Text2 = B(157, 164, 176);
    public static readonly SolidColorBrush Muted = B(90, 96, 110);
    public static readonly SolidColorBrush Divider = B(32, 36, 45);
    public static readonly SolidColorBrush Red = B(255, 59, 48);
    public static readonly SolidColorBrush Pill = B(37, 42, 54);
    public static readonly SolidColorBrush Shade = B(0, 0, 0, 191);
    public static readonly SolidColorBrush ListBg = B(6, 8, 12, 240);
    public static readonly SolidColorBrush Track = B(255, 255, 255, 64);
    public static readonly SolidColorBrush Toast = B(38, 43, 54);
    public static readonly SolidColorBrush Transparent = B(0, 0, 0, 0);
    /// Almost transparent: still receives mouse input in a layered window.
    public static readonly SolidColorBrush HitTestable = B(0, 0, 0, 1);

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static LinearGradientBrush Fade(bool toBottom)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        var dark = Color.FromArgb(225, 0, 0, 0);
        var clear = Color.FromArgb(0, 0, 0, 0);
        g.GradientStops.Add(new GradientStop(toBottom ? clear : dark, 0));
        g.GradientStops.Add(new GradientStop(toBottom ? dark : clear, 1));
        g.Freeze();
        return g;
    }

    public static TextBlock Text(string text, double size, Brush? color = null, bool bold = false, double? maxLines = 1)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            Foreground = color ?? White,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = maxLines == 1 ? TextWrapping.NoWrap : TextWrapping.Wrap,
            IsHitTestVisible = false,
        };
        if (maxLines is { } n && n > 1) t.MaxHeight = Math.Ceiling(size * 1.36 * n);
        return t;
    }

    public static TextBlock Icon(string glyph, double size, Brush? color = null) => new()
    {
        Text = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = color ?? White,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        IsHitTestVisible = false,
    };

    public static T At<T>(this T e, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : UIElement
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        if (rowSpan > 1) Grid.SetRowSpan(e, rowSpan);
        if (columnSpan > 1) Grid.SetColumnSpan(e, columnSpan);
        return e;
    }

    public static Grid Columns(params string[] defs)
    {
        var g = new Grid();
        foreach (var d in defs) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Len(d) });
        return g;
    }

    public static Grid Rows(params string[] defs)
    {
        var g = new Grid();
        foreach (var d in defs) g.RowDefinitions.Add(new RowDefinition { Height = Len(d) });
        return g;
    }

    private static GridLength Len(string d) => d switch
    {
        "*" => new GridLength(1, GridUnitType.Star),
        "auto" => GridLength.Auto,
        _ => new GridLength(double.Parse(d, System.Globalization.CultureInfo.InvariantCulture)),
    };

    public static StackPanel HStack(double spacing, params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children)
        {
            if (p.Children.Count > 0 && c is FrameworkElement fe && spacing > 0)
                fe.Margin = new Thickness(spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(c);
        }
        return p;
    }
}
