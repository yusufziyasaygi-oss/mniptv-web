using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MNIPTV.UI;

namespace MNIPTV;

/// CI-only self test. Active only when MN_SMOKE_DIR is set; drives the real UI with keyboard
/// events against ci/mock_xtream.py and records screenshots and checks.
public sealed class SmokeRunner
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private readonly string _dir;
    private readonly List<string> _lines = new();
    private readonly List<Dictionary<string, object>> _checks = new();

    public SmokeRunner(AppModel model, MainWindow window, string dir)
    {
        _model = model;
        _window = window;
        _dir = dir;
    }

    private static Task Wait(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

    private async Task<bool> WaitUntil(double timeout, Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(timeout);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            await Wait(0.25);
        }
        return condition();
    }

    private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    private void Log(string text)
    {
        var p = _model.ActivePlayer;
        var state = p == null ? "-" : $"pos={F(p.Position)} dur={F(p.Duration)} h={p.VideoHeight} status={p.Status} cur={p.Current} msg={p.Message ?? ""} fail={p.FailureText}";
        var e = PlaybackEngine.Peek;
        var diag = e == null
            ? "engine=nil " + (PlaybackEngine.Failure ?? "")
            : $"hwdec={e.Text("hwdec-current")} vo={e.Text("current-vo")} gpu={e.Text("gpu-api")}/{e.Text("gpu-context")} pixfmt={e.Text("video-params/pixelformat")} out={e.Text("video-out-params/w")}x{e.Text("video-out-params/h")} drops={e.Text("frame-drop-count")} container={e.Text("file-format")}";
        var line = $"{DateTime.Now:HH:mm:ss.f} {text} | section={_model.Section.Title()} route={_model.RouteStack.Count} tiles={_model.Tiles.Count} | {state} | {diag}";
        _lines.Add(line);
        File.WriteAllText(Path.Combine(_dir, "steps.txt"), string.Join("\n", _lines), Encoding.UTF8);
    }

    private void Check(string name, bool ok, string detail = "")
    {
        _checks.Add(new Dictionary<string, object> { ["name"] = name, ["ok"] = ok, ["detail"] = detail });
        Log($"CHECK {(ok ? "OK  " : "FAIL")} {name} {detail}");
    }

    private void Finish()
    {
        File.WriteAllText(Path.Combine(_dir, "results.json"), JsonSerializer.Serialize(_checks, new JsonSerializerOptions { WriteIndented = true }));
        var failed = _checks.Count(c => !(bool)c["ok"]);
        File.WriteAllText(Path.Combine(_dir, "finished.txt"), $"checks={_checks.Count} failed={failed}");
    }

    // input

    private UIElement Target => _model.RouteStack.LastOrDefault() is PlayerRoute && _window.Overlay.IsVisible ? _window.Overlay : _window;

    private async Task Press(Key key)
    {
        var target = Target;
        var source = PresentationSource.FromVisual(target);
        if (source != null)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            target.RaiseEvent(args);
        }
        await Wait(0.6);
    }

    private Task Up() => Press(System.Windows.Input.Key.Up);
    private Task Down() => Press(System.Windows.Input.Key.Down);
    private Task Left() => Press(System.Windows.Input.Key.Left);
    private Task Right() => Press(System.Windows.Input.Key.Right);
    private Task Enter() => Press(System.Windows.Input.Key.Enter);
    private Task Escape() => Press(System.Windows.Input.Key.Escape);
    private Task Space() => Press(System.Windows.Input.Key.Space);

    private async Task ToSidebar()
    {
        for (var i = 0; i < 4; i++) await Left();
    }

    // screenshots

    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect32 r);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    private void Shot(string name)
    {
        try
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            GetWindowRect(hwnd, out var r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            var screen = GetDC(IntPtr.Zero);
            var mem = CreateCompatibleDC(screen);
            var bmp = CreateCompatibleBitmap(screen, w, h);
            var old = SelectObject(mem, bmp);
            BitBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, 0x00CC0020 | 0x40000000);
            SelectObject(mem, old);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var f = File.Create(Path.Combine(_dir, name + ".png"))) encoder.Save(f);

            // How much of the central area is not black: tells whether video is visible.
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int x0 = w / 4, y0 = h / 4, cw = w / 2, ch = h / 2;
            var pixels = new byte[cw * ch * 4];
            bgra.CopyPixels(new Int32Rect(x0, y0, cw, ch), pixels, cw * 4, 0);
            var lit = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 40) lit++;
            Log($"SHOT {name} center-nonblack={(double)lit / (cw * ch):0.00}");
        }
        catch (Exception e)
        {
            Log($"SHOT {name} failed: {e.Message}");
        }
    }

    // run

    public async void Run()
    {
        try
        {
            await RunSteps();
        }
        catch (Exception e)
        {
            Log("EXCEPTION " + e);
            Finish();
        }
        await Wait(1);
        _window.Close();
    }

    private async Task RunSteps()
    {
        var server = Environment.GetEnvironmentVariable("MN_SMOKE_SERVER") ?? "http://127.0.0.1:8080";
        await Wait(1.5);
        var area = SystemParameters.WorkArea;
        _window.Left = area.Left;
        _window.Top = area.Top;
        _window.Width = Math.Min(1440, area.Width);
        _window.Height = Math.Min(900, area.Height);
        _window.Activate();
        await Wait(1);

        // 1. Login with the keyboard
        Shot("01-login");
        _window.Login?.Fill(server, "u", "p");
        await Wait(0.5);
        Shot("02-login-filled");
        await Enter();
        var loggedIn = await WaitUntil(15, () => _model.Account != null);
        Check("login", loggedIn);
        if (!loggedIn) { Finish(); return; }
        var loaded = await WaitUntil(15, () => _model.Tiles.Count > 0);
        await Wait(1.5);
        Shot("03-home-live");
        Check("live categories loaded", _model.Categories.Count == 3, $"categories={_model.Categories.Count}");
        Check("live channels loaded", loaded && _model.Tiles.Count == 15, $"tiles={_model.Tiles.Count}");

        // 2. Keyboard navigation: sidebar -> categories -> grid, open a channel
        await Right(); await Right(); await Down(); await Right();
        Shot("04-grid-focus");
        await Enter();
        var livePlayer = await WaitUntil(10, () => _model.ActivePlayer != null && _model.RouteStack.Count == 1);
        Check("live player opened", livePlayer);
        var liveStarted = await WaitUntil(25, () => (_model.ActivePlayer?.Position ?? 0) > 1);
        Check("live stream plays", liveStarted, $"pos={F(_model.ActivePlayer?.Position ?? 0)} h={_model.ActivePlayer?.VideoHeight ?? 0}");
        var c0 = _model.ActivePlayer?.Current ?? -1;
        Check("opened the focused tile (not the first)", c0 > 0, $"current={c0}");
        await Wait(1);
        Shot("05-live-playing");

        await Down();
        await Wait(5);
        Check("zap down -> next channel", _model.ActivePlayer?.Current == c0 + 1, $"current={_model.ActivePlayer?.Current ?? -1}");
        Shot("06-live-zap");
        await Left();
        await Wait(4);
        Check("left -> previous channel", _model.ActivePlayer?.Current == c0, $"current={_model.ActivePlayer?.Current ?? -1}");
        await Enter();
        await Wait(1);
        Shot("07-live-list");
        await Down(); await Down(); await Enter();
        await Wait(4);
        Check("channel list pick", _model.ActivePlayer?.Current == c0 + 2, $"current={_model.ActivePlayer?.Current ?? -1}");
        await Press(System.Windows.Input.Key.D1); await Press(System.Windows.Input.Key.D2);
        await Wait(3);
        var ch = _model.ActivePlayer?.CurrentChannel;
        Check("number keys 1-2 -> channel 12", ch?.Number == 12, $"number={ch?.Number ?? -1}");
        Shot("08-live-number");
        await Escape();
        await Wait(1.5);
        Check("esc leaves live player", _model.RouteStack.Count == 0);
        Shot("09-back-home");

        // 3. Movies via sidebar keyboard
        await ToSidebar();
        await Down();
        var moviesLoaded = await WaitUntil(10, () => _model.Section == Section.Movies && _model.Tiles.Count > 0);
        Check("movies section", moviesLoaded, $"tiles={_model.Tiles.Count}");
        await Wait(1);
        await Right(); await Right();
        Shot("10-movies-grid");
        await Enter();
        var movieStarted = await WaitUntil(25, () => (_model.ActivePlayer?.Position ?? 0) > 18);
        Check("movie plays past 18s", movieStarted, $"pos={F(_model.ActivePlayer?.Position ?? 0)}");
        Shot("11-movie-playing");
        var before = _model.ActivePlayer?.Position ?? 0;
        await Right(); await Right();
        await Wait(2);
        var after = _model.ActivePlayer?.Position ?? 0;
        Check("right arrow seeks +20s", after - before >= 17, $"before={F(before)} after={F(after)}");
        Shot("12-movie-seek");
        await Space();
        await Wait(1);
        Check("space pauses", _model.ActivePlayer?.Status == PlayStatus.Paused, $"status={_model.ActivePlayer?.Status}");
        Shot("13-movie-paused");
        await Space();
        await Wait(1);
        await Escape();
        await Wait(1.5);
        var saved = _model.Store.GetProgress("MOVIE:2001");
        Check("movie progress saved", saved != null && saved.Position > 15, $"pos={F(saved?.Position ?? 0)}");

        // 4. Continue watching
        await ToSidebar();
        await Up(); await Up();
        await Wait(1);
        Check("continue watching shows movie", _model.Section == Section.ContinueWatching && _model.Tiles.Count == 1, $"tiles={_model.Tiles.Count}");
        Shot("14-continue");
        await Right();
        await Enter();
        var resumed = await WaitUntil(15, () => (_model.ActivePlayer?.Position ?? 0) >= (saved?.Position ?? 0) - 3);
        Check("resume from saved position", resumed, $"pos={F(_model.ActivePlayer?.Position ?? 0)} saved={F(saved?.Position ?? 0)}");
        await Escape();
        await Wait(1.5);

        // 5. Series
        await ToSidebar();
        await Down(); await Down(); await Down();
        var seriesLoaded = await WaitUntil(10, () => _model.Section == Section.Series && _model.Tiles.Count > 0);
        Check("series section", seriesLoaded, $"tiles={_model.Tiles.Count}");
        await Wait(1);
        await Right(); await Right(); await Enter();
        await Wait(3);
        Shot("15-series-detail");
        Check("series screen open", _model.RouteStack.LastOrDefault() is SeriesRoute);
        await Enter();
        var episodeStarted = await WaitUntil(25, () => (_model.ActivePlayer?.Position ?? 0) > 18);
        Check("episode plays", episodeStarted, $"pos={F(_model.ActivePlayer?.Position ?? 0)}");
        Shot("16-episode-playing");
        await Escape();
        await Wait(2);
        var sp = _model.Store.GetProgress("SERIES:3001");
        Check("series progress saved (one card)", sp != null && sp.EpisodeId == "4101", $"ep={sp?.EpisodeId ?? "-"} pos={F(sp?.Position ?? 0)}");
        Shot("17-series-resume");
        await Down();
        await Right();
        await Wait(1);
        Shot("18-series-season-2");
        await Escape();
        await Wait(1);

        // 6. Series without episodes must not crash
        _model.Push(new SeriesRoute(new MediaEntry { Kind = Kind.Series, Id = "3008", Name = "Dizi 8" }));
        await Wait(3);
        Shot("19-series-empty");
        await Escape();
        await Wait(1);

        // 7. Favourite with F, then favourites section
        _model.GoToSection(Section.Movies);
        await WaitUntil(10, () => _model.Tiles.Count > 0);
        await Wait(0.5);
        await ToSidebar();
        await Right(); await Right();
        await Press(System.Windows.Input.Key.F);
        await Wait(1);
        Check("F adds favourite", _model.Store.Favorites.Any(f => f.Key == "MOVIE:2001"));
        _model.GoToSection(Section.Favorites);
        await Wait(1);
        Shot("20-favorites");
        Check("favourites list", _model.Tiles.Count >= 1, $"tiles={_model.Tiles.Count}");

        // 8. Search
        _model.GoToSection(Section.Search);
        await Wait(1);
        if (_window.Content is FrameworkElement)
        {
            var browse = FindBrowse(_window);
            browse?.TypeSearch("kanal 1");
        }
        var searched = await WaitUntil(10, () => _model.Tiles.Count == 7);
        Check("search 'kanal 1'", searched, $"tiles={_model.Tiles.Count}");
        await Wait(1);
        Shot("21-search");

        // 9. Account menu opens and closes with the keyboard
        await Escape();
        for (var i = 0; i < 8; i++) await Down();
        await Enter();
        await Wait(0.5);
        Shot("22-account-menu");
        Check("account menu opens", _window.DialogOpen);
        await Escape();
        await Wait(0.5);
        Check("account menu closes", !_window.DialogOpen && _model.Account != null);

        Finish();
    }

    private static BrowseView? FindBrowse(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is BrowseView b) return b;
            var found = FindBrowse(c);
            if (found != null) return found;
        }
        return null;
    }
}
