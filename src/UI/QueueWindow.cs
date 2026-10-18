using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MNPlayer.UI;

/// Playlist panel (the macOS "Oynatma listesi" panel).
public sealed class QueueWindow : Window
{
    private readonly PlayerShell _s;
    private readonly StackPanel _list = new();
    private int _selected = -1;

    public QueueWindow(PlayerShell shell)
    {
        _s = shell;
        Title = "Oynatma listesi";
        Width = 440;
        Height = 400;
        MinWidth = 280;
        MinHeight = 160;
        Background = MN.Surface;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        try { Owner = shell; } catch (Exception) { }
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Focusable = false };
        SourceInitialized += (_, _) => Native.DarkTitleBar(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => Reload();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    public void Reload()
    {
        if (!IsLoaded) return;
        _list.Children.Clear();
        for (var i = 0; i < _s.Queue.Count; i++)
        {
            var index = i;
            var current = i == _s.Index;
            var text = MN.Text((current ? "▶  " : "") + PlayerShell.DisplayName(_s.Queue[i]), 13, current ? MN.Blue : MN.White);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.VerticalAlignment = VerticalAlignment.Center;
            var row = new Border
            {
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                Background = index == _selected ? MN.HudHover : MN.HitTestable,
                Child = text,
                Cursor = Cursors.Hand,
            };
            row.MouseLeftButtonDown += (_, e) =>
            {
                _selected = index;
                if (e.ClickCount == 2) _s.Play(index);
                Reload();
            };
            _list.Children.Add(row);
        }
        if (_s.Queue.Count == 0)
        {
            var empty = MN.Text("Liste boş. Bir dosya aç veya sürükleyip bırak.", 13, MN.Text2);
            empty.Margin = new Thickness(16);
            _list.Children.Add(empty);
        }
    }
}
