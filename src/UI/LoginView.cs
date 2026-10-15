using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MNIPTV.UI;

public readonly record struct KeyInput(Key Key, bool Shift, bool Ctrl, bool Alt, bool Repeat);

public sealed class LoginView : Grid
{
    private readonly AppModel _model;
    private readonly Action? _onCancel;
    private readonly TextBox _server = Box();
    private readonly TextBox _user = Box();
    private readonly PasswordBox _pass = new();
    private readonly TextBlock _error = MN.Text("", 14, MN.Red, maxLines: 3);
    private readonly Spinner _spinner = new(22) { Visibility = Visibility.Collapsed, Margin = new Thickness(14, 0, 0, 0) };
    private readonly Pill _connect = new("Bağlan", prominent: true) { MinWidth = 150 };
    private bool _busy;

    public LoginView(AppModel model, Action? onCancel = null)
    {
        _model = model;
        _onCancel = onCancel;
        Background = MN.Black;

        var form = new StackPanel { Width = 440, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 28) };
        brand.Children.Add(new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Fill = MN.Blue, VerticalAlignment = VerticalAlignment.Center });
        var name = MN.Text("MN IPTV", 26, MN.White, bold: true);
        name.Margin = new Thickness(12, 0, 0, 0);
        brand.Children.Add(name);
        form.Children.Add(brand);

        form.Children.Add(Label("Sunucu adresi", 0));
        form.Children.Add(WithPlaceholder(_server, "http://sunucu.com:8080"));
        form.Children.Add(Label("Kullanıcı adı", 16));
        form.Children.Add(_user);
        form.Children.Add(Label("Şifre", 16));
        StyleBox(_pass);
        form.Children.Add(_pass);
        _error.Margin = new Thickness(0, 14, 0, 0);
        _error.Visibility = Visibility.Collapsed;
        form.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        _connect.Click += Connect;
        buttons.Children.Add(_connect);
        if (onCancel != null)
        {
            var cancel = new Pill("Vazgeç") { Margin = new Thickness(14, 0, 0, 0) };
            cancel.Click += onCancel;
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(_spinner);
        form.Children.Add(buttons);
        Children.Add(form);

        if (model.Store.Account is { } acc)
        {
            _server.Text = acc.Server;
            _user.Text = acc.Username;
            _pass.Password = acc.Password;
        }
        Loaded += (_, _) => FocusFirstEmpty(always: true);
    }

    private static TextBox Box()
    {
        var t = new TextBox();
        StyleBox(t);
        return t;
    }

    private static void StyleBox(Control c)
    {
        c.Height = 44;
        c.FontSize = 16;
        c.FontFamily = MN.Font;
        c.Foreground = MN.White;
        c.Background = MN.Card;
        c.BorderBrush = MN.Divider;
        c.BorderThickness = new Thickness(2);
        c.Padding = new Thickness(10, 0, 10, 0);
        c.VerticalContentAlignment = VerticalAlignment.Center;
        if (c is TextBox t) t.CaretBrush = MN.White;
        if (c is PasswordBox p) p.CaretBrush = MN.White;
        c.GotKeyboardFocus += (_, _) => c.BorderBrush = MN.Blue;
        c.LostKeyboardFocus += (_, _) => c.BorderBrush = MN.Divider;
    }

    private static TextBlock Label(string text, double top)
    {
        var t = MN.Text(text, 13, MN.Text2);
        t.Margin = new Thickness(0, top, 0, 6);
        return t;
    }

    private static Grid WithPlaceholder(TextBox box, string placeholder)
    {
        var g = new Grid();
        g.Children.Add(box);
        var hint = MN.Text(placeholder, 16, MN.Muted);
        hint.Margin = new Thickness(14, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(hint);
        void Sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        return g;
    }

    public void Fill(string server, string user, string pass)
    {
        _server.Text = server;
        _user.Text = user;
        _pass.Password = pass;
    }

    public bool FocusFirstEmpty(bool always = false)
    {
        if (_server.Text.Trim().Length == 0) { _server.Focus(); return true; }
        if (_user.Text.Trim().Length == 0) { _user.Focus(); return true; }
        if (_pass.Password.Length == 0) { _pass.Focus(); return true; }
        if (always) _pass.Focus();
        return false;
    }

    public bool HandleKey(KeyInput k)
    {
        switch (k.Key)
        {
            case Key.Enter:
                // Enter connects as soon as every field is filled; otherwise it jumps to the first empty one.
                if (!FocusFirstEmpty()) Connect();
                return true;
            case Key.Escape:
                if (_onCancel != null) { _onCancel(); return true; }
                return false;
            case Key.Down:
                if (_server.IsKeyboardFocused) { _user.Focus(); return true; }
                if (_user.IsKeyboardFocused) { _pass.Focus(); return true; }
                return false;
            case Key.Up:
                if (_pass.IsKeyboardFocused) { _user.Focus(); return true; }
                if (_user.IsKeyboardFocused) { _server.Focus(); return true; }
                return false;
            default:
                if (Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not PasswordBox) FocusFirstEmpty(always: true);
                return false;
        }
    }

    public async void Connect()
    {
        if (_busy) return;
        _busy = true;
        _error.Visibility = Visibility.Collapsed;
        _spinner.Visibility = Visibility.Visible;
        _connect.Opacity = 0.6;
        var error = await _model.Login(_server.Text, _user.Text, _pass.Password);
        _busy = false;
        _spinner.Visibility = Visibility.Collapsed;
        _connect.Opacity = 1;
        if (error != null)
        {
            _error.Text = error;
            _error.Visibility = Visibility.Visible;
        }
        else
        {
            _onCancel?.Invoke();
        }
    }
}
