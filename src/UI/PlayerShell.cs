using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MNPlayer.UI;

public enum PState { Idle, Opening, Buffering, Playing, Paused, Ended, Error }

/// Transparent window floating exactly over the video area. It holds the controls
/// (WPF cannot draw above a native child window inside the same window).
public sealed class OverlayWindow : Window
{
    public bool AllowClose { get; set; }

    public OverlayWindow(Window owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = true;
        Title = "MN Player";
        UseLayoutRounding = true;
        AllowDrop = true;
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            Owner?.Close();
        };
    }
}

public sealed class PlayerShell : Window
{
    public static readonly string[] VideoExt = "mp4 m4v mov mkv avi webm wmv flv f4v mpg mpeg mpe m1v m2v mpv ts mts m2ts m2t vob ogv ogm 3gp 3g2 asf divx rm rmvb dv mxf nut nsv hevc h264 h265 y4m av1 amv mpegts".Split(' ');
    public static readonly string[] AudioExt = "mp3 m4a aac wav flac ogg opus aiff aif wma ac3 dts ape caf au mka alac".Split(' ');
    public static readonly string[] SubExt = { "srt", "vtt", "ass", "ssa", "sub" };
    private static readonly string[] Schemes = { "http", "https", "rtsp", "rtmp", "udp", "rtp" };

    public static bool IsUrl(string s) => s.Contains("://", StringComparison.Ordinal);
    public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    public static bool IsVideoFile(string path) => VideoExt.Contains(Ext(path));

    public bool Preview { get; }
    public event Action? PreviewHidden;

    private readonly Grid _root = new() { Background = Brushes.Black };
    private readonly VideoHost _video = new();
    public PlayerUi Ui { get; }
    public OverlayWindow? Overlay { get; private set; }
    public MpvEngine? Engine { get; private set; }

    public readonly List<string> Queue = new();
    public int Index { get; private set; } = -1;
    public bool RepeatOne { get; set; }
    public bool Autoplay { get; set; } = true;
    public double Rate { get; private set; } = 1;
    public double VolumeValue { get; private set; } = 80;
    public bool Muted { get; private set; }
    public bool IsFullScreen { get; private set; }
    public bool IsMini { get; private set; }
    public PState State { get; private set; } = PState.Idle;
    public long LastTime { get; private set; }
    public long LastLength { get; private set; }
    public bool Seekable { get; private set; }
    public double? VideoAspect { get; private set; }
    public bool EngineFailed { get; private set; }
    public string? CurrentItem => Queue.Count > 0 && Index >= 0 && Index < Queue.Count ? Queue[Index] : null;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(3) };
    private DispatcherTimer? _sleep;
    private bool _loaded, _failed, _ended, _errorShown, _terminating, _ready;
    private long? _pendingSeek;
    private DateTime _positionSaved = DateTime.MinValue;
    private DateTime _lastSeekSend = DateTime.MinValue;
    private WindowState _restoreState = WindowState.Normal;
    private Rect _regularBounds = Rect.Empty;
    private Rect _preMiniBounds = Rect.Empty;
    private int _sleepMinutes;
    private IntPtr _previousForeground;
    private QueueWindow? _queueWindow;
    private double _chromeW = 16, _chromeH = 39;
    private readonly List<string> _pendingOpen = new();

    public PlayerShell(bool preview)
    {
        Preview = preview;
        if (preview) Autoplay = false;
        Title = "MN Player";
        Width = 1136;
        Height = 719;
        MinWidth = 316;
        MinHeight = 239;
        Background = Brushes.Black;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute)); }
        catch (Exception) { }
        _root.Children.Add(_video);
        Content = _root;
        Ui = new PlayerUi(this);
        if (preview) Topmost = true;

        var s = Settings.Current;
        VolumeValue = Math.Max(0, Math.Min(100, s.Volume));
        Rate = s.Rate > 0 ? s.Rate : 1;
        if (!preview && s.WinWidth > 300 && s.WinHeight > 200 && OnScreen(s.WinLeft, s.WinTop, s.WinWidth, s.WinHeight))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WinLeft;
            Top = s.WinTop;
            Width = s.WinWidth;
            Height = s.WinHeight;
        }

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.DarkTitleBar(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnKeyDown;
        LocationChanged += (_, _) => SyncOverlay();
        SizeChanged += (_, _) => SyncOverlay();
        StateChanged += (_, _) => SyncOverlay();
        _root.SizeChanged += (_, _) => SyncOverlay();
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(new Action(SyncOverlay), DispatcherPriority.Loaded); };
        _tick.Tick += (_, _) => Tick();
        _hide.Tick += (_, _) => AutoHide();
    }

    private static bool OnScreen(double l, double t, double w, double h)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        return l + w > vl + 80 && t + 60 > vt && l < vl + SystemParameters.VirtualScreenWidth - 80 && t < vt + SystemParameters.VirtualScreenHeight - 60;
    }

    // setup

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var engine = new MpvEngine(_video.VideoHandle, null);
        if (!engine.Ready)
        {
            EngineFailed = true;
            MessageBox.Show("Medya motoru başlatılamadı. Uygulamayı yeniden kurmayı dene.\n\n" + engine.Error, "MN Player", MessageBoxButton.OK, MessageBoxImage.Error);
            AllowCloseNow = true;
            Close();
            return;
        }
        Engine = engine;
        engine.Set("volume", VolumeValue);
        engine.Set("speed", Rate);
        Overlay = new OverlayWindow(this) { Content = Ui };
        Overlay.PreviewKeyDown += OnKeyDown;
        Overlay.PreviewMouseWheel += (_, ev) => { SetVolume(VolumeValue + (ev.Delta > 0 ? 5 : -5)); ev.Handled = true; };
        Overlay.DragOver += (_, ev) => { ev.Effects = DragDropEffects.Copy; ev.Handled = true; };
        Overlay.Drop += (_, ev) =>
        {
            if (ev.Data.GetData(DataFormats.FileDrop) is string[] files) Add(files);
        };
        Ui.MouseMove += (_, _) => Wake();
        Ui.MouseLeftButtonDown += OnVideoClick;
        Overlay.Show();
        SyncOverlay();
        Ui.DoLayout();
        Ui.SetControlsVisible(true, !Preview);
        Ui.Volume.Value = VolumeValue;
        UpdateMuteGlyph();
        RefreshUi();
        _ready = true;
        _tick.Start();
        Wake();
        if (_pendingOpen.Count > 0) { var items = _pendingOpen.ToList(); _pendingOpen.Clear(); Add(items); }
    }

    public bool AllowCloseNow { get; set; }

    private void OnVideoClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != Ui) return;
        if (e.ClickCount == 2) { TogglePlay(); ToggleFullScreen(); }
        else if (CurrentItem != null) TogglePlay();
        else OpenMedia();
        e.Handled = true;
    }

    /// Queue media from outside (second launch, drag-drop, preview request).
    public void Add(IEnumerable<string> items)
    {
        if (!_ready) { _pendingOpen.AddRange(items); return; }
        var list = items.Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        var subs = list.Where(i => !IsUrl(i) && SubExt.Contains(Ext(i))).ToList();
        var media = list.Where(i => !subs.Contains(i) && (IsUrl(i) || !Directory.Exists(i))).ToList();
        if (media.Count > 0)
        {
            var start = Queue.Count;
            Queue.AddRange(media);
            Play(start);
        }
        foreach (var s in subs) AddSubtitle(s);
        _queueWindow?.Reload();
    }

    // playback

    public void Play(int i)
    {
        if (Engine == null || i < 0 || i >= Queue.Count) return;
        SavePosition();
        Engine.DrainEvents();
        Index = i;
        _loaded = false;
        _failed = false;
        _ended = false;
        _errorShown = false;
        LastTime = 0;
        LastLength = 0;
        Seekable = false;
        VideoAspect = null;
        _positionSaved = DateTime.UtcNow;
        var item = Queue[i];
        _pendingSeek = !Preview && Settings.Current.Positions.TryGetValue(item, out var pos) ? pos : null;
        State = PState.Opening;
        Engine.Set("pause", "no");
        if (Engine.Load(item) < 0) { Ui.Notify("Dosya açılamadı"); }
        Engine.Set("volume", VolumeValue);
        Title = Preview ? DisplayName(item) : DisplayName(item) + " – MN Player";
        _queueWindow?.Reload();
        RefreshUi();
        Wake();
    }

    public static string DisplayName(string item)
    {
        if (IsUrl(item))
        {
            try { var u = new Uri(item); var name = Path.GetFileName(u.AbsolutePath); return string.IsNullOrEmpty(name) ? u.Host : name; }
            catch (Exception) { return item; }
        }
        return Path.GetFileName(item);
    }

    public void TogglePlay()
    {
        if (Engine == null) return;
        if (CurrentItem == null) { OpenMedia(); return; }
        if (State == PState.Ended || State == PState.Error) Play(Index);
        else Engine.Set("pause", Engine.Flag("pause") ? "no" : "yes");
        RefreshUi();
        Wake();
    }

    public void Pause()
    {
        if (Engine != null && CurrentItem != null) Engine.Set("pause", "yes");
    }

    public void Next() { if (Index + 1 < Queue.Count) Play(Index + 1); }

    public void Previous()
    {
        if (Index > 0) Play(Index - 1);
        else Engine?.Seek(0);
    }

    public void SeekFraction(double fraction)
    {
        if (Engine == null || LastLength <= 0 || !Seekable) return;
        if ((DateTime.UtcNow - _lastSeekSend).TotalMilliseconds < 80 && Ui.Progress.IsDragging) return;
        _lastSeekSend = DateTime.UtcNow;
        Engine.Seek(fraction * LastLength / 1000.0);
        Wake();
    }

    public void SeekBy(long deltaMs)
    {
        if (Engine == null || !Seekable) return;
        var target = Math.Max(0, Math.Min(LastLength, LastTime + deltaMs));
        Engine.Seek(target / 1000.0);
        LastTime = target;
        Wake();
    }

    public void SeekPercent(int tenths)
    {
        if (Engine == null || LastLength <= 0 || !Seekable) return;
        Engine.Seek(LastLength * tenths / 10 / 1000.0);
    }

    public void SetVolume(double v)
    {
        VolumeValue = Math.Max(0, Math.Min(100, Math.Round(v)));
        Muted = false;
        Engine?.Set("volume", VolumeValue);
        Engine?.Set("mute", "no");
        Settings.Current.Volume = VolumeValue;
        Ui.Volume.Value = VolumeValue;
        UpdateMuteGlyph();
        Ui.Notify("Ses: " + (int)VolumeValue);
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        Engine?.Set("mute", Muted ? "yes" : "no");
        UpdateMuteGlyph();
        Wake();
    }

    private void UpdateMuteGlyph() => Ui.MuteButton.Glyph = Muted || VolumeValue <= 0 ? "" : "";

    public bool SetRate(double r)
    {
        if (Engine == null) return false;
        if (Engine.Set("speed", r) < 0) { Ui.Notify("Bu kaynakta hız değiştirilemiyor"); return false; }
        Rate = r;
        Settings.Current.Rate = r;
        return true;
    }

    public void ToggleSubtitles()
    {
        if (Engine == null) return;
        var subs = Engine.Tracks().Where(t => t.Type == "sub").ToList();
        if (subs.Count == 0) { Ui.Notify("Altyazı yok"); return; }
        if (subs.Any(t => t.Selected)) { Engine.Set("sid", "no"); Ui.Notify("Altyazı kapalı"); }
        else { Engine.Set("sid", subs[0].Id); Ui.Notify("Altyazı açık"); }
    }

    public void FrameStep() => Engine?.CmdAsync("frame-step");

    public void AddSubtitle(string path)
    {
        if (Engine == null || CurrentItem == null) { Ui.Notify("Önce medya aç"); return; }
        var r = Engine.Cmd("sub-add", path, "select");
        Ui.Notify(r >= 0 ? "Altyazı eklendi" : "Altyazı açılamadı");
    }

    // tick

    private void Tick()
    {
        if (Engine == null || _terminating) return;
        foreach (var ev in Engine.DrainEvents())
        {
            switch (ev.Kind)
            {
                case MpvEventKind.Loaded:
                    _loaded = true;
                    Engine.Set("speed", Rate);
                    break;
                case MpvEventKind.Failed:
                    _failed = true;
                    break;
                case MpvEventKind.Ended:
                    if (!_ended && _loaded)
                    {
                        _ended = true;
                        OnEnded();
                    }
                    break;
            }
        }

        var timePos = Engine.Number("time-pos");
        var duration = Engine.Number("duration");
        if (_loaded && !_ended)
        {
            LastTime = double.IsFinite(timePos) ? (long)(timePos * 1000) : LastTime;
            LastLength = double.IsFinite(duration) ? (long)(duration * 1000) : 0;
            Seekable = Engine.Flag("seekable");
        }

        PState state;
        if (Index < 0) state = PState.Idle;
        else if (_failed) state = PState.Error;
        else if (_ended) state = PState.Ended;
        else if (!_loaded) state = PState.Opening;
        else if (Engine.Flag("pause")) state = PState.Paused;
        else if (Engine.Flag("paused-for-cache")) state = PState.Buffering;
        else state = PState.Playing;
        State = state;

        Native.KeepAwake(state == PState.Playing || state == PState.Opening || state == PState.Buffering);

        if (_loaded)
        {
            var dw = Engine.Number("dwidth");
            var dh = Engine.Number("dheight");
            if (double.IsFinite(dw) && double.IsFinite(dh) && dw > 0 && dh > 0)
            {
                var aspect = dw / dh;
                if (VideoAspect == null || Math.Abs(aspect - VideoAspect.Value) > 0.005)
                {
                    VideoAspect = aspect;
                    FitVideoWindow();
                }
            }
            if (_pendingSeek is { } seek && LastLength > 0)
            {
                _pendingSeek = null;
                if (seek > 5000 && seek < LastLength - 5000) Engine.Seek(seek / 1000.0);
            }
        }

        if (state == PState.Error && !_errorShown) { _errorShown = true; Wake(); ShowError(); }
        if ((DateTime.UtcNow - _positionSaved).TotalSeconds > 5) { SavePosition(); _positionSaved = DateTime.UtcNow; }
        RefreshUi();
    }

    private void OnEnded()
    {
        if (CurrentItem is { } item) { Settings.Current.ForgetPosition(item); }
        if (RepeatOne) { Play(Index); return; }
        if (Autoplay && Index + 1 < Queue.Count) { Play(Index + 1); return; }
        Wake();
    }

    private void RefreshUi()
    {
        var playing = State == PState.Playing || State == PState.Buffering || State == PState.Opening;
        Ui.PlayButton.Glyph = playing ? "" : "";
        Ui.CenterButton.Visibility = CurrentItem == null ? Visibility.Visible : Visibility.Collapsed;
        Ui.NextButton.Enabled = Index + 1 < Queue.Count;
        Ui.Progress.Enabled = Seekable && LastLength > 0;
        if (!Ui.Progress.IsDragging) Ui.Progress.Value = LastLength > 0 ? Math.Max(0, Math.Min(1, (double)LastTime / LastLength)) : 0;
        Ui.TimeLabel.Text = $"{Clock(LastTime)} / {(LastLength > 0 ? Clock(LastLength) : "0:00")}";
        Ui.FullButton.Glyph = IsFullScreen ? "" : "";
    }

    public static string Clock(long ms)
    {
        var s = Math.Max(0, ms / 1000);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    // controls visibility

    public void Wake()
    {
        Ui.SetControlsVisible(true, !Preview);
        _hide.Stop();
        _hide.Start();
    }

    private void AutoHide()
    {
        _hide.Stop();
        if (State != PState.Playing || Ui.MenuOpen || Ui.DialogOpen) return;
        if (Ui.BarBox.IsMouseOver && !IsFullScreen) { Wake(); return; }
        Ui.SetControlsVisible(false, false);
    }

    // window geometry

    private void SyncOverlay()
    {
        if (Overlay == null || !IsLoaded || WindowState == WindowState.Minimized) return;
        var source = PresentationSource.FromVisual(_root);
        if (source?.CompositionTarget == null) return;
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(_root.PointToScreen(new Point(0, 0)));
        Overlay.Left = topLeft.X;
        Overlay.Top = topLeft.Y;
        Overlay.Width = _root.ActualWidth;
        Overlay.Height = _root.ActualHeight;
    }

    private Rect WorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var wa = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var tl = m.Transform(new Point(wa.Left, wa.Top));
        var br = m.Transform(new Point(wa.Right, wa.Bottom));
        return new Rect(tl, br);
    }

    public void FitVideoWindow()
    {
        if (VideoAspect is not { } ratio || IsFullScreen || WindowState != WindowState.Normal || !IsLoaded) return;
        _chromeW = Math.Max(0, ActualWidth - _root.ActualWidth);
        _chromeH = Math.Max(0, ActualHeight - _root.ActualHeight);
        var avail = WorkArea();
        avail.Inflate(-16, -16);
        var maxW = Math.Min(avail.Width - _chromeW, IsMini ? 420 : 1120);
        var maxH = Math.Min(avail.Height - _chromeH, IsMini ? 440 : 800);
        var height = Math.Min(maxH, maxW / ratio);
        var width = height * ratio;
        MinWidth = Math.Min(300, width) + _chromeW;
        MinHeight = Math.Min(200, height) + _chromeH;
        var newW = width + _chromeW;
        var newH = height + _chromeH;
        var cx = Left + ActualWidth / 2;
        var cy = Top + ActualHeight / 2;
        var l = Math.Max(avail.Left, Math.Min(avail.Right - newW, cx - newW / 2));
        var t = Math.Max(avail.Top, Math.Min(avail.Bottom - newH, cy - newH / 2));
        Left = l;
        Top = t;
        Width = newW;
        Height = newH;
        SyncOverlay();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmSizing = 0x0214, WmNcActivate = 0x0086;
        if (msg == WmNcActivate && wParam == IntPtr.Zero && Overlay != null)
        {
            // Keep the title bar looking active while the overlay (our own window) has the focus.
            var fg = Native.GetForegroundWindow();
            if (fg == new WindowInteropHelper(Overlay).Handle)
            {
                handled = true;
                return Native.DefWindowProc(hwnd, msg, (IntPtr)1, (IntPtr)(-1));
            }
        }
        if (msg == WmSizing && VideoAspect is { } ratio && !IsFullScreen && ratio > 0.05)
        {
            var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(lParam);
            Native.GetWindowRect(hwnd, out var win);
            Native.GetClientRect(hwnd, out var client);
            var ncW = (win.Right - win.Left) - (client.Right - client.Left);
            var ncH = (win.Bottom - win.Top) - (client.Bottom - client.Top);
            var w = rect.Right - rect.Left - ncW;
            var h = rect.Bottom - rect.Top - ncH;
            var edge = wParam.ToInt32();
            if (edge == 3 || edge == 6)
            {
                w = (int)Math.Round(h * ratio);
                rect.Right = rect.Left + w + ncW;
            }
            else
            {
                h = (int)Math.Round(w / ratio);
                if (edge == 3 || edge == 4 || edge == 5) rect.Top = rect.Bottom - h - ncH;
                else rect.Bottom = rect.Top + h + ncH;
            }
            System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, false);
            handled = true;
            return (IntPtr)1;
        }
        return IntPtr.Zero;
    }

    public void ToggleFullScreen()
    {
        if (!IsFullScreen)
        {
            IsFullScreen = true;
            _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            if (WindowState == WindowState.Normal) _regularBounds = new Rect(Left, Top, Width, Height);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            IsFullScreen = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Topmost = IsMini || Preview;
            WindowState = _restoreState;
            if (_restoreState == WindowState.Normal && !_regularBounds.IsEmpty)
            {
                Left = _regularBounds.Left; Top = _regularBounds.Top; Width = _regularBounds.Width; Height = _regularBounds.Height;
            }
            Dispatcher.BeginInvoke(new Action(() => { SyncOverlay(); FitVideoWindow(); }), DispatcherPriority.Loaded);
        }
        Dispatcher.BeginInvoke(new Action(() => { SyncOverlay(); Ui.DoLayout(); RefreshUi(); }), DispatcherPriority.Loaded);
        Wake();
    }

    public void ToggleMini()
    {
        if (IsFullScreen) return;
        if (IsMini)
        {
            IsMini = false;
            Topmost = Preview;
            if (!_preMiniBounds.IsEmpty)
            {
                Left = _preMiniBounds.Left; Top = _preMiniBounds.Top; Width = _preMiniBounds.Width; Height = _preMiniBounds.Height;
            }
            else FitVideoWindow();
            _preMiniBounds = Rect.Empty;
        }
        else
        {
            _preMiniBounds = new Rect(Left, Top, Width, Height);
            IsMini = true;
            Topmost = true;
            if (VideoAspect != null) FitVideoWindow();
            else { Width = 420 + _chromeW; Height = 260 + _chromeH; }
        }
        SyncOverlay();
        Ui.DoLayout();
        Wake();
    }

    // keyboard

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleKey(key, Keyboard.Modifiers)) e.Handled = true;
    }

    public bool HandleKey(Key key, ModifierKeys mods)
    {
        if (Ui.DialogOpen) return false;
        if (Ui.MenuOpen)
        {
            if (key == Key.Escape) { Ui.CloseMenu(); return true; }
            return false;
        }
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            switch (key)
            {
                case Key.O: OpenMedia(); return true;
                case Key.L: OpenUrl(); return true;
                case Key.S: OpenSubtitles(); return true;
                case Key.P: ShowQueue(); return true;
            }
            return false;
        }
        if (key == Key.F11 || (alt && key == Key.Return)) { ToggleFullScreen(); return true; }
        if (alt) return false;
        if (Preview)
        {
            if (key == Key.Escape) { HidePreview(); return true; }
            if (key == Key.Up) { Previous(); Wake(); return true; }
            if (key == Key.Down) { Next(); Wake(); return true; }
        }
        var handled = true;
        switch (key)
        {
            case Key.Space:
            case Key.K: TogglePlay(); break;
            case Key.J: SeekBy(-10000); break;
            case Key.L: SeekBy(10000); break;
            case Key.M: ToggleMute(); break;
            case Key.F: ToggleFullScreen(); break;
            case Key.I: ToggleMini(); break;
            case Key.C: ToggleSubtitles(); break;
            case Key.OemPeriod: FrameStep(); break;
            case Key.F1: ShowShortcuts(); break;
            case Key.OemQuestion: ShowShortcuts(); break;
            case Key.Left: SeekBy(-5000); break;
            case Key.Right: SeekBy(5000); break;
            case Key.Up: SetVolume(VolumeValue + 5); break;
            case Key.Down: SetVolume(VolumeValue - 5); break;
            case Key.PageDown: Next(); break;
            case Key.PageUp: Previous(); break;
            case Key.Escape:
                if (IsFullScreen) ToggleFullScreen(); else handled = false;
                break;
            default:
                if (key >= Key.D0 && key <= Key.D9) SeekPercent(key - Key.D0);
                else if (key >= Key.NumPad0 && key <= Key.NumPad9) SeekPercent(key - Key.NumPad0);
                else handled = false;
                break;
        }
        if (handled) Wake();
        return handled;
    }

    // dialogs and menus

    private Window DialogOwner => (Window?)Overlay ?? this;

    public void OpenMedia()
    {
        var filter = "Medya dosyaları|" + string.Join(";", VideoExt.Concat(AudioExt).Select(x => "*." + x)) + "|Tüm dosyalar|*.*";
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = filter, Title = "Medya aç" };
        if (dlg.ShowDialog(DialogOwner) == true) Add(dlg.FileNames);
    }

    public void OpenSubtitles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Altyazı|" + string.Join(";", SubExt.Select(x => "*." + x)) + "|Tüm dosyalar|*.*",
            Title = "Altyazı ekle",
        };
        if (dlg.ShowDialog(DialogOwner) == true) foreach (var f in dlg.FileNames) AddSubtitle(f);
    }

    public void OpenUrl()
    {
        CardDialog? dialog = null;
        dialog = new CardDialog("Medya bağlantısı", null, "https://…",
            ("İptal", false, null),
            ("Aç", true, text =>
            {
                if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && Schemes.Contains(uri.Scheme.ToLowerInvariant())) Add(new[] { uri.ToString() });
                else Ui.Notify("Geçerli bir bağlantı gir");
            }));
        Ui.ShowDialog(dialog);
    }

    public void ShowQueue()
    {
        if (_queueWindow == null || !_queueWindow.IsLoaded)
        {
            _queueWindow = new QueueWindow(this);
        }
        _queueWindow.Show();
        _queueWindow.Activate();
    }

    public void ShowShortcuts()
    {
        const string text =
            "Boşluk / K: Oynat / duraklat\nJ / L: 10 saniye  •  ← / →: 5 saniye\n↑ / ↓: Ses  •  M: Sessiz\nF / F11: Tam ekran  •  I: Mini oynatıcı\nC: Altyazı aç / kapat\n0–9: Sürenin %0–90 noktasına git\n.: Duraklatılmış videoda sonraki kare\nPgDn / PgUp: Sonraki / önceki video\nCtrl+O: Dosya  •  Ctrl+L: Bağlantı  •  Ctrl+S: Altyazı  •  Ctrl+P: Liste\nExplorer'da Ctrl+M: Seçili videoyu önizle";
        Ui.ShowDialog(new CardDialog("Klavye kısayolları", text, null, ("Tamam", true, null)));
    }

    private void ShowError()
    {
        Ui.ShowDialog(new CardDialog("Medya açılamadı",
            "Dosya bozuk, şifreli, erişilemez veya bu motorun desteklemediği bir biçimde olabilir.", null,
            ("Başka dosya aç", false, _ => OpenMedia()),
            ("Tamam", true, null)));
    }

    private List<MenuEntry> TrackEntries(string type, string prop)
    {
        var tracks = Engine?.Tracks().Where(t => t.Type == type).ToList() ?? new List<MpvTrack>();
        var list = new List<MenuEntry>
        {
            new() { Text = "Kapalı", Checked = !tracks.Any(t => t.Selected), Click = () => Engine?.Set(prop, "no") },
        };
        var n = 0;
        foreach (var t in tracks)
        {
            n++;
            var label = !string.IsNullOrEmpty(t.Title) ? t.Title : (!string.IsNullOrEmpty(t.Lang) ? t.Lang : $"Kanal {t.Id}");
            if (!string.IsNullOrEmpty(t.Title) && !string.IsNullOrEmpty(t.Lang)) label = $"{t.Title} [{t.Lang}]";
            var id = t.Id;
            list.Add(new MenuEntry { Text = $"{n}. {label}", Checked = t.Selected, Click = () => Engine?.Set(prop, id) });
        }
        return list;
    }

    public void CaptionsMenu()
    {
        var items = TrackEntries("sub", "sid");
        items.Add(MenuEntry.Line());
        items.Add(new MenuEntry { Text = "Altyazı dosyası ekle…", Click = OpenSubtitles });
        Ui.ShowMenu(items);
        Wake();
    }

    public void SettingsMenu()
    {
        var speeds = new List<MenuEntry>();
        foreach (var r in new[] { 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2.0 })
        {
            var rate = r;
            speeds.Add(new MenuEntry { Text = rate == 1 ? "Normal" : rate.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + "×", Checked = Math.Abs(Rate - rate) < 0.01, Click = () => SetRate(rate) });
        }
        var sleeps = new List<MenuEntry>();
        foreach (var m in new[] { 0, 15, 30, 60, 90 })
        {
            var minutes = m;
            sleeps.Add(new MenuEntry { Text = minutes == 0 ? "Kapalı" : minutes + " dakika", Checked = _sleepMinutes == minutes, Click = () => SetSleep(minutes) });
        }
        var items = new List<MenuEntry>
        {
            new() { Text = "Oynatma hızı", Sub = speeds },
            new() { Text = "Ses kanalı", Sub = TrackEntries("audio", "aid") },
            new() { Text = "Döngü", Checked = RepeatOne, Click = () => RepeatOne = !RepeatOne },
            new() { Text = "Otomatik sonraki video", Checked = Autoplay, Click = () => Autoplay = !Autoplay },
            new() { Text = "Uyku zamanlayıcısı", Sub = sleeps },
            new() { Text = "Medya bilgisi", Click = ShowInfo },
            MenuEntry.Line(),
            new() { Text = "Varsayılan video oynatıcı yap…", Click = SetDefaults },
            new() { Text = "Explorer'da Ctrl+M önizleme", Checked = PreviewAutostart.Enabled, Click = TogglePreviewService },
            new() { Text = "MN Player hakkında", Click = ShowAbout },
        };
        Ui.ShowMenu(items);
        Wake();
    }

    private void SetSleep(int minutes)
    {
        _sleep?.Stop();
        _sleepMinutes = minutes;
        if (minutes <= 0) return;
        _sleep = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
        _sleep.Tick += (_, _) => { _sleep?.Stop(); _sleepMinutes = 0; Pause(); Wake(); };
        _sleep.Start();
    }

    public void ShowInfo()
    {
        if (CurrentItem is not { } item || Engine == null) return;
        var w = Engine.Text("video-params/w");
        var h = Engine.Text("video-params/h");
        var size = string.IsNullOrEmpty(w) ? "Yalnızca ses" : $"{w} × {h}";
        var codec = Engine.Text("video-codec");
        Ui.ShowDialog(new CardDialog(DisplayName(item), $"{size}\n{Clock(LastLength)}\n{codec}\nmpv {Engine.Text("mpv-version")}", null, ("Tamam", true, null)));
    }

    private void ShowAbout()
    {
        Ui.ShowDialog(new CardDialog("MN Player " + (typeof(PlayerShell).Assembly.GetName().Version?.ToString(3) ?? ""),
            "Mavi Nokta Production\nmpv medya motoru. Lisanslar kurulum klasöründeki Licenses klasöründedir.", null, ("Tamam", true, null)));
    }

    private void SetDefaults()
    {
        Ui.ShowDialog(new CardDialog("Varsayılan video oynatıcı",
            "Windows, varsayılan uygulamayı programların kendi başına değiştirmesine izin vermez. Ayarlar açılınca \"MN Player\"ı seç ve video türlerinin yanındaki uygulamayı MN Player yap.", null,
            ("Vazgeç", false, null),
            ("Ayarları aç", true, _ =>
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=MN%20Player") { UseShellExecute = true }); }
                catch (Exception) { Ui.Notify("Ayarlar açılamadı"); }
            })));
    }

    private void TogglePreviewService()
    {
        var on = !PreviewAutostart.Enabled;
        PreviewAutostart.Set(on);
        Ui.Notify(on ? "Explorer'da Ctrl+M önizleme açık" : "Explorer'da Ctrl+M önizleme kapalı");
    }

    // preview mode

    public void OpenPreview(string selected, IntPtr previousForeground)
    {
        if (!Preview) return;
        _previousForeground = previousForeground;
        var folder = Path.GetDirectoryName(selected) ?? "";
        List<string> siblings;
        try
        {
            siblings = Directory.EnumerateFiles(folder).Where(IsVideoFile).ToList();
            siblings.Sort((a, b) => Native.StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
        }
        catch (Exception)
        {
            siblings = new List<string>();
        }
        Queue.Clear();
        Queue.AddRange(siblings);
        var idx = Queue.FindIndex(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) { Queue.Add(selected); idx = Queue.Count - 1; }
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (_ready) Play(idx);
        else { _pendingPlay = idx; Loaded += StartPendingPreview; }
        Overlay?.Show();
        Overlay?.Activate();
    }

    private int _pendingPlay = -1;

    private void StartPendingPreview(object sender, RoutedEventArgs e)
    {
        Loaded -= StartPendingPreview;
        if (_pendingPlay >= 0) Dispatcher.BeginInvoke(new Action(() => Play(_pendingPlay)), DispatcherPriority.Loaded);
    }

    public void HidePreview()
    {
        if (!Preview) return;
        if (IsFullScreen) ToggleFullScreen();
        SavePositionSkip();
        Engine?.Stop();
        Queue.Clear();
        Index = -1;
        State = PState.Idle;
        _loaded = false;
        Native.KeepAwake(false);
        Ui.CloseMenu();
        Ui.CloseDialog();
        Hide();
        Overlay?.Hide();
        if (_previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground)) Native.SetForegroundWindow(_previousForeground);
        PreviewHidden?.Invoke();
    }

    private void SavePositionSkip() { }

    // lifetime

    public void SavePosition()
    {
        if (Preview || CurrentItem is not { } item || Engine == null) return;
        Settings.Current.RememberPosition(item, LastTime, LastLength);
        Settings.Current.Save();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Preview && !AllowCloseNow)
        {
            e.Cancel = true;
            HidePreview();
            return;
        }
        _terminating = true;
        _tick.Stop();
        _hide.Stop();
        _sleep?.Stop();
        Native.KeepAwake(false);
        if (!Preview)
        {
            SavePosition();
            if (WindowState == WindowState.Normal && !IsFullScreen)
            {
                Settings.Current.WinLeft = Left; Settings.Current.WinTop = Top; Settings.Current.WinWidth = Width; Settings.Current.WinHeight = Height;
            }
            Settings.Current.Save();
        }
        _queueWindow?.Close();
        if (Overlay != null) { Overlay.AllowClose = true; Overlay.Close(); }
        Engine?.Shutdown();
        Engine = null;
    }
}
