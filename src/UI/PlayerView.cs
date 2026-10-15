using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNIPTV.UI;

/// Player controls drawn above the video (shown in the transparent overlay window).
public sealed class PlayerView : Grid
{
    private readonly PlayRequest _request;
    private readonly AppModel _model;
    private readonly IShell _shell;
    public PlayerController Player { get; }

    private bool _controlsVisible = true, _infoVisible = true, _listVisible;
    private int _listIndex;
    private string _numberBuffer = "";
    private CancellationTokenSource? _hideCts, _infoCts, _numberCts, _hudCts;
    private Point _lastMouse = new(-1, -1);

    // layers
    private readonly Spinner _spinner = new(48);
    private readonly Border _messageBox = new() { Background = MN.Shade, CornerRadius = new CornerRadius(10), Padding = new Thickness(20), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBlock _messageText = MN.Text("", 16, MN.White, maxLines: 4);
    private readonly Border _hud = new() { Background = MN.Shade, CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 10, 22, 10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hudText = MN.Text("", 20, MN.White, bold: true);
    private readonly Border _numberBox = new() { Background = MN.Shade, CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 8, 22, 8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _numberText = MN.Text("", 40, MN.White, bold: true);

    // top bar
    private readonly Grid _topBar = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _title = MN.Text("", 18, MN.White, bold: true);
    private readonly TextBlock _subtitle = MN.Text("", 13, MN.Text2);
    private readonly Pill _listButton = new("Kanallar", "");
    private readonly Pill _starButton = new(null, "");
    private readonly Pill _tracksButton = new(null, "");
    private readonly Pill _fillButton = new(null, "");
    private readonly Pill _fullButton = new(null, "");

    // live info
    private readonly Border _liveInfo = new() { VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = MN.Fade(toBottom: true), Padding = new Thickness(40, 50, 40, 28) };
    private readonly RemoteImage _liveLogo = new(fill: false) { Width = 60, Height = 60 };
    private readonly TextBlock _liveNumber = MN.Text("", 22, MN.Blue, bold: true);
    private readonly TextBlock _liveName = MN.Text("", 24, MN.White, bold: true);
    private readonly Grid _qualityHost = new();
    private readonly TextBlock _epgText = MN.Text("", 15, MN.Text2);
    private readonly ProgressLine _epgLine = new() { Margin = new Thickness(0, 8, 0, 0) };

    // channel list
    private readonly Border _channelPanel = new() { Width = 400, HorizontalAlignment = HorizontalAlignment.Left, Background = MN.ListBg };
    private readonly VirtualPanel _channelList = new() { Margin = new Thickness(8, 0, 4, 0), ContentPadding = new Thickness(0, 0, 0, 20) };

    // vod controls
    private readonly Border _vod = new() { VerticalAlignment = VerticalAlignment.Bottom, Background = MN.Fade(toBottom: true), Padding = new Thickness(32, 40, 32, 24) };
    private readonly TextBlock _posText = new() { FontFamily = MN.Mono, FontSize = 13, Foreground = MN.White, VerticalAlignment = VerticalAlignment.Center, Width = 70 };
    private readonly TextBlock _durText = new() { FontFamily = MN.Mono, FontSize = 13, Foreground = MN.Text2, VerticalAlignment = VerticalAlignment.Center, Width = 70, TextAlignment = TextAlignment.Right };
    private readonly Bar _seek = new();
    private readonly Pill _prevEp = new(null, "");
    private readonly Pill _back10 = new("-10", "");
    private readonly Pill _playPause = new(null, "", prominent: true) { MinWidth = 52 };
    private readonly Pill _fwd10 = new("+10", null);
    private readonly Pill _nextEp = new("Sonraki bölüm", "");
    private readonly Pill _mute = new(null, "");
    private readonly Bar _volume = new() { Width = 110, Maximum = 100 };

    public PlayerView(PlayRequest request, AppModel model, IShell shell)
    {
        _request = request;
        _model = model;
        _shell = shell;
        Player = new PlayerController(request, model);
        Background = MN.HitTestable;
        Focusable = false;

        Children.Add(_spinner);
        _messageText.TextAlignment = TextAlignment.Center;
        _messageBox.Child = _messageText;
        Children.Add(_messageBox);
        _hudText.TextAlignment = TextAlignment.Center;
        _hud.Child = _hudText;
        Children.Add(_hud);
        _numberBox.Child = _numberText;
        Children.Add(_numberBox);

        BuildTopBar();
        BuildLiveInfo();
        BuildChannelList();
        BuildVod();

        Children.Add(_liveInfo);
        Children.Add(_vod);
        Children.Add(_channelPanel);
        Children.Add(_topBar);

        MouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _lastMouse.X) + Math.Abs(p.Y - _lastMouse.Y) < 3) return;
            _lastMouse = p;
            Poke();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource != this) return;
            if (e.ClickCount == 2) { _shell.ToggleFullScreen(); e.Handled = true; return; }
            Poke();
            if (Player.IsLive && _listVisible) { _listVisible = false; Render(); }
        };
        MouseWheel += (_, e) =>
        {
            if (_listVisible && _channelPanel.IsMouseOver) return;
            Player.ChangeVolume(e.Delta > 0 ? 5 : -5);
            ShowHud($"Ses: {(int)Player.Volume}");
            e.Handled = true;
        };

        Player.Changed += Render;
        Player.ChannelChanged += () => { FlashInfo(); _channelList.RefreshItems(); };
        Player.OnFinished = () => _model.Back();
    }

    // build

    private void BuildTopBar()
    {
        var row = MN.Columns("auto", "*", "auto");
        row.Margin = new Thickness(28, 26, 28, 40);
        var back = new Pill("Geri", "");
        back.Click += GoBack;
        row.Children.Add(back.At(0, 0));
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
        titles.Children.Add(_title);
        titles.Children.Add(_subtitle);
        row.Children.Add(titles.At(0, 1));
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in new[] { _listButton, _starButton, _tracksButton, _fillButton, _fullButton })
        {
            p.Margin = new Thickness(10, 0, 0, 0);
            right.Children.Add(p);
        }
        _listButton.Click += ToggleList;
        _starButton.Click += () => { if (Player.CurrentChannel is { } ch) { _model.ToggleFavorite(ch); Render(); _channelList.RefreshItems(); } };
        _tracksButton.Click += ShowTracks;
        _tracksButton.ToolTip = "Ses ve altyazı";
        _fillButton.Click += () => { Player.FillScreen = !Player.FillScreen; ShowHud(Player.FillScreen ? "Görüntü: Doldur" : "Görüntü: Sığdır"); Render(); };
        _fillButton.ToolTip = "Görüntü oranı";
        _fullButton.Click += () => _shell.ToggleFullScreen();
        _fullButton.ToolTip = "Tam ekran (F)";
        row.Children.Add(right.At(0, 2));
        _topBar.Background = MN.Fade(toBottom: false);
        _topBar.Children.Add(row);
    }

    private void BuildLiveInfo()
    {
        var stack = new StackPanel();
        var head = MN.Columns("auto", "auto", "*", "auto");
        head.Children.Add(_liveLogo.At(0, 0));
        _liveNumber.Margin = new Thickness(16, 0, 16, 0);
        _liveNumber.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_liveNumber.At(0, 1));
        _liveName.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_liveName.At(0, 2));
        _qualityHost.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(_qualityHost.At(0, 3));
        stack.Children.Add(head);
        _epgText.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(_epgText);
        stack.Children.Add(_epgLine);
        var hint = MN.Text("↑↓ kanal   ← önceki kanal   Enter kanal listesi   Rakamlar kanal no   Boşluk duraklat   F tam ekran   Esc geri", 12, MN.Muted);
        hint.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(hint);
        _liveInfo.Child = stack;
    }

    private void BuildChannelList()
    {
        var g = MN.Rows("auto", "*");
        var title = MN.Text(string.IsNullOrEmpty(_request.ListTitle) ? "Kanallar" : _request.ListTitle, 14, MN.Text2);
        title.Margin = new Thickness(20, 96, 0, 10);
        g.Children.Add(title.At(0));
        _channelList.Create = _ => new ChannelRow();
        _channelList.Update = (i, e) =>
        {
            if (e is not ChannelRow row || i >= Player.Channels.Count) return;
            row.Tag = i;
            var ch = Player.Channels[i];
            row.Set(ch, i, i == _listIndex, i == Player.Current, _model.Store.IsFavorite(ch.Key));
        };
        _channelList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            DependencyObject? d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ChannelRow) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            if (d is not ChannelRow { Tag: int i }) return;
            e.Handled = true;
            _listIndex = i;
            _listVisible = false;
            if (i != Player.Current) Player.Tune(i, immediate: true);
            Render();
        };
        g.Children.Add(_channelList.At(1));
        _channelPanel.Child = g;
    }

    private void BuildVod()
    {
        var stack = new StackPanel();
        var seekRow = MN.Columns("auto", "*", "auto");
        seekRow.Children.Add(_posText.At(0, 0));
        _seek.Margin = new Thickness(12, 0, 12, 0);
        _seek.Dragging += v => { _posText.Text = Fmt.Time(v); Poke(); };
        _seek.Committed += v => { Player.SeekTo(v); Poke(); };
        seekRow.Children.Add(_seek.At(0, 1));
        seekRow.Children.Add(_durText.At(0, 2));
        stack.Children.Add(seekRow);

        var buttons = MN.Columns("auto", "*", "auto");
        buttons.Margin = new Thickness(0, 10, 0, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var p in new[] { _prevEp, _back10, _playPause, _fwd10, _nextEp })
        {
            p.Margin = new Thickness(0, 0, 12, 0);
            left.Children.Add(p);
        }
        _prevEp.ToolTip = "Önceki bölüm (P)";
        _nextEp.ToolTip = "Sonraki bölüm (N)";
        _prevEp.Click += () => Player.PlayEpisode(Player.EpisodeIndex - 1, 0);
        _nextEp.Click += () => Player.PlayEpisode(Player.EpisodeIndex + 1, 0);
        _back10.Click += () => { Player.SeekBy(-10); ShowSeekHud(-10); };
        _fwd10.Click += () => { Player.SeekBy(10); ShowSeekHud(10); };
        _playPause.Click += () => { Player.TogglePause(); Poke(); };
        buttons.Children.Add(left.At(0, 0));
        var volume = new StackPanel { Orientation = Orientation.Horizontal };
        _mute.Click += () => { Player.ToggleMute(); Render(); };
        volume.Children.Add(_mute);
        _volume.Margin = new Thickness(8, 0, 0, 0);
        _volume.VerticalAlignment = VerticalAlignment.Center;
        _volume.Dragging += v => { Player.SetVolume(v); Render(); };
        _volume.Committed += v => { Player.SetVolume(v); Render(); };
        volume.Children.Add(_volume);
        buttons.Children.Add(volume.At(0, 2));
        stack.Children.Add(buttons);
        _vod.Child = stack;
    }

    // lifecycle

    public void Start()
    {
        _model.ActivePlayer = Player;
        Player.Start();
        _listIndex = Player.Current;
        _channelList.SetBlocks(new[] { new Block { Count = Player.Channels.Count, MinItemWidth = 100000, ItemHeight = 50, VSpacing = 2 } });
        Poke();
        FlashInfo();
        Render();
    }

    public void Stop()
    {
        _hideCts?.Cancel();
        _infoCts?.Cancel();
        _numberCts?.Cancel();
        _hudCts?.Cancel();
        Player.Changed -= Render;
        Player.Stop();
        if (_model.ActivePlayer == Player) _model.ActivePlayer = null;
        Cursor = null;
    }

    // render

    private string? OverlayMessage =>
        Player.Status == PlayStatus.Failed ? Player.FailureText + "\nTekrar denemek için Boşluk tuşuna bas." : Player.Message;

    public void Render()
    {
        var p = Player;
        var message = OverlayMessage;
        _spinner.Visibility = p.Status == PlayStatus.Loading && message == null ? Visibility.Visible : Visibility.Collapsed;
        _messageText.Text = message ?? "";
        _messageBox.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        _hud.Margin = new Thickness(0, 0, 0, _controlsVisible && !p.IsLive ? 130 : 60);

        _topBar.Visibility = _controlsVisible || _listVisible ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = p.Title;
        _subtitle.Text = p.Subtitle;
        _subtitle.Visibility = p.Subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _listButton.Visibility = p.IsLive ? Visibility.Visible : Visibility.Collapsed;
        _listButton.Prominent = _listVisible;
        var ch = p.CurrentChannel;
        _starButton.Visibility = p.IsLive && ch != null ? Visibility.Visible : Visibility.Collapsed;
        if (ch != null)
        {
            var fav = _model.Store.IsFavorite(ch.Key);
            _starButton.Glyph = fav ? "" : "";
            _starButton.ToolTip = fav ? "Favorilerden çıkar" : "Favorilere ekle";
        }
        _fullButton.Glyph = _shell.IsFullScreen ? "" : "";

        if (p.IsLive)
        {
            _vod.Visibility = Visibility.Collapsed;
            _channelPanel.Visibility = _listVisible ? Visibility.Visible : Visibility.Collapsed;
            _liveInfo.Visibility = (_infoVisible || _controlsVisible) && ch != null ? Visibility.Visible : Visibility.Collapsed;
            if (ch != null)
            {
                _liveLogo.Url = ch.Icon;
                _liveNumber.Text = ch.Number > 0 ? ch.Number.ToString() : (p.Current + 1).ToString();
                _liveName.Text = ch.Name;
                var q = Quality(p.VideoHeight);
                if (q != (_qualityHost.Tag as string))
                {
                    _qualityHost.Tag = q;
                    _qualityHost.Children.Clear();
                    if (q != null) _qualityHost.Children.Add(Badge.Make(q));
                }
                if (p.Epg is { } e && e.Title.Length > 0)
                {
                    _epgText.Text = $"{e.Title}   {Fmt.Clock(e.Start)} – {Fmt.Clock(e.End)}";
                    _epgText.Visibility = Visibility.Visible;
                    _epgLine.Visibility = Visibility.Visible;
                    _epgLine.Fraction = (Fmt.Now() - e.Start) / Math.Max(1, e.End - e.Start);
                }
                else
                {
                    _epgText.Visibility = Visibility.Collapsed;
                    _epgLine.Visibility = Visibility.Collapsed;
                }
            }
        }
        else
        {
            _liveInfo.Visibility = Visibility.Collapsed;
            _channelPanel.Visibility = Visibility.Collapsed;
            _vod.Visibility = _controlsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!_seek.IsDragging) _posText.Text = Fmt.Time(p.Position);
            _durText.Text = Fmt.Time(p.Duration);
            _seek.Maximum = Math.Max(1, p.Duration);
            _seek.Value = p.Position;
            _prevEp.Visibility = p.HasPreviousEpisode ? Visibility.Visible : Visibility.Collapsed;
            _nextEp.Visibility = p.HasNextEpisode ? Visibility.Visible : Visibility.Collapsed;
            _playPause.Glyph = p.Status == PlayStatus.Paused ? "" : "";
            _mute.Glyph = p.Muted || p.Volume == 0 ? "" : "";
            if (!_volume.IsDragging) _volume.Value = p.Volume;
        }
    }

    private static string? Quality(int h) => h >= 2000 ? "4K" : h >= 1000 ? "FHD" : h >= 700 ? "HD" : h > 0 ? "SD" : null;

    // helpers

    private async void Poke()
    {
        var wasHidden = !_controlsVisible;
        _controlsVisible = true;
        Cursor = null;
        if (wasHidden) Render();
        _hideCts?.Cancel();
        var cts = _hideCts = new CancellationTokenSource();
        try { await Task.Delay(3500, cts.Token); } catch (TaskCanceledException) { return; }
        if (_seek.IsDragging || _volume.IsDragging || _tracksMenuOpen) { Poke(); return; }
        _controlsVisible = false;
        if (!_listVisible) Cursor = Cursors.None;
        Render();
    }

    private async void FlashInfo()
    {
        _infoVisible = true;
        Render();
        _infoCts?.Cancel();
        var cts = _infoCts = new CancellationTokenSource();
        try { await Task.Delay(5000, cts.Token); } catch (TaskCanceledException) { return; }
        _infoVisible = false;
        Render();
    }

    private async void ShowHud(string text)
    {
        _hudText.Text = text;
        _hud.Visibility = Visibility.Visible;
        _hudCts?.Cancel();
        var cts = _hudCts = new CancellationTokenSource();
        try { await Task.Delay(1200, cts.Token); } catch (TaskCanceledException) { return; }
        _hud.Visibility = Visibility.Collapsed;
    }

    private void ShowSeekHud(double delta)
    {
        var total = Player.Duration > 0 ? " / " + Fmt.Time(Player.Duration) : "";
        ShowHud($"{(delta < 0 ? "◀◀" : "▶▶")}  {Fmt.Time(Player.Position)}{total}");
    }

    private void ToggleList()
    {
        _listIndex = Player.Current;
        _listVisible = !_listVisible;
        Poke();
        Render();
        if (_listVisible)
        {
            _channelList.RefreshItems();
            Dispatcher.BeginInvoke(() => _channelList.ScrollIntoView(_listIndex, center: true), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private bool _tracksMenuOpen;

    private void ShowTracks()
    {
        Player.RefreshTracks();
        var menu = new ContextMenu();
        void Header(string text)
        {
            menu.Items.Add(new MenuItem { Header = text, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        }
        void Item(string text, bool check, Action action)
        {
            var mi = new MenuItem { Header = text, IsCheckable = false, IsChecked = check };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Header("Ses");
        if (Player.AudioTracks.Count == 0) menu.Items.Add(new MenuItem { Header = "Ses izi yok", IsEnabled = false });
        foreach (var t in Player.AudioTracks) { var id = t.Id; Item(t.Label, t.Selected, () => Player.SelectAudio(id)); }
        menu.Items.Add(new Separator());
        Header("Altyazı");
        var anySub = false;
        foreach (var t in Player.SubtitleTracks) anySub |= t.Selected;
        Item("Kapalı", !anySub, () => Player.SelectSubtitle(null));
        foreach (var t in Player.SubtitleTracks) { var id = t.Id; Item(t.Label, t.Selected, () => Player.SelectSubtitle(id)); }
        menu.PlacementTarget = _tracksButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Opened += (_, _) => _tracksMenuOpen = true;
        menu.Closed += (_, _) => { _tracksMenuOpen = false; Poke(); };
        menu.IsOpen = true;
    }

    private void GoBack()
    {
        Player.SaveProgress();
        _model.Back();
    }

    private async void AppendDigit(int d)
    {
        if (_numberBuffer.Length >= 5) _numberBuffer = "";
        _numberBuffer += d.ToString();
        _numberText.Text = _numberBuffer;
        _numberBox.Visibility = Visibility.Visible;
        _numberCts?.Cancel();
        var cts = _numberCts = new CancellationTokenSource();
        try { await Task.Delay(1500, cts.Token); } catch (TaskCanceledException) { return; }
        var n = int.TryParse(_numberBuffer, out var x) ? x : 0;
        _numberBuffer = "";
        _numberBox.Visibility = Visibility.Collapsed;
        if (!Player.JumpToNumber(n)) _model.ShowToast($"{n} numaralı kanal bu listede yok");
    }

    private static int Digit(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => k - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => k - Key.NumPad0,
        _ => -1,
    };

    // keyboard

    public bool HandleKey(KeyInput k)
    {
        var p = Player;
        var repeating = k.Repeat;

        // Shared keys
        if (k.Key == Key.Escape || k.Key == Key.Back)
        {
            if (p.IsLive && _listVisible) { _listVisible = false; Render(); }
            else if (k.Key == Key.Escape && _shell.IsFullScreen) _shell.ToggleFullScreen();
            else GoBack();
            return true;
        }
        if ((k.Key == Key.F || k.Key == Key.F11) && !repeating) { _shell.ToggleFullScreen(); return true; }
        if (k.Key == Key.M && !repeating) { p.ToggleMute(); ShowHud(p.Muted ? "Ses kapalı" : $"Ses: {(int)p.Volume}"); Render(); return true; }
        if (k.Key is Key.OemPlus or Key.Add) { p.ChangeVolume(5); ShowHud($"Ses: {(int)p.Volume}"); return true; }
        if (k.Key is Key.OemMinus or Key.Subtract) { p.ChangeVolume(-5); ShowHud($"Ses: {(int)p.Volume}"); return true; }

        if (p.IsLive)
        {
            if (_listVisible)
            {
                switch (k.Key)
                {
                    case Key.Up: _listIndex = Math.Max(0, _listIndex - 1); break;
                    case Key.Down: _listIndex = Math.Min(p.Channels.Count - 1, _listIndex + 1); break;
                    case Key.PageUp: _listIndex = Math.Max(0, _listIndex - 10); break;
                    case Key.PageDown: _listIndex = Math.Min(p.Channels.Count - 1, _listIndex + 10); break;
                    case Key.Enter:
                    case Key.Right:
                        _listVisible = false;
                        if (_listIndex != p.Current) p.Tune(_listIndex, immediate: true);
                        Render();
                        return true;
                    case Key.Left:
                    case Key.L:
                        _listVisible = false;
                        Render();
                        return true;
                    default:
                        return false;
                }
                _channelList.RefreshItems();
                _channelList.ScrollIntoView(_listIndex);
                Poke();
                return true;
            }
            switch (k.Key)
            {
                case Key.Up: p.Zap(-1, repeating); FlashInfo(); return true;
                case Key.Down: p.Zap(1, repeating); FlashInfo(); return true;
                case Key.PageUp: p.Zap(-1, repeating); FlashInfo(); return true;
                case Key.PageDown: p.Zap(1, repeating); FlashInfo(); return true;
                case Key.Left: if (!repeating) p.GoPrevious(); return true;
                case Key.Right:
                    if (!repeating) { if (_infoVisible) { _infoVisible = false; _infoCts?.Cancel(); Render(); } else FlashInfo(); }
                    return true;
                case Key.Enter: if (!repeating) ToggleList(); return true;
                case Key.L: if (!repeating) ToggleList(); return true;
                case Key.Space: if (!repeating) p.TogglePause(); return true;
            }
            var d = Digit(k.Key);
            if (d >= 0 && !repeating && !k.Shift) { AppendDigit(d); return true; }
            return false;
        }

        // Movie / series
        var step = k.Shift ? 60 : 10;
        switch (k.Key)
        {
            case Key.Left: p.SeekBy(-step); ShowSeekHud(-step); Poke(); return true;
            case Key.Right: p.SeekBy(step); ShowSeekHud(step); Poke(); return true;
            case Key.Up: p.ChangeVolume(5); ShowHud($"Ses: {(int)p.Volume}"); Render(); return true;
            case Key.Down: p.ChangeVolume(-5); ShowHud($"Ses: {(int)p.Volume}"); Render(); return true;
            case Key.Space:
            case Key.Enter:
                if (!repeating) { p.TogglePause(); Poke(); }
                return true;
            case Key.N:
                if (!repeating && p.HasNextEpisode) p.PlayEpisode(p.EpisodeIndex + 1, 0);
                return true;
            case Key.P:
                if (!repeating && p.HasPreviousEpisode) p.PlayEpisode(p.EpisodeIndex - 1, 0);
                return true;
        }
        return false;
    }
}

public sealed class ChannelRow : Border
{
    private readonly TextBlock _number = MN.Text("", 13, MN.Text2);
    private readonly RemoteImage _logo = new(fill: false) { Width = 36, Height = 36 };
    private readonly TextBlock _name = MN.Text("", 15);
    private readonly TextBlock _star = MN.Icon("", 11, MN.Blue);
    private bool _hover, _focused, _current;

    public ChannelRow()
    {
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(10, 0, 10, 0);
        Cursor = Cursors.Hand;
        var g = MN.Columns("44", "36", "*", "auto");
        _number.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(_number.At(0, 0));
        g.Children.Add(_logo.At(0, 1));
        _name.Margin = new Thickness(10, 0, 6, 0);
        _name.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(_name.At(0, 2));
        g.Children.Add(_star.At(0, 3));
        Child = g;
        MouseEnter += (_, _) => { _hover = true; Paint(); };
        MouseLeave += (_, _) => { _hover = false; Paint(); };
    }

    public void Set(MediaEntry ch, int index, bool focused, bool current, bool favorite)
    {
        _number.Text = ch.Number > 0 ? ch.Number.ToString() : (index + 1).ToString();
        _logo.Url = ch.Icon;
        _name.Text = ch.Name;
        _star.Visibility = favorite ? Visibility.Visible : Visibility.Collapsed;
        _focused = focused;
        _current = current;
        Paint();
    }

    private void Paint()
    {
        Background = _focused ? MN.Blue : (_current ? MN.Card : (_hover ? MN.CardHover : MN.Transparent));
        _number.Foreground = _focused ? MN.White : (_current ? MN.Blue : MN.Text2);
        _star.Foreground = _focused ? MN.White : MN.Blue;
    }
}
