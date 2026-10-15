using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MNIPTV.UI;

public interface IShell
{
    void ToggleFullScreen();
    bool IsFullScreen { get; }
}

/// Transparent window that floats exactly over the video area and holds the player controls.
/// (WPF cannot draw on top of a native child window inside the same window.)
public sealed class PlayerWindow : Window
{
    private readonly Grid _root = new();
    private readonly ToastView _toast = new();
    public bool AllowClose { get; set; }

    public PlayerWindow(MainWindow owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = true;
        Title = "MN IPTV";
        UseLayoutRounding = true;
        Content = _root;
        _root.Children.Add(_toast);
        PreviewKeyDown += owner.OnPreviewKeyDown;
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            owner.Close();
        };
    }

    public void SetView(UIElement? view)
    {
        _root.Children.Clear();
        if (view != null) _root.Children.Add(view);
        _root.Children.Add(_toast);
    }

    public void ShowToast(string? text) => _toast.Show(text);
}

public sealed class MainWindow : Window, IShell
{
    private readonly AppModel _model;
    private readonly Grid _root = new() { Background = MN.Black };
    private readonly VideoHost _video = new() { Visibility = Visibility.Hidden };
    private readonly Grid _screens = new();
    private readonly Grid _dialogHost = new();
    private readonly ToastView _toast = new();
    private PlayerWindow? _overlayWindow;

    private LoginView? _login;
    private BrowseView? _browse;
    private SeriesView? _series;
    private PlayerView? _player;
    private LoginView? _changeAccount;
    private ChoiceDialog? _dialog;
    private bool _fullScreen;
    private WindowState _restoreState = WindowState.Normal;

    public LoginView? Login => _changeAccount ?? _login;
    public bool IsFullScreen => _fullScreen;
    /// Created on first use: an owned window needs its owner to be shown first.
    public PlayerWindow Overlay => _overlayWindow ??= new PlayerWindow(this);

    public MainWindow(AppModel model)
    {
        _model = model;
        Title = "MN IPTV";
        Width = 1440;
        Height = 880;
        MinWidth = 960;
        MinHeight = 600;
        Background = MN.Black;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        FontFamily = MN.Font;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Focusable = true;
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/assets/app.ico", UriKind.Absolute));
        }
        catch (Exception)
        {
            // The taskbar icon still comes from the exe.
        }
        KeyboardNavigation.SetTabNavigation(_screens, KeyboardNavigationMode.Cycle);

        _root.Children.Add(_video);
        _root.Children.Add(_screens);
        _root.Children.Add(_dialogHost);
        _root.Children.Add(_toast);
        Content = _root;

        PreviewKeyDown += OnPreviewKeyDown;
        LocationChanged += (_, _) => SyncOverlay();
        SizeChanged += (_, _) => SyncOverlay();
        StateChanged += (_, _) => SyncOverlay();
        _root.SizeChanged += (_, _) => SyncOverlay();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += (_, _) =>
        {
            PlaybackEngine.VideoWindow = _video.VideoHandle;
            _model.StartIfNeeded();
            Focus();
        };
        Closing += OnClosing;

        _model.Changed += OnModelChanged;
        RebuildScreens();
    }

    // model

    private void OnModelChanged(string what)
    {
        switch (what)
        {
            case "account":
                RebuildScreens();
                break;
            case "route":
                ApplyRoute();
                break;
            case "toast":
                _overlayWindow?.ShowToast(_player != null ? _model.Toast : null);
                _toast.Show(_player == null ? _model.Toast : null);
                break;
            case "revision":
                _series?.Refresh();
                break;
        }
    }

    private void RebuildScreens()
    {
        CloseDialog();
        TearDownPlayer();
        _series = null;
        _screens.Children.Clear();
        _changeAccount = null;
        if (_model.Account == null)
        {
            _login = new LoginView(_model);
            _screens.Children.Add(_login);
        }
        else
        {
            _login = null;
            _browse ??= new BrowseView(_model, ShowAccountMenu);
            _screens.Children.Add(_browse);
        }
        ApplyRoute();
    }

    private void ApplyRoute()
    {
        var top = _model.RouteStack.LastOrDefault();

        // Player
        if (top is PlayerRoute pr)
        {
            if (_player == null || _player.Player.Request.Token != pr.Request.Token)
            {
                TearDownPlayer();
                _video.Visibility = Visibility.Visible;
                _video.UpdateLayout();
                PlaybackEngine.VideoWindow = _video.VideoHandle;
                _player = new PlayerView(pr.Request, _model, this);
                Overlay.SetView(_player);
                ShowOverlay();
                _player.Start();
            }
        }
        else
        {
            TearDownPlayer();
        }

        // Series
        if (top is SeriesRoute sr)
        {
            if (_series == null || _series.Entry.Key != sr.Entry.Key)
            {
                if (_series != null) _screens.Children.Remove(_series);
                _series = new SeriesView(sr.Entry, _model, Present);
                _screens.Children.Add(_series);
            }
            else if (_series.Visibility != Visibility.Visible)
            {
                _series.ReturnedFromPlayer();
            }
            _series.Visibility = Visibility.Visible;
            _series.Refresh();
        }
        else if (top is PlayerRoute && _series != null)
        {
            // Keep the series screen underneath (hidden) so returning keeps its place.
            var below = _model.RouteStack.Count >= 2 ? _model.RouteStack[^2] as SeriesRoute : null;
            if (below == null || below.Entry.Key != _series.Entry.Key) { _screens.Children.Remove(_series); _series = null; }
            else _series.Visibility = Visibility.Hidden;
        }
        else if (_series != null)
        {
            _screens.Children.Remove(_series);
            _series = null;
        }

        var catalogue = top == null;
        if (_browse != null)
        {
            _browse.Visibility = catalogue ? Visibility.Visible : Visibility.Hidden;
            _browse.Active = catalogue;
        }
        if (top is not PlayerRoute && IsLoaded)
        {
            Activate();
            if (_login == null && _changeAccount == null) Focus();
        }
    }

    private void TearDownPlayer()
    {
        if (_player == null) return;
        _player.Stop();
        _player = null;
        _overlayWindow?.SetView(null);
        _overlayWindow?.Hide();
        _video.Visibility = Visibility.Hidden;
        _toast.Show(_model.Toast);
    }

    private void ShowOverlay()
    {
        SyncOverlay();
        if (!Overlay.IsVisible) Overlay.Show();
        SyncOverlay();
        Overlay.Activate();
    }

    private void SyncOverlay()
    {
        if (_player == null || !IsLoaded) return;
        if (WindowState == WindowState.Minimized) return;
        var source = PresentationSource.FromVisual(_root);
        if (source?.CompositionTarget == null) return;
        var topLeft = source.CompositionTarget.TransformFromDevice.Transform(_root.PointToScreen(new Point(0, 0)));
        var o = Overlay;
        o.Left = topLeft.X;
        o.Top = topLeft.Y;
        o.Width = _root.ActualWidth;
        o.Height = _root.ActualHeight;
    }

    // dialogs

    public void Present(ChoiceDialog dialog)
    {
        CloseDialog();
        _dialog = dialog;
        dialog.Closed += () =>
        {
            if (_dialog != dialog) return;
            _dialogHost.Children.Clear();
            _dialog = null;
            Focus();
        };
        _dialogHost.Children.Add(dialog);
    }

    private void CloseDialog()
    {
        _dialogHost.Children.Clear();
        _dialog = null;
    }

    private void ShowAccountMenu()
    {
        var acc = _model.Account;
        Present(new ChoiceDialog("Hesap", acc == null ? null : $"{acc.Username}\n{acc.Server}")
            .Add("Listeyi yenile", () => _model.ReloadSection())
            .Add("Hesabı değiştir", ShowChangeAccount)
            .Add("Çıkış yap", () => _model.SignOut(), destructive: true)
            .Cancel());
    }

    private void ShowChangeAccount()
    {
        if (_changeAccount != null) return;
        _changeAccount = new LoginView(_model, () =>
        {
            if (_changeAccount != null) _screens.Children.Remove(_changeAccount);
            _changeAccount = null;
            _browse?.Reclaim();
        });
        _screens.Children.Add(_changeAccount);
    }

    // full screen

    public void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _restoreState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _fullScreen = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            _fullScreen = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _restoreState;
        }
        Dispatcher.BeginInvoke(SyncOverlay, System.Windows.Threading.DispatcherPriority.Loaded);
        _player?.Render();
    }

    // keyboard

    public void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var input = new KeyInput(key, mods.HasFlag(ModifierKeys.Shift), mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), e.IsRepeat);
        if (Dispatch(input)) e.Handled = true;
    }

    public bool Dispatch(KeyInput k)
    {
        if (_dialog != null) return _dialog.HandleKey(k.Key);

        if (k.Key == Key.F11 || (k.Alt && k.Key == Key.Enter)) { ToggleFullScreen(); return true; }

        if (_model.Account != null && _changeAccount == null)
        {
            if (k.Ctrl)
            {
                switch (k.Key)
                {
                    case >= Key.D1 and <= Key.D6:
                        _model.GoToSection(SectionExt.All[k.Key - Key.D1]);
                        return true;
                    case Key.F:
                        _model.GoToSection(Section.Search);
                        return true;
                    case Key.R:
                        _model.ReloadSection();
                        return true;
                }
            }
            if (k.Key == Key.F5) { _model.ReloadSection(); return true; }
            if (k.Alt && k.Key == Key.Left) { _model.Back(); return true; }
        }

        if (_changeAccount != null) return _changeAccount.HandleKey(k);
        if (_login != null) return _login.HandleKey(k);
        if (k.Ctrl || k.Alt) return false;

        var top = _model.RouteStack.LastOrDefault();
        if (top is PlayerRoute && _player != null) return _player.HandleKey(k);
        if (top is SeriesRoute && _series != null) return _series.HandleKey(k);
        return _browse?.HandleKey(k) ?? false;
    }

    // window

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _player?.Player.SaveProgress();
        TearDownPlayer();
        if (_overlayWindow != null)
        {
            _overlayWindow.AllowClose = true;
            _overlayWindow.Close();
        }
        PlaybackEngine.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows: keep the default title bar.
        }
    }
}
