using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MNIPTV.UI;

/// Native child window that mpv renders into. It never takes keyboard focus.
public sealed class VideoHost : HwndHost
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000, WsClipSiblings = 0x04000000;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021, MaNoActivate = 3, WmEraseBkgnd = 0x0014;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);

    private const string ClassName = "MNIPTVVideo";
    private static bool _registered;
    private static readonly WndProcDelegate Proc = WndProc;

    public IntPtr VideoHandle { get; private set; }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmMouseActivate) return (IntPtr)MaNoActivate;
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var instance = GetModuleHandle(null);
        if (!_registered)
        {
            var wc = new WndClassEx
            {
                cbSize = Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                hInstance = instance,
                hbrBackground = GetStockObject(4), // BLACK_BRUSH
                hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512),
                lpszClassName = ClassName,
            };
            RegisterClassEx(ref wc);
            _registered = true;
        }
        VideoHandle = CreateWindowEx(WsExNoActivate, ClassName, "", WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            0, 0, 64, 64, parent.Handle, IntPtr.Zero, instance, IntPtr.Zero);
        return new HandleRef(this, VideoHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        VideoHandle = IntPtr.Zero;
    }
}
