using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MNPlayer.UI;

public static class MN
{
    private static SolidColorBrush B(byte r, byte g, byte b, byte a = 255)
    {
        var x = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        x.Freeze();
        return x;
    }

    public static readonly SolidColorBrush Black = B(0, 0, 0);
    public static readonly SolidColorBrush Blue = B(59, 139, 255);
    public static readonly SolidColorBrush White = B(255, 255, 255);
    public static readonly SolidColorBrush Text2 = B(172, 178, 190);
    public static readonly SolidColorBrush Hud = B(30, 32, 38, 232);
    public static readonly SolidColorBrush HudHover = B(255, 255, 255, 38);
    public static readonly SolidColorBrush HudBorder = B(255, 255, 255, 28);
    public static readonly SolidColorBrush Menu = B(26, 29, 36, 248);
    public static readonly SolidColorBrush Card = B(28, 31, 39);
    public static readonly SolidColorBrush Field = B(17, 19, 24);
    public static readonly SolidColorBrush Chip = B(44, 49, 62);
    public static readonly SolidColorBrush Dim = B(0, 0, 0, 150);
    public static readonly SolidColorBrush Toast = B(20, 22, 28, 215);
    public static readonly SolidColorBrush Track = B(255, 255, 255, 64);
    public static readonly SolidColorBrush Surface = B(15, 17, 21);
    /// Almost transparent: still receives mouse input in a layered window.
    public static readonly SolidColorBrush HitTestable = B(0, 0, 0, 1);

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static TextBlock Text(string text, double size, Brush? color = null, bool bold = false, bool wrap = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            Foreground = color ?? White,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            IsHitTestVisible = false,
        };
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
}
