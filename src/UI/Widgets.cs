using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNPlayer.UI;

/// Icon-only button used in the control bar.
public sealed class IconButton : Border
{
    private readonly TextBlock _glyph;
    private bool _hover, _enabled = true;
    public event Action? Click;

    public IconButton(string glyph, string tip, double size = 16, Brush? color = null)
    {
        CornerRadius = new CornerRadius(7);
        Background = MN.HitTestable;
        Cursor = Cursors.Hand;
        Focusable = false;
        ToolTip = tip;
        _glyph = MN.Icon(glyph, size, color);
        Child = _glyph;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_enabled && IsMouseOver) Click?.Invoke();
        };
    }

    public string Glyph { get => _glyph.Text; set => _glyph.Text = value; }
    public Brush GlyphBrush { set => _glyph.Foreground = value; }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Opacity = value ? 1 : 0.35; Cursor = value ? Cursors.Hand : Cursors.Arrow; Paint(); }
    }

    private void Paint() => Background = _hover && _enabled ? MN.HudHover : MN.HitTestable;
}

/// Seek / volume slider with the accent colour.
public sealed class Slider2 : FrameworkElement
{
    private double _value, _max = 1;
    private bool _dragging, _hover;

    public event Action<double>? Dragging;
    public event Action<double>? Committed;
    public bool IsDragging => _dragging;

    public Slider2()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        MouseEnter += (_, _) => { _hover = true; InvalidateVisual(); };
        MouseLeave += (_, _) => { _hover = false; InvalidateVisual(); };
    }

    public bool Enabled { get; set; } = true;
    public double Value { get => _value; set { if (!_dragging) { _value = value; InvalidateVisual(); } } }
    public double Maximum { get => _max; set { _max = Math.Max(0.0001, value); InvalidateVisual(); } }

    private double At(MouseEventArgs e) => Math.Max(0, Math.Min(1, e.GetPosition(this).X / Math.Max(1, ActualWidth))) * _max;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!Enabled) return;
        _dragging = true;
        CaptureMouse();
        _value = At(e);
        Dragging?.Invoke(_value);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _value = At(e);
        Dragging?.Invoke(_value);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        _value = At(e);
        Committed?.Invoke(_value);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(MN.HitTestable, null, new Rect(0, 0, w, h));
        var y = h / 2;
        var t = _hover || _dragging ? 5 : 4;
        var f = Math.Max(0, Math.Min(1, _value / _max)) * w;
        dc.DrawRoundedRectangle(MN.Track, null, new Rect(0, y - t / 2.0, w, t), t / 2.0, t / 2.0);
        if (Enabled) dc.DrawRoundedRectangle(MN.Blue, null, new Rect(0, y - t / 2.0, f, t), t / 2.0, t / 2.0);
        if (Enabled && (_hover || _dragging)) dc.DrawEllipse(MN.White, null, new Point(Math.Max(6, Math.Min(w - 6, f)), y), 6, 6);
    }
}

public sealed class MenuEntry
{
    public string Text = "";
    public bool Checked;
    public bool Separator;
    public bool Disabled;
    public Action? Click;
    public List<MenuEntry>? Sub;

    public static MenuEntry Line() => new() { Separator = true };
}

/// Dark popup menu drawn inside the overlay (no native popup windows, so it sits above the video).
public sealed class MenuPanel : Border
{
    private readonly StackPanel _list = new();
    public event Action? Closed;

    public MenuPanel()
    {
        Background = MN.Menu;
        BorderBrush = MN.HudBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(6);
        MinWidth = 240;
        Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Focusable = false };
    }

    public void Show(List<MenuEntry> items) => Build(items, null);

    private void Build(List<MenuEntry> items, List<MenuEntry>? parent)
    {
        _list.Children.Clear();
        if (parent != null)
        {
            var back = Row("Geri", false, true);
            back.MouseLeftButtonUp += (_, e) => { e.Handled = true; Build(parent, null); };
            _list.Children.Add(back);
            _list.Children.Add(Divider());
        }
        foreach (var item in items)
        {
            if (item.Separator) { _list.Children.Add(Divider()); continue; }
            var row = Row(item.Text, item.Checked, false, item.Sub != null, item.Disabled);
            if (!item.Disabled)
            {
                var captured = item;
                row.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (captured.Sub != null) { Build(captured.Sub, items); return; }
                    Close();
                    captured.Click?.Invoke();
                };
            }
            _list.Children.Add(row);
        }
    }

    private static Border Divider() => new() { Height = 1, Background = MN.HudBorder, Margin = new Thickness(6, 4, 6, 4) };

    private static Border Row(string text, bool isChecked, bool isBack, bool hasSub = false, bool disabled = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        var mark = MN.Icon(isBack ? "" : (isChecked ? "" : ""), 12, MN.Blue);
        Grid.SetColumn(mark, 0);
        grid.Children.Add(mark);
        var label = MN.Text(text, 13, disabled ? MN.Text2 : MN.White);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (hasSub)
        {
            var arrow = MN.Icon("", 11, MN.Text2);
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }
        var row = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = MN.HitTestable,
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = disabled ? Cursors.Arrow : Cursors.Hand,
            Child = grid,
        };
        if (!disabled)
        {
            row.MouseEnter += (_, _) => row.Background = MN.HudHover;
            row.MouseLeave += (_, _) => row.Background = MN.HitTestable;
        }
        return row;
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
    }
}

public sealed class TextButton : Border
{
    public event Action? Click;

    public TextButton(string text, bool primary)
    {
        Height = 34;
        MinWidth = 84;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(18, 0, 18, 0);
        Margin = new Thickness(8, 0, 0, 0);
        Cursor = Cursors.Hand;
        Focusable = false;
        var normal = primary ? MN.Blue : MN.Chip;
        Background = normal;
        var label = MN.Text(text, 13, MN.White, bold: true);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        Child = label;
        MouseEnter += (_, _) => Opacity = 0.85;
        MouseLeave += (_, _) => Opacity = 1;
        MouseLeftButtonUp += (_, e) => { e.Handled = true; if (IsMouseOver) Click?.Invoke(); };
    }
}

/// Modal card drawn inside the overlay: message or single-line input.
public sealed class CardDialog : Grid
{
    private readonly TextBox? _input;
    private readonly List<(string text, bool primary, Action<string>? action)> _buttons;
    public event Action? Closed;

    public CardDialog(string title, string? body, string? placeholder, params (string text, bool primary, Action<string>? action)[] buttons)
    {
        _buttons = new List<(string, bool, Action<string>?)>(buttons);
        Background = MN.Dim;
        Focusable = true;

        var stack = new StackPanel();
        stack.Children.Add(MN.Text(title, 17, MN.White, bold: true));
        if (!string.IsNullOrEmpty(body))
        {
            var text = MN.Text(body!, 13, MN.Text2, wrap: true);
            text.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(text);
        }
        if (placeholder != null)
        {
            _input = new TextBox
            {
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 14,
                FontFamily = MN.Font,
                Background = MN.Field,
                Foreground = MN.White,
                CaretBrush = MN.White,
                SelectionBrush = MN.Blue,
                BorderBrush = MN.HudBorder,
                BorderThickness = new Thickness(1),
                ToolTip = placeholder,
            };
            stack.Children.Add(_input);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        foreach (var (text, primary, action) in _buttons)
        {
            var b = new TextButton(text, primary);
            var captured = action;
            b.Click += () => Finish(captured);
            row.Children.Add(b);
        }
        stack.Children.Add(row);

        var card = new Border
        {
            Background = MN.Card,
            BorderBrush = MN.HudBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            Width = 460,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };
        Children.Add(card);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Finish(null); }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var primary = _buttons.FindIndex(b => b.primary);
                Finish(primary >= 0 ? _buttons[primary].action : null);
            }
        };
        MouseLeftButtonDown += (_, e) => e.Handled = true;
        Loaded += (_, _) =>
        {
            if (_input != null) { _input.Focus(); Keyboard.Focus(_input); }
            else { Focus(); Keyboard.Focus(this); }
        };
    }

    public string Input => _input?.Text ?? "";

    private void Finish(Action<string>? action)
    {
        var value = Input;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke();
        action?.Invoke(value);
    }
}
