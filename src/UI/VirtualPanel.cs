using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNIPTV.UI;

/// A run of equally sized items laid out in rows. Columns follow the available width.
public sealed class Block
{
    public int Count { get; init; }
    public double MinItemWidth { get; init; } = 100;
    public int MinColumns { get; init; } = 1;
    public double ItemHeight { get; init; } = 40;
    public double HSpacing { get; init; } = 12;
    public double VSpacing { get; init; } = 12;

    internal int Columns;
    internal double ItemWidth;
    internal double Top;
    internal int Rows => Columns <= 0 ? 0 : (Count + Columns - 1) / Columns;
    internal double Height => Rows == 0 ? 0 : Rows * ItemHeight + (Rows - 1) * VSpacing;
}

/// Scrolling panel that only creates the items that are on screen, so channel lists with
/// tens of thousands of entries stay fast. Items are addressed by a global index across blocks.
public sealed class VirtualPanel : Grid
{
    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Border _thumb = new() { Width = 6, CornerRadius = new CornerRadius(3), Background = MN.Track, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 1, 0) };
    private readonly Dictionary<int, FrameworkElement> _items = new();
    private List<Block> _blocks = new();
    private double _offset, _contentHeight;
    private bool _dragging;
    private double _dragStartY, _dragStartOffset;

    public Func<int, FrameworkElement>? Create { get; set; }
    public Action<int, FrameworkElement>? Update { get; set; }
    public Thickness ContentPadding { get; set; } = new(0, 6, 0, 30);
    public double BlockGap { get; set; } = 14;
    public event Action? ColumnsChanged;

    public VirtualPanel()
    {
        Children.Add(_canvas);
        Children.Add(_thumb);
        ClipToBounds = true;
        _thumb.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _dragStartY = e.GetPosition(this).Y;
            _dragStartOffset = _offset;
            _thumb.CaptureMouse();
            e.Handled = true;
        };
        _thumb.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var view = ActualHeight;
            if (view <= 0 || _contentHeight <= view) return;
            var dy = e.GetPosition(this).Y - _dragStartY;
            SetOffset(_dragStartOffset + dy * _contentHeight / view);
        };
        _thumb.MouseLeftButtonUp += (_, _) => { _dragging = false; _thumb.ReleaseMouseCapture(); };
        SizeChanged += (_, e) =>
        {
            var before = string.Join(",", _blocks.Select(b => b.Columns));
            Relayout();
            if (e.WidthChanged && before != string.Join(",", _blocks.Select(b => b.Columns))) ColumnsChanged?.Invoke();
        };
    }

    public int Total => _blocks.Sum(b => b.Count);

    public int ColumnsOf(int block) => block >= 0 && block < _blocks.Count ? Math.Max(1, _blocks[block].Columns) : 1;

    public void SetBlocks(IEnumerable<Block> blocks, bool keepOffset = false)
    {
        foreach (var e in _items.Values) _canvas.Children.Remove(e);
        _items.Clear();
        _blocks = blocks.ToList();
        if (!keepOffset) _offset = 0;
        Relayout();
    }

    /// Re-applies Update to every realized item (focus/favourite state changed).
    public void RefreshItems()
    {
        if (Update == null) return;
        foreach (var (i, e) in _items) Update(i, e);
    }

    public void RefreshItem(int index)
    {
        if (Update != null && _items.TryGetValue(index, out var e)) Update(index, e);
    }

    private void MeasureBlocks()
    {
        var width = Math.Max(0, ActualWidth - 10);
        var y = ContentPadding.Top;
        var first = true;
        foreach (var b in _blocks)
        {
            if (b.Count == 0) { b.Columns = 1; b.Top = y; continue; }
            if (!first) y += BlockGap;
            first = false;
            b.Columns = Math.Max(b.MinColumns, (int)Math.Floor((width + b.HSpacing) / (b.MinItemWidth + b.HSpacing)));
            b.ItemWidth = Math.Max(10, (width - (b.Columns - 1) * b.HSpacing) / b.Columns);
            b.Top = y;
            y += b.Height;
        }
        _contentHeight = y + ContentPadding.Bottom;
    }

    private bool Locate(int index, out Block block, out double x, out double y)
    {
        var start = 0;
        foreach (var b in _blocks)
        {
            if (index < start + b.Count)
            {
                var local = index - start;
                int row = local / b.Columns, col = local % b.Columns;
                x = col * (b.ItemWidth + b.HSpacing);
                y = b.Top + row * (b.ItemHeight + b.VSpacing);
                block = b;
                return true;
            }
            start += b.Count;
        }
        block = null!;
        x = y = 0;
        return false;
    }

    private void Relayout()
    {
        MeasureBlocks();
        ClampOffset();
        Realize();
    }

    private void ClampOffset()
    {
        _offset = Math.Max(0, Math.Min(_offset, Math.Max(0, _contentHeight - ActualHeight)));
    }

    private void SetOffset(double value)
    {
        var old = _offset;
        _offset = value;
        ClampOffset();
        if (Math.Abs(old - _offset) > 0.1) Realize();
    }

    private void Realize()
    {
        var view = ActualHeight;
        double top = _offset - 200, bottom = _offset + view + 200;
        var wanted = new HashSet<int>();
        var start = 0;
        foreach (var b in _blocks)
        {
            if (b.Count > 0 && b.Columns > 0)
            {
                var rowH = b.ItemHeight + b.VSpacing;
                var firstRow = Math.Max(0, (int)Math.Floor((top - b.Top) / rowH));
                var lastRow = Math.Min(b.Rows - 1, (int)Math.Floor((bottom - b.Top) / rowH));
                for (var r = firstRow; r <= lastRow; r++)
                    for (var c = 0; c < b.Columns; c++)
                    {
                        var i = start + r * b.Columns + c;
                        if (i < start + b.Count) wanted.Add(i);
                    }
            }
            start += b.Count;
        }
        foreach (var i in _items.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _canvas.Children.Remove(_items[i]);
            _items.Remove(i);
        }
        foreach (var i in wanted)
        {
            if (!Locate(i, out var b, out var x, out var y)) continue;
            if (!_items.TryGetValue(i, out var e))
            {
                if (Create == null) continue;
                e = Create(i);
                _items[i] = e;
                _canvas.Children.Add(e);
                Update?.Invoke(i, e);
            }
            e.Width = b.ItemWidth;
            e.Height = b.ItemHeight;
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y - _offset);
        }
        if (view > 0 && _contentHeight > view + 1)
        {
            _thumb.Visibility = Visibility.Visible;
            var h = Math.Max(30, view * view / _contentHeight);
            _thumb.Height = h;
            _thumb.Margin = new Thickness(0, (view - h) * (_offset / (_contentHeight - view)), 1, 0);
        }
        else
        {
            _thumb.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetOffset(_offset - e.Delta * 0.9);
        e.Handled = true;
    }

    public void ScrollIntoView(int index, bool center = false)
    {
        if (!Locate(index, out var b, out _, out var y)) return;
        var view = ActualHeight;
        if (view <= 0) return;
        if (center) { SetOffset(y - (view - b.ItemHeight) / 2); return; }
        if (y - 8 < _offset) SetOffset(y - 8 - (index < b.Columns ? ContentPadding.Top : 0));
        else if (y + b.ItemHeight + 8 > _offset + view) SetOffset(y + b.ItemHeight + 8 - view);
    }

    public void ScrollToTop() => SetOffset(0);
}
