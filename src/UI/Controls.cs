using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MNIPTV.UI;

/// Rounded button used across the app. Mouse only: keyboard focus is handled by each screen.
public sealed class Pill : Border
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock? _label;
    private readonly TextBlock? _icon;
    private bool _prominent, _hover, _focused;

    public event Action? Click;

    public Pill(string? text, string? glyph = null, bool prominent = false)
    {
        Height = 38;
        CornerRadius = new CornerRadius(19);
        Padding = new Thickness(text == null ? 13 : 20, 0, text == null ? 13 : 20, 0);
        Cursor = Cursors.Hand;
        Focusable = false;
        Child = _row;
        if (glyph != null)
        {
            _icon = MN.Icon(glyph, 15);
            _row.Children.Add(_icon);
        }
        if (text != null)
        {
            _label = MN.Text(text, 14, MN.White, bold: true);
            _label.VerticalAlignment = VerticalAlignment.Center;
            if (glyph != null) _label.Margin = new Thickness(8, 0, 0, 0);
            _row.Children.Add(_label);
        }
        _prominent = prominent;
        Paint();
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        MouseLeftButtonDown += (_, e) => { e.Handled = true; Opacity = 0.75; };
        MouseLeftButtonUp += (_, e) =>
        {
            Opacity = 1;
            e.Handled = true;
            if (IsMouseOver) Click?.Invoke();
        };
    }

    public string Text { set { if (_label != null) _label.Text = value; } }
    public string Glyph { set { if (_icon != null) _icon.Text = value; } }
    public bool Prominent { get => _prominent; set { _prominent = value; Paint(); } }
    /// Keyboard focus ring drawn by the owning screen.
    public bool Focused { get => _focused; set { _focused = value; Paint(); } }

    private void Paint()
    {
        Background = _prominent || _focused ? MN.Blue : (_hover ? MN.CardFocus : MN.Pill);
        BorderBrush = _focused && !_prominent ? MN.White : null;
        BorderThickness = new Thickness(_focused && _prominent ? 0 : (_focused ? 2 : 0));
    }
}

public sealed class ProgressLine : Grid
{
    private readonly Border _fill = new() { Background = MN.Blue, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    private double _fraction;

    public ProgressLine()
    {
        Height = 4;
        IsHitTestVisible = false;
        Children.Add(new Border { Background = MN.Track, CornerRadius = new CornerRadius(2) });
        Children.Add(_fill);
        SizeChanged += (_, _) => Apply();
    }

    public double Fraction { get => _fraction; set { _fraction = Math.Max(0, Math.Min(1, double.IsFinite(value) ? value : 0)); Apply(); } }

    private void Apply() => _fill.Width = ActualWidth * _fraction;
}

public static class Badge
{
    public static Border Make(string text) => new()
    {
        Background = MN.Shade,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(6, 1, 6, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Child = MN.Text(text, 11, MN.White, bold: true),
    };
}

/// Indeterminate ring.
public sealed class Spinner : Grid
{
    public Spinner(double size = 40)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        var arc = new Path
        {
            Stroke = MN.Blue,
            StrokeThickness = size / 10,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse(FormattableString.Invariant($"M {size / 2},{size / 20} A {size * 0.45},{size * 0.45} 0 1 1 {size / 20},{size / 2}")),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var rotate = new RotateTransform();
        arc.RenderTransform = rotate;
        Children.Add(new Ellipse { Stroke = MN.Track, StrokeThickness = size / 10 });
        Children.Add(arc);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            else rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }
}

/// Horizontal slider drawn by hand (seek bar and volume).
public sealed class Bar : FrameworkElement
{
    private double _value, _max = 1;
    private bool _dragging, _hover;

    public event Action<double>? Dragging;
    public event Action<double>? Committed;
    public bool IsDragging => _dragging;

    public Bar()
    {
        Height = 18;
        Cursor = Cursors.Hand;
        Focusable = false;
        MouseEnter += (_, _) => { _hover = true; InvalidateVisual(); };
        MouseLeave += (_, _) => { _hover = false; InvalidateVisual(); };
    }

    public double Value { get => _value; set { if (!_dragging) { _value = value; InvalidateVisual(); } } }
    public double Maximum { get => _max; set { _max = Math.Max(0.0001, value); InvalidateVisual(); } }

    private double At(MouseEventArgs e) => Math.Max(0, Math.Min(1, e.GetPosition(this).X / Math.Max(1, ActualWidth))) * _max;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        _value = At(e);
        Dragging?.Invoke(_value);
        InvalidateVisual();
        e.Handled = true;
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
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var y = h / 2;
        var t = _hover || _dragging ? 6 : 4;
        var f = Math.Max(0, Math.Min(1, _value / _max)) * w;
        dc.DrawRoundedRectangle(MN.Track, null, new Rect(0, y - t / 2.0, w, t), t / 2.0, t / 2.0);
        dc.DrawRoundedRectangle(MN.Blue, null, new Rect(0, y - t / 2.0, f, t), t / 2.0, t / 2.0);
        if (_hover || _dragging) dc.DrawEllipse(MN.White, null, new Point(f, y), 7, 7);
    }
}

/// Wide tile for live channels.
public sealed class LiveTile : Border
{
    private readonly RemoteImage _logo = new(fill: false) { Width = 52, Height = 52 };
    private readonly TextBlock _number = MN.Text("", 11, MN.Muted);
    private readonly TextBlock _name = MN.Text("", 14, MN.White, maxLines: 2);
    private readonly TextBlock _star = MN.Icon("", 12, MN.Blue);
    private bool _hover, _focused;

    public LiveTile()
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(2.5);
        Padding = new Thickness(12, 0, 12, 0);
        Cursor = Cursors.Hand;
        var g = MN.Columns("52", "*", "auto");
        g.Children.Add(_logo.At(0, 0));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
        texts.Children.Add(_number);
        texts.Children.Add(_name);
        g.Children.Add(texts.At(0, 1));
        g.Children.Add(_star.At(0, 2));
        Child = g;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        Paint();
    }

    public void Set(MediaEntry e, bool favorite, bool focused)
    {
        _logo.Url = e.Icon;
        _number.Text = e.Number > 0 ? e.Number.ToString() : "";
        _number.Visibility = e.Number > 0 ? Visibility.Visible : Visibility.Collapsed;
        _name.Text = e.Name;
        _star.Visibility = favorite ? Visibility.Visible : Visibility.Collapsed;
        _focused = focused;
        Paint();
    }

    private void Paint()
    {
        Background = _focused || _hover ? MN.CardFocus : MN.Card;
        BorderBrush = _focused ? MN.Blue : (_hover ? MN.BlueSoft : MN.Transparent);
    }
}

/// Poster tile for movies, series and continue-watching cards.
public sealed class PosterTile : Border
{
    private readonly Border _frame = new() { Background = MN.Poster, CornerRadius = new CornerRadius(8) };
    private readonly RemoteImage _image = new(fill: true);
    private readonly Grid _art = new() { ClipToBounds = true };
    private readonly TextBlock _title = MN.Text("", 13, MN.White, maxLines: 2);
    private readonly TextBlock _subtitle = MN.Text("", 11, MN.Text2);
    private readonly Grid _badgeHost = new() { IsHitTestVisible = false };
    private readonly Border _star = new()
    {
        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = MN.Shade, Margin = new Thickness(5),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Child = MN.Icon("", 11, MN.Blue), IsHitTestVisible = false,
    };
    private readonly ProgressLine _progress = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _hover, _focused;

    public PosterTile()
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(2.5);
        Padding = new Thickness(6);
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        var g = MN.Rows("*", "auto", "auto");
        _art.Children.Add(_image);
        _art.Children.Add(_badgeHost);
        _art.Children.Add(_star);
        _art.Children.Add(_progress);
        _frame.Child = _art;
        _frame.SizeChanged += (_, _) =>
            _art.Clip = new RectangleGeometry(new Rect(0, 0, _frame.ActualWidth, _frame.ActualHeight), 8, 8);
        g.Children.Add(_frame.At(0));
        _title.Margin = new Thickness(2, 6, 2, 0);
        _title.Height = 36;
        _title.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(_title.At(1));
        _subtitle.Margin = new Thickness(2, 0, 2, 2);
        g.Children.Add(_subtitle.At(2));
        Child = g;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        Paint();
    }

    public void Set(string? image, string title, string? subtitle, string? badge, bool favorite, double? progress, bool focused)
    {
        _image.Url = image;
        _title.Text = title;
        _subtitle.Text = subtitle ?? "";
        _subtitle.Visibility = subtitle == null ? Visibility.Collapsed : Visibility.Visible;
        _badgeHost.Children.Clear();
        if (badge != null) { var b = Badge.Make(badge); b.Margin = new Thickness(5); _badgeHost.Children.Add(b); }
        _star.Visibility = favorite ? Visibility.Visible : Visibility.Collapsed;
        _progress.Visibility = progress == null ? Visibility.Collapsed : Visibility.Visible;
        if (progress is { } p) _progress.Fraction = p;
        _focused = focused;
        Paint();
    }

    private void Paint()
    {
        Background = _focused || _hover ? MN.CardFocus : MN.Card;
        BorderBrush = _focused ? MN.Blue : (_hover ? MN.BlueSoft : MN.Transparent);
        var s = _focused ? 1.03 : 1.0;
        _scale.ScaleX = s;
        _scale.ScaleY = s;
        Panel.SetZIndex(this, _focused ? 1 : 0);
    }
}

public sealed class SideRow : Border
{
    private readonly TextBlock? _icon;
    private readonly TextBlock _title;
    private bool _hover, _selected, _focused;

    public SideRow(string title, string? glyph = null)
    {
        Height = 40;
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(14, 0, 14, 0);
        Cursor = Cursors.Hand;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null)
        {
            _icon = MN.Icon(glyph, 15);
            _icon.Width = 18;
            _icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(_icon);
        }
        _title = MN.Text(title, 15);
        _title.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_title);
        Child = row;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
        Paint();
    }

    public void Set(bool selected, bool focused, string? title = null)
    {
        _selected = selected;
        _focused = focused;
        if (title != null) _title.Text = title;
        Paint();
    }

    private void Paint()
    {
        Brush fg = _focused ? MN.White : (_selected ? MN.Blue : MN.Text2);
        _title.Foreground = fg;
        if (_icon != null) _icon.Foreground = fg;
        Background = _focused ? MN.Blue : (_selected ? MN.Card : (_hover ? MN.CardHover : MN.Transparent));
    }
}

public sealed class ToastView : Border
{
    private readonly TextBlock _text = MN.Text("", 14, MN.White, bold: true);

    public ToastView()
    {
        Background = MN.Toast;
        CornerRadius = new CornerRadius(20);
        Padding = new Thickness(18, 10, 18, 10);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 30);
        IsHitTestVisible = false;
        Child = _text;
        Visibility = Visibility.Collapsed;
    }

    public void Show(string? text)
    {
        _text.Text = text ?? "";
        Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// In-window choice sheet (account menu, resume question). Keyboard: ↑↓ Enter Esc.
public sealed class ChoiceDialog : Grid
{
    private readonly List<(string Label, Action Action, bool Destructive)> _choices = new();
    private readonly List<Border> _rows = new();
    private int _index;

    public event Action? Closed;

    public ChoiceDialog(string title, string? message)
    {
        Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        var card = new Border
        {
            Background = MN.Surface,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 20, 22, 18),
            Width = 380,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = MN.Divider,
            BorderThickness = new Thickness(1),
        };
        var stack = new StackPanel();
        var t = MN.Text(title, 17, MN.White, bold: true, maxLines: 2);
        t.TextAlignment = TextAlignment.Center;
        stack.Children.Add(t);
        if (!string.IsNullOrEmpty(message))
        {
            var m = MN.Text(message!, 13, MN.Text2, maxLines: 4);
            m.TextAlignment = TextAlignment.Center;
            m.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(m);
        }
        _list.Margin = new Thickness(0, 16, 0, 0);
        stack.Children.Add(_list);
        card.Child = stack;
        Children.Add(card);
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) { Close(); } e.Handled = true; };
    }

    private readonly StackPanel _list = new();

    public ChoiceDialog Add(string label, Action action, bool destructive = false)
    {
        var index = _choices.Count;
        _choices.Add((label, action, destructive));
        var text = MN.Text(label, 14, destructive ? MN.Red : MN.White, bold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new Border { Height = 40, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 3, 0, 3), Child = text, Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => { _index = index; Paint(); };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Choose(index); };
        _rows.Add(row);
        _list.Children.Add(row);
        Paint();
        return this;
    }

    public ChoiceDialog Cancel(string label = "Vazgeç") => Add(label, () => { });

    private void Paint()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Background = i == _index ? MN.Blue : MN.Card;
            if (_rows[i].Child is TextBlock tb && _choices[i].Destructive) tb.Foreground = i == _index ? MN.White : MN.Red;
        }
    }

    private void Choose(int index)
    {
        Close();
        _choices[index].Action();
    }

    public void Close() => Closed?.Invoke();

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Up: case Key.Left: _index = Math.Max(0, _index - 1); Paint(); return true;
            case Key.Down: case Key.Right: case Key.Tab: _index = Math.Min(_rows.Count - 1, _index + 1); Paint(); return true;
            case Key.Enter: case Key.Space: if (_rows.Count > 0) Choose(_index); return true;
            case Key.Escape: Close(); return true;
            default: return true; // modal
        }
    }
}
