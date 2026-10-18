using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MNPlayer.UI;

/// Everything drawn above the video: control bar, tool strip, centre button, menus and dialogs.
/// Positions follow the macOS layout one to one.
public sealed class PlayerUi : Canvas
{
    private readonly PlayerShell _s;

    public readonly Border BarBox = new() { Background = MN.Hud, BorderBrush = MN.HudBorder, BorderThickness = new Thickness(0, 1, 0, 0) };
    private readonly Canvas _bar = new();
    public readonly Slider2 Progress = new() { Maximum = 1, Enabled = false };
    public readonly Slider2 Volume = new() { Maximum = 100 };
    public readonly IconButton PlayButton = new("", "Oynat / duraklat", 17);
    public readonly IconButton NextButton = new("", "Sonraki video", 15);
    public readonly IconButton MuteButton = new("", "Sessiz", 16);
    public readonly IconButton CaptionsButton = new("", "Altyazılar", 16);
    public readonly IconButton SettingsButton = new("", "Ayarlar", 16);
    public readonly IconButton MiniButton = new("", "Mini oynatıcı (I)", 15);
    public readonly IconButton FullButton = new("", "Tam ekran (F)", 15);
    public readonly TextBlock TimeLabel = new() { FontFamily = MN.Mono, FontSize = 12, Foreground = MN.White, Text = "0:00 / 0:00", VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

    private readonly Border _tools = new() { Background = MN.Hud, BorderBrush = MN.HudBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
    private readonly Canvas _toolCanvas = new();
    public readonly IconButton CenterButton = new("", "Medya aç / oynat", 54, MN.Blue);
    private readonly Border _status = new() { Background = MN.Toast, CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 9, 18, 9), Visibility = Visibility.Collapsed, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _statusText = MN.Text("", 13, MN.White);
    private readonly Grid _menuLayer = new() { Background = MN.HitTestable, Visibility = Visibility.Collapsed };
    private readonly MenuPanel _menu = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Grid _dialogLayer = new() { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _compact;
    private bool _controlsVisible = true;

    public bool ControlsVisible => _controlsVisible;
    public bool MenuOpen => _menuLayer.Visibility == Visibility.Visible;
    public bool DialogOpen => _dialogLayer.Visibility == Visibility.Visible;
    public double BarHeight => _compact ? 110 : 74;

    public PlayerUi(PlayerShell shell)
    {
        _s = shell;
        Background = MN.HitTestable;
        Focusable = false;
        ClipToBounds = true;

        BarBox.Child = _bar;
        foreach (var e in new UIElement[] { Progress, PlayButton, NextButton, MuteButton, Volume, TimeLabel, CaptionsButton, SettingsButton, MiniButton, FullButton })
            _bar.Children.Add(e);
        Children.Add(BarBox);

        var tools = new[]
        {
            new IconButton("", "Medya aç (Ctrl+O)", 15),
            new IconButton("", "Bağlantı aç (Ctrl+L)", 15),
            new IconButton("", "Altyazı ekle (Ctrl+S)", 15),
            new IconButton("", "Oynatma listesi (Ctrl+P)", 15),
            new IconButton("", "Klavye kısayolları (?)", 15),
        };
        for (var i = 0; i < tools.Length; i++)
        {
            SetLeft(tools[i], i * 38 + 4);
            SetTop(tools[i], 4);
            tools[i].Width = 34;
            tools[i].Height = 32;
            _toolCanvas.Children.Add(tools[i]);
        }
        tools[0].Click += _s.OpenMedia;
        tools[1].Click += _s.OpenUrl;
        tools[2].Click += _s.OpenSubtitles;
        tools[3].Click += _s.ShowQueue;
        tools[4].Click += _s.ShowShortcuts;
        _tools.Child = _toolCanvas;
        Children.Add(_tools);

        Children.Add(CenterButton);
        _status.Child = _statusText;
        Children.Add(_status);

        _menuLayer.Children.Add(_menu);
        Children.Add(_menuLayer);
        Children.Add(_dialogLayer);

        PlayButton.Click += _s.TogglePlay;
        CenterButton.Click += _s.TogglePlay;
        NextButton.Click += _s.Next;
        MuteButton.Click += _s.ToggleMute;
        CaptionsButton.Click += _s.CaptionsMenu;
        SettingsButton.Click += _s.SettingsMenu;
        MiniButton.Click += _s.ToggleMini;
        FullButton.Click += _s.ToggleFullScreen;
        Progress.Dragging += _s.SeekFraction;
        Progress.Committed += _s.SeekFraction;
        Volume.Dragging += v => _s.SetVolume(v);
        Volume.Committed += v => _s.SetVolume(v);

        _menu.Closed += () => { _menuLayer.Visibility = Visibility.Collapsed; _s.Wake(); };
        _menuLayer.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == _menuLayer) { _menu.Close(); e.Handled = true; }
        };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); _status.Visibility = Visibility.Collapsed; };

        SizeChanged += (_, _) => DoLayout();
    }

    public void DoLayout()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 1 || h < 1) return;
        _compact = w < 600;
        var barH = BarHeight;
        SetLeft(BarBox, 0);
        SetTop(BarBox, h - barH);
        BarBox.Width = w;
        BarBox.Height = barH;
        _bar.Width = w;
        _bar.Height = barH;

        Place(Progress, 12, 4, w - 24, 20);
        Place(PlayButton, 12, 31, 36, 36);
        Place(NextButton, 52, 31, 32, 36);
        Place(MuteButton, 89, 31, 32, 36);
        Place(Volume, 126, 40, 72, 18);
        Volume.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        Place(TimeLabel, _compact ? 128 : 210, 31, _compact ? Math.Max(60, w - 140) : 175, 36);
        TimeLabel.FontSize = _compact ? 11 : 12;
        var rightTop = _compact ? 67 : 31;
        var rights = new FrameworkElement[] { CaptionsButton, SettingsButton, MiniButton, FullButton };
        for (var i = 0; i < rights.Length; i++) Place(rights[i], w - (4 - i) * 42 - 10, rightTop, 36, 36);

        SetLeft(_tools, w - 208);
        SetTop(_tools, 12);
        _tools.Width = 198;
        _tools.Height = 40;

        SetLeft(CenterButton, w / 2 - 35);
        SetTop(CenterButton, h / 2 - 35);
        CenterButton.Width = 70;
        CenterButton.Height = 70;

        _status.Width = Math.Min(560, Math.Max(120, w - 32));
        SetLeft(_status, (w - _status.Width) / 2);
        SetTop(_status, Math.Max(8, h / 2 - 110));

        _menuLayer.Width = w;
        _menuLayer.Height = h;
        _menu.Margin = new Thickness(0, 0, 10, barH + 8);
        _menu.MaxHeight = Math.Max(120, h - barH - 40);
        _dialogLayer.Width = w;
        _dialogLayer.Height = h;
    }

    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        SetLeft(e, x);
        SetTop(e, y);
        e.Width = w;
        e.Height = h;
    }

    public void SetControlsVisible(bool visible, bool showTools)
    {
        _controlsVisible = visible;
        BarBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _tools.Visibility = visible && showTools ? Visibility.Visible : Visibility.Collapsed;
        Cursor = visible ? Cursors.Arrow : Cursors.None;
    }

    public void Notify(string text)
    {
        _statusText.Text = text;
        _status.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public void ShowMenu(List<MenuEntry> entries)
    {
        _menu.Visibility = Visibility.Visible;
        _menu.Show(entries);
        _menuLayer.Visibility = Visibility.Visible;
    }

    public void CloseMenu()
    {
        if (MenuOpen) _menu.Close();
    }

    public void ShowDialog(CardDialog dialog)
    {
        _dialogLayer.Children.Clear();
        _dialogLayer.Children.Add(dialog);
        _dialogLayer.Visibility = Visibility.Visible;
        dialog.Closed += () =>
        {
            _dialogLayer.Visibility = Visibility.Collapsed;
            _dialogLayer.Children.Clear();
            _s.Wake();
        };
    }

    public void CloseDialog()
    {
        _dialogLayer.Visibility = Visibility.Collapsed;
        _dialogLayer.Children.Clear();
    }
}
