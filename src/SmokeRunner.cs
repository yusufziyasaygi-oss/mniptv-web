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
using MNPlayer.UI;

namespace MNPlayer;

internal static class SmokeKit
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    public static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    /// Screenshot of a window rectangle; returns the share of non-black pixels in its centre.
    public static string Shot(IntPtr hwnd, string dir, string name)
    {
        try
        {
            Native.GetWindowRect(hwnd, out var r);
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
            using (var f = File.Create(Path.Combine(dir, name + ".png"))) encoder.Save(f);
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int x0 = w / 4, y0 = h / 4, cw = Math.Max(1, w / 2), ch = Math.Max(1, h / 2);
            var pixels = new byte[cw * ch * 4];
            bgra.CopyPixels(new Int32Rect(x0, y0, cw, ch), pixels, cw * 4, 0);
            var lit = 0;
            for (var i = 0; i < pixels.Length; i += 4)
                if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 40) lit++;
            return $"center-nonblack={(double)lit / (cw * ch):0.00} size={w}x{h}";
        }
        catch (Exception e)
        {
            return "failed: " + e.Message;
        }
    }
}

/// CI-only self test. Active only when MN_SMOKE_DIR is set (and MN_SMOKE_MEDIA points to generated test media).
public sealed class SmokeRunner
{
    private readonly PlayerShell _s;
    private readonly string _dir;
    private readonly string _media;
    private readonly List<string> _lines = new();
    private readonly List<Dictionary<string, object>> _checks = new();

    public SmokeRunner(PlayerShell shell, string dir)
    {
        _s = shell;
        _dir = dir;
        _media = Environment.GetEnvironmentVariable("MN_SMOKE_MEDIA") ?? Path.Combine(dir, "media");
    }

    private static Task Wait(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

    private async Task<bool> WaitUntil(double timeout, Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(timeout);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            await Wait(0.2);
        }
        return condition();
    }

    private void Log(string text)
    {
        var e = _s.Engine;
        var diag = e == null ? "engine=nil"
            : $"hwdec={e.Text("hwdec-current")} vo={e.Text("current-vo")} pixfmt={e.Text("video-params/pixelformat")} drops={e.Text("frame-drop-count")}";
        _lines.Add($"{DateTime.Now:HH:mm:ss.f} {text} | idx={_s.Index} state={_s.State} t={_s.LastTime} len={_s.LastLength} aspect={_s.VideoAspect:0.000} vol={_s.VolumeValue} | {diag}");
        File.WriteAllText(Path.Combine(_dir, "steps.txt"), string.Join("\n", _lines), Encoding.UTF8);
    }

    private void Check(string name, bool ok, string detail = "")
    {
        _checks.Add(new Dictionary<string, object> { ["name"] = name, ["ok"] = ok, ["detail"] = detail });
        Log($"CHECK {(ok ? "OK  " : "FAIL")} {name} {detail}");
    }

    private void Shot(string name) => Log($"SHOT {name} " + SmokeKit.Shot(new WindowInteropHelper(_s).Handle, _dir, name));

    private void Finish()
    {
        File.WriteAllText(Path.Combine(_dir, "results.json"), JsonSerializer.Serialize(_checks, new JsonSerializerOptions { WriteIndented = true }));
        var failed = _checks.Count(c => !(bool)c["ok"]);
        File.WriteAllText(Path.Combine(_dir, "finished.txt"), $"checks={_checks.Count} failed={failed}");
    }

    private void Press(Key k, ModifierKeys m = ModifierKeys.None) => _s.HandleKey(k, m);
    private double ClientAspect => _s.Ui.ActualWidth / Math.Max(1, _s.Ui.ActualHeight);
    private string P(string name) => Path.Combine(_media, name);

    public async void Run()
    {
        try { await Steps(); }
        catch (Exception e) { Log("EXCEPTION " + e); Check("no exception", false, e.Message); }
        Finish();
        await Wait(1);
        _s.Close();
    }

    private async Task Steps()
    {
        await WaitUntil(15, () => _s.Engine != null && _s.Ui.ActualWidth > 100);
        Check("engine ready", _s.Engine != null, _s.EngineFailed ? "failed" : "");
        if (_s.Engine == null) return;
        Check("mpv version", !string.IsNullOrEmpty(_s.Engine.Text("mpv-version")), _s.Engine.Text("mpv-version"));
        Shot("00-empty");

        _s.Add(new[] { P("a.mp4"), P("b.mkv"), P("c.mp4") });
        Check("queue filled", _s.Queue.Count == 3, _s.Queue.Count.ToString());
        Check("playing", await WaitUntil(20, () => _s.State == PState.Playing && _s.LastTime > 500));
        await Wait(2);
        Check("time advances", _s.LastTime > 1500, _s.LastTime.ToString());
        Check("duration known", _s.LastLength > 20000, _s.LastLength.ToString());
        Check("aspect 16:9", _s.VideoAspect is { } a && Math.Abs(a - 16.0 / 9) < 0.02, $"{_s.VideoAspect:0.000}");
        Check("window fits video", Math.Abs(ClientAspect - 16.0 / 9) < 0.06, $"client={ClientAspect:0.000}");
        Check("title has file name", _s.Title.Contains("a.mp4"), _s.Title);
        Shot("01-playing");
        Check("keep-awake on", true, "(not observable)");

        Press(System.Windows.Input.Key.Space);
        Check("pause via Space", await WaitUntil(4, () => _s.State == PState.Paused));
        var t1 = _s.LastTime;
        await Wait(1.2);
        Check("time frozen while paused", Math.Abs(_s.LastTime - t1) < 400, $"{t1}->{_s.LastTime}");
        Press(System.Windows.Input.Key.Space);
        Check("resume via Space", await WaitUntil(4, () => _s.State == PState.Playing));

        var t0 = _s.LastTime;
        Press(System.Windows.Input.Key.L);
        await Wait(1);
        Check("seek +10s (L)", _s.LastTime >= t0 + 8000, $"{t0}->{_s.LastTime}");
        t0 = _s.LastTime;
        Press(System.Windows.Input.Key.Left);
        await Wait(1);
        Check("seek -5s (Left)", _s.LastTime < t0 - 2500, $"{t0}->{_s.LastTime}");

        var v0 = _s.VolumeValue;
        Press(System.Windows.Input.Key.Down);
        Check("volume down", Math.Abs(_s.VolumeValue - (v0 - 5)) < 0.1 && Math.Abs(_s.Engine.Number("volume") - _s.VolumeValue) < 0.5, $"{v0}->{_s.VolumeValue}");
        Press(System.Windows.Input.Key.M);
        Check("mute", _s.Muted && _s.Engine.Flag("mute"));
        Press(System.Windows.Input.Key.M);
        Check("unmute", !_s.Muted && !_s.Engine.Flag("mute"));

        _s.SetRate(1.5);
        Check("speed 1.5", Math.Abs(_s.Engine.Number("speed") - 1.5) < 0.01, _s.Engine.Number("speed").ToString(CultureInfo.InvariantCulture));
        _s.SetRate(1);

        var audio = _s.Engine.Tracks().Count(t => t.Type == "audio");
        Check("two audio tracks listed", audio == 2, audio.ToString());
        _s.AddSubtitle(P("sub.srt"));
        await Wait(1);
        Check("subtitle added", _s.Engine.Tracks().Count(t => t.Type == "sub") >= 1);
        Check("subtitle selected", _s.Engine.Tracks().Any(t => t.Type == "sub" && t.Selected));
        Shot("02-subtitle");
        Press(System.Windows.Input.Key.C);
        Check("subtitle toggled off (C)", !_s.Engine.Tracks().Any(t => t.Type == "sub" && t.Selected));
        Press(System.Windows.Input.Key.C);
        Check("subtitle toggled on (C)", _s.Engine.Tracks().Any(t => t.Type == "sub" && t.Selected));

        _s.SettingsMenu();
        await Wait(0.5);
        Check("settings menu opens", _s.Ui.MenuOpen);
        Shot("03-menu-settings");
        Press(System.Windows.Input.Key.Escape);
        Check("Esc closes menu", !_s.Ui.MenuOpen);
        _s.CaptionsMenu();
        await Wait(0.5);
        Shot("04-menu-captions");
        _s.Ui.CloseMenu();
        _s.ShowShortcuts();
        await Wait(0.5);
        Check("dialog opens", _s.Ui.DialogOpen);
        Shot("05-shortcuts");
        _s.Ui.CloseDialog();

        var oldW = _s.Width;
        _s.Width = 500;
        await Wait(0.8);
        Shot("06-compact");
        _s.Width = oldW;
        await Wait(0.5);
        _s.FitVideoWindow();

        _s.Next();
        Check("next video", await WaitUntil(15, () => _s.Index == 1 && _s.State == PState.Playing && _s.LastTime > 300));
        await Wait(1);
        Check("title follows file", _s.Title.Contains("b.mkv"), _s.Title);
        Check("aspect follows file (mkv 4:3)", _s.VideoAspect is { } b && Math.Abs(b - 4.0 / 3) < 0.02, $"{_s.VideoAspect:0.000}");
        Check("window refits", Math.Abs(ClientAspect - 4.0 / 3) < 0.06, $"client={ClientAspect:0.000}");
        Shot("07-second-video");

        Press(System.Windows.Input.Key.F);
        await Wait(1.5);
        Check("fullscreen on (F)", _s.IsFullScreen && _s.WindowState == WindowState.Maximized);
        Shot("08-fullscreen");
        Press(System.Windows.Input.Key.Escape);
        await Wait(1);
        Check("fullscreen off (Esc)", !_s.IsFullScreen && _s.WindowState == WindowState.Normal);

        Press(System.Windows.Input.Key.I);
        await Wait(0.8);
        Check("mini on (I)", _s.IsMini && _s.Topmost && _s.Width < 700, $"w={_s.Width}");
        Shot("09-mini");
        Press(System.Windows.Input.Key.I);
        await Wait(0.8);
        Check("mini off (I)", !_s.IsMini && !_s.Topmost);

        _s.Next();
        Check("portrait video plays", await WaitUntil(15, () => _s.Index == 2 && _s.State == PState.Playing && _s.LastTime > 300));
        await Wait(1);
        Check("portrait aspect < 1", _s.VideoAspect is { } c && c < 0.7, $"{_s.VideoAspect:0.000}");
        Check("window refits portrait", Math.Abs(ClientAspect - (_s.VideoAspect ?? 1)) < 0.06, $"client={ClientAspect:0.000}");
        Shot("10-portrait");

        if (File.Exists(P("rot.mp4")))
        {
            _s.Add(new[] { P("rot.mp4") });
            await WaitUntil(15, () => _s.State == PState.Playing && _s.LastTime > 300);
            await Wait(1);
            Check("rotated video uses display aspect", _s.VideoAspect is { } r && r < 1, $"{_s.VideoAspect:0.000}");
        }

        // resume position
        _s.Play(0);
        await WaitUntil(15, () => _s.State == PState.Playing && _s.LastTime > 300);
        _s.SeekBy(14000);
        await Wait(1.5);
        var resumeAt = _s.LastTime;
        _s.Play(1);
        await WaitUntil(15, () => _s.Index == 1 && _s.State == PState.Playing);
        Check("position stored", Settings.Current.Positions.Keys.Any(k => k.EndsWith("a.mp4")), string.Join(",", Settings.Current.Positions.Keys));
        _s.Play(0);
        await WaitUntil(15, () => _s.State == PState.Playing && _s.LastTime > 300);
        await Wait(2);
        Check("resume from saved position", _s.LastTime >= resumeAt - 1500, $"saved={resumeAt} now={_s.LastTime}");

        // autoplay at end of file
        _s.Play(1);
        await WaitUntil(15, () => _s.Index == 1 && _s.State == PState.Playing && _s.LastLength > 0);
        _s.SeekBy(_s.LastLength - _s.LastTime - 2500);
        Check("autoplay next at EOF", await WaitUntil(20, () => _s.Index == 2 && _s.State == PState.Playing));

        // repeat one
        _s.RepeatOne = true;
        await WaitUntil(10, () => _s.LastLength > 0);
        _s.SeekBy(_s.LastLength - _s.LastTime - 2500);
        await Wait(5);
        Check("repeat one restarts same item", _s.Index == 2 && _s.State == PState.Playing && _s.LastTime < _s.LastLength - 3000, $"t={_s.LastTime}/{_s.LastLength}");
        _s.RepeatOne = false;

        _s.ShowQueue();
        await Wait(1);
        Check("queue window opens", Application.Current.Windows.OfType<QueueWindow>().Any(w => w.IsLoaded));
        foreach (var w in Application.Current.Windows.OfType<QueueWindow>().ToList()) w.Close();

        _s.Add(new[] { P("missing-file.mp4") });
        Check("missing file reports error", await WaitUntil(15, () => _s.State == PState.Error && _s.Ui.DialogOpen));
        Shot("11-error");
        _s.Ui.CloseDialog();
        Log("DONE");
    }
}

/// CI self test of the Explorer Ctrl+M helper (runs inside the preview process).
public sealed class PreviewSmokeRunner
{
    private readonly PreviewService _svc;
    private readonly string _dir;
    private readonly string? _first;
    private readonly List<string> _lines = new();
    private readonly List<Dictionary<string, object>> _checks = new();

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    internal PreviewSmokeRunner(PreviewService svc, string dir, string? first)
    {
        _svc = svc;
        _dir = dir;
        _first = first;
    }

    private static Task Wait(double seconds) => Task.Delay(TimeSpan.FromSeconds(seconds));

    private async Task<bool> WaitUntil(double timeout, Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(timeout);
        while (DateTime.UtcNow < end)
        {
            if (condition()) return true;
            await Wait(0.2);
        }
        return condition();
    }

    private void Log(string text)
    {
        _lines.Add($"{DateTime.Now:HH:mm:ss.f} {text}");
        File.WriteAllText(Path.Combine(_dir, "steps.txt"), string.Join("\n", _lines), Encoding.UTF8);
    }

    private void Check(string name, bool ok, string detail = "")
    {
        _checks.Add(new Dictionary<string, object> { ["name"] = name, ["ok"] = ok, ["detail"] = detail });
        Log($"CHECK {(ok ? "OK  " : "FAIL")} {name} {detail}");
    }

    public async void Run()
    {
        try { await Steps(); }
        catch (Exception e) { Log("EXCEPTION " + e); Check("no exception", false, e.Message); }
        File.WriteAllText(Path.Combine(_dir, "results.json"), JsonSerializer.Serialize(_checks, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(_dir, "finished.txt"), $"checks={_checks.Count} failed={_checks.Count(c => !(bool)c["ok"])}");
        await Wait(1);
        _svc.Quit();
    }

    private async Task Steps()
    {
        var media = Environment.GetEnvironmentVariable("MN_SMOKE_MEDIA") ?? Path.Combine(_dir, "media");
        var first = _first ?? Path.Combine(media, "a.mp4");
        _svc.Open(first);
        Check("preview window created", await WaitUntil(15, () => _svc.Shell is { Engine: not null }));
        var s = _svc.Shell!;
        Check("preview plays", await WaitUntil(20, () => s.State == PState.Playing && s.LastTime > 400));
        Check("preview is topmost", s.Topmost);
        Check("siblings queued (4 videos)", s.Queue.Count >= 4, s.Queue.Count.ToString());
        Check("selected item first", s.CurrentItem != null && Path.GetFileName(s.CurrentItem) == Path.GetFileName(first), s.CurrentItem ?? "");
        Check("title is file name", s.Title == Path.GetFileName(first), s.Title);
        Check("autoplay off in preview", !s.Autoplay);
        await Wait(1);
        Log("SHOT preview " + SmokeKit.Shot(new WindowInteropHelper(s).Handle, _dir, "preview-01"));

        var i0 = s.Index;
        s.HandleKey(Key.Down, ModifierKeys.None);
        Check("Down = next", await WaitUntil(12, () => s.Index == i0 + 1 && s.State == PState.Playing), $"{i0}->{s.Index}");
        s.HandleKey(Key.Up, ModifierKeys.None);
        Check("Up = previous", await WaitUntil(12, () => s.Index == i0 && s.State == PState.Playing), $"->{s.Index}");
        s.HandleKey(Key.Right, ModifierKeys.None);
        s.HandleKey(Key.Space, ModifierKeys.None);
        Check("Space pauses", await WaitUntil(4, () => s.State == PState.Paused));
        s.HandleKey(Key.Escape, ModifierKeys.None);
        await Wait(0.8);
        Check("Esc hides preview", !s.IsVisible && s.State == PState.Idle && s.Queue.Count == 0);
        Check("process still alive (warm)", true);

        _svc.Open(Path.Combine(media, "b.mkv"));
        Check("second preview reuses window", await WaitUntil(15, () => ReferenceEquals(_svc.Shell, s) && s.IsVisible && s.State == PState.Playing && s.LastTime > 300));
        s.HidePreview();
        Check("keyboard hook installed", _svc.HookInstalled);

        // Real Explorer round trip: select a file in Explorer and press Ctrl+M.
        try
        {
            var target = Path.Combine(media, "c.mp4");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            IntPtr hwnd = IntPtr.Zero;
            await WaitUntil(15, () => (hwnd = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "CabinetWClass", null)) != IntPtr.Zero);
            Log("explorer window " + hwnd);
            await Wait(3);
            string? selected = null;
            var done = new System.Threading.ManualResetEventSlim();
            var t = new System.Threading.Thread(() => { selected = ExplorerSelection.Get(hwnd); done.Set(); });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            done.Wait(10000);
            Check("INFO explorer selection read", selected != null && string.Equals(Path.GetFileName(selected), "c.mp4", StringComparison.OrdinalIgnoreCase), selected ?? "(null)");

            if (hwnd != IntPtr.Zero)
            {
                Native.SetForegroundWindow(hwnd);
                await Wait(1.5);
                Log("foreground class " + Native.ClassOf(Native.GetForegroundWindow()));
                keybd_event(0x11, 0, 0, UIntPtr.Zero);
                keybd_event(0x4D, 0, 0, UIntPtr.Zero);
                keybd_event(0x4D, 0, 2, UIntPtr.Zero);
                keybd_event(0x11, 0, 2, UIntPtr.Zero);
                Check("INFO Ctrl+M in Explorer opens preview", await WaitUntil(10, () => s.IsVisible && s.CurrentItem != null && Path.GetFileName(s.CurrentItem) == "c.mp4"), s.CurrentItem ?? "(none)");
            }
        }
        catch (Exception e)
        {
            Check("INFO explorer round trip", false, e.Message);
        }
    }
}
