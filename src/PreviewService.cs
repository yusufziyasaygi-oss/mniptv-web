using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MNPlayer.UI;

namespace MNPlayer;

internal static class PreviewAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MNPlayerPreview";

    public static bool Enabled
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(ValueName) != null; }
            catch (Exception) { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            var exe = Environment.ProcessPath!;
            if (on)
            {
                k.SetValue(ValueName, $"\"{exe}\" --preview-service");
                if (!Ipc.Send(Ipc.PreviewPipe, "PING", 150))
                    Process.Start(new ProcessStartInfo(exe, "--preview-service") { UseShellExecute = false });
            }
            else
            {
                k.DeleteValue(ValueName, false);
                Ipc.Send(Ipc.PreviewPipe, "QUIT", 150);
            }
        }
        catch (Exception) { }
    }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProviderCom
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? obj);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowserCom
{
    [PreserveSig] int GetWindow(out IntPtr hwnd);
}

/// Reads the selected file of the active Explorer window (all of it best effort, never throws).
internal static class ExplorerSelection
{
    public static string Debug = "";

    public static string? Get(IntPtr foreground)
    {
        try
        {
            Debug = "start;";
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            var active = ActiveTab(foreground);
            string? fallback = null;
            foreach (dynamic w in shell.Windows())
            {
                try
                {
                    Debug += $" win={(long)w.HWND}/{foreground.ToInt64()};";
                    if ((long)w.HWND != foreground.ToInt64()) continue;
                    dynamic selection = w.Document.SelectedItems();
                    Debug += $" sel={(int)selection.Count};";
                    try { Debug += " folder=" + (string)w.Document.Folder.Self.Path + " items=" + (int)w.Document.Folder.Items().Count + ";"; } catch (Exception e) { Debug += " folderErr:" + e.Message; }
                    try { Debug += " focus=" + (string)w.Document.FocusedItem.Path + ";"; } catch (Exception e) { Debug += " focusErr:" + e.Message; }
                    if ((int)selection.Count < 1) continue;
                    string path = selection.Item(0).Path;
                    if (active != IntPtr.Zero && TabOf((object)w) == active) return path;
                    fallback ??= path;
                }
                catch (Exception e) { Debug += " inner:" + e.Message; }
            }
            return fallback;
        }
        catch (Exception e)
        {
            Debug += " outer:" + e;
            return null;
        }
    }

    private static IntPtr ActiveTab(IntPtr foreground)
    {
        var child = IntPtr.Zero;
        for (var i = 0; i < 32; i++)
        {
            child = Native.FindWindowEx(foreground, child, "ShellTabWindowClass", null);
            if (child == IntPtr.Zero) return IntPtr.Zero;
            if (Native.IsWindowVisible(child)) return child;
        }
        return IntPtr.Zero;
    }

    private static IntPtr TabOf(object window)
    {
        try
        {
            var sp = (IServiceProviderCom)window;
            var sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
            var iid = new Guid("000214E2-0000-0000-C000-000000000046");
            if (sp.QueryService(ref sid, ref iid, out var browser) != 0 || browser == null) return IntPtr.Zero;
            return ((IShellBrowserCom)browser).GetWindow(out var hwnd) == 0 ? hwnd : IntPtr.Zero;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}

/// Background helper: Ctrl+M in Explorer opens the selected video in a floating preview window.
internal sealed class PreviewService
{
    private PlayerShell? _shell;
    private System.Windows.Forms.NotifyIcon? _tray;
    private IntPtr _hook;
    private Native.HookProc? _proc;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMinutes(10) };
    private DateTime _lastKey = DateTime.MinValue;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public PlayerShell? Shell => _shell;
    public bool HookInstalled => _hook != IntPtr.Zero;

    public void Start(bool withHook)
    {
        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_shell is { } s && !s.IsVisible) { s.AllowCloseNow = true; s.Close(); _shell = null; }
        };
        try
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("MN Player'ı aç", null, (_, _) => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }));
            menu.Items.Add("Önizleme: Explorer'da Ctrl+M").Enabled = false;
            menu.Items.Add("Çık", null, (_, _) => Quit());
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                Text = "MN Player Önizleme (Ctrl+M)",
                Visible = true,
                ContextMenuStrip = menu,
            };
        }
        catch (Exception) { }

        if (withHook)
        {
            _proc = HookCallback;
            _hook = Native.SetWindowsHookEx(13, _proc, Native.GetModuleHandle(null), 0);
        }
        Ipc.Serve(Ipc.PreviewPipe, message =>
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (message == "QUIT") Quit();
                else if (message != "PING" && File.Exists(message)) Open(message);
            }));
        });
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var msg = (int)wParam;
            var vk = Marshal.ReadInt32(lParam);
            if (vk == 0x4D && (msg == 0x100 || msg == 0x101)
                && (Native.GetAsyncKeyState(0x11) & 0x8000) != 0
                && (Native.GetAsyncKeyState(0x10) & 0x8000) == 0
                && (Native.GetAsyncKeyState(0x12) & 0x8000) == 0)
            {
                var fg = Native.GetForegroundWindow();
                var cls = Native.ClassOf(fg);
                if (cls == "CabinetWClass" || cls == "ExploreWClass")
                {
                    if (msg == 0x100 && (DateTime.UtcNow - _lastKey).TotalMilliseconds > 500)
                    {
                        _lastKey = DateTime.UtcNow;
                        ResolveSelection(fg);
                    }
                    return (IntPtr)1;
                }
            }
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void ResolveSelection(IntPtr foreground)
    {
        // Shell COM is slow; never block the keyboard hook with it.
        var thread = new Thread(() =>
        {
            var path = ExplorerSelection.Get(foreground);
            if (path != null && PlayerShell.IsVideoFile(path) && File.Exists(path))
                _ui.BeginInvoke(new Action(() => Open(path, foreground)));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public void Open(string path, IntPtr? foreground = null)
    {
        var previous = foreground ?? Native.GetForegroundWindow();
        // Do not keep playing (and talking) behind the preview.
        Ipc.Send(Ipc.MainPipe, "PAUSE", 150);
        _idle.Stop();
        if (_shell == null)
        {
            _shell = new PlayerShell(true);
            _shell.PreviewHidden += () => { _idle.Stop(); _idle.Start(); };
        }
        _shell.OpenPreview(path, previous);
    }

    public void Quit()
    {
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Application.Current.Shutdown();
        Environment.Exit(0);
    }
}
