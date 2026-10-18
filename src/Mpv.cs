using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace MNPlayer;

internal static class MpvNative
{
    private const string Lib = "libmpv-2.dll";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_create();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_initialize(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_terminate_destroy(IntPtr ctx);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command(IntPtr ctx, IntPtr[] args);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_command_async(IntPtr ctx, ulong replyUserdata, IntPtr[] args);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_set_property(IntPtr ctx, byte[] name, int format, ref double data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_get_property(IntPtr ctx, byte[] name, int format, out double data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int mpv_get_property(IntPtr ctx, byte[] name, int format, out int data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void mpv_free(IntPtr data);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr mpv_error_string(int error);

    public const int FormatString = 1, FormatFlag = 3, FormatDouble = 5;
    public const int EventNone = 0, EventCommandReply = 5, EventEndFile = 7, EventFileLoaded = 8;
    public const int EndEof = 0, EndError = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EndFile
    {
        public int Reason;
        public int Error;
    }

    public static byte[] U8(string s)
    {
        var b = System.Text.Encoding.UTF8.GetBytes(s);
        var r = new byte[b.Length + 1];
        Buffer.BlockCopy(b, 0, r, 0, b.Length);
        return r;
    }
}

public enum MpvEventKind { Loaded, Failed, Ended }

public readonly record struct MpvEvent(MpvEventKind Kind, int Code);

public readonly record struct MpvTrack(string Type, int Id, bool Selected, string Title, string Lang, int Channels);

/// One libmpv instance drawing into a native child window. Used from the UI thread.
public sealed class MpvEngine
{
    private IntPtr _handle;

    public bool Ready => _handle != IntPtr.Zero;
    public string Error { get; private set; } = "";

    public MpvEngine(IntPtr wid, string? caFile, bool headless = false)
    {
        string stage = "create";
        try
        {
            _handle = MpvNative.mpv_create();
            if (_handle == IntPtr.Zero) { Error = "mpv_create"; return; }
            var options = new List<(string, string)>
            {
                                ("config", "no"), ("load-scripts", "no"), ("terminal", "no"), ("msg-level", "all=no"),
                ("hwdec", "auto-safe"), ("idle", "yes"), ("keep-open", "no"), ("force-window", "no"),
                ("input-default-bindings", "no"), ("input-vo-keyboard", "no"), ("input-cursor", "no"), ("cursor-autohide", "no"),
                ("osc", "no"), ("osd-level", "0"), ("save-position-on-quit", "no"), ("audio-file-auto", "no"), ("ytdl", "no"), ("network-timeout", "15"), ("cache", "yes"),
                ("cache-on-disk", "no"), ("cache-secs", "120"), ("demuxer-max-bytes", "256MiB"),
                ("demuxer-max-back-bytes", "512MiB"), ("demuxer-readahead-secs", "60"), ("audio-channels", "auto-safe"),
                ("user-agent", "MNPlayer/2.2.4"), ("background-color", "#000000"),
                ("volume-max", "100"), ("sub-auto", "fuzzy"), ("sub-font", "Segoe UI"), ("audio-display", "no"), ("cursor-autohide", "no"),
            };
            if (headless) { options.Add(("vo", "null")); options.Add(("ao", "null")); }
            else options.Add(("wid", ((long)wid).ToString(CultureInfo.InvariantCulture)));
            var hw = Environment.GetEnvironmentVariable("MN_HWDEC");
            if (!string.IsNullOrEmpty(hw)) options.Add(("hwdec", hw));
            if (caFile != null && File.Exists(caFile)) { options.Add(("tls-ca-file", caFile)); options.Add(("tls-verify", "yes")); }
            foreach (var (k, v) in options)
            {
                stage = "option-" + k;
                var r = MpvNative.mpv_set_option_string(_handle, MpvNative.U8(k), MpvNative.U8(v));
                // Unknown optional settings must not stop playback on a different mpv build.
                if (r < 0 && (k == "wid" || k == "idle")) { Fail(stage, r); return; }
            }
            stage = "initialize";
            var init = MpvNative.mpv_initialize(_handle);
            if (init < 0) { Fail(stage, init); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = $"{stage}: {e.Message}";
            _handle = IntPtr.Zero;
        }
    }

    private void Fail(string stage, int code)
    {
        Error = $"{stage} {ErrorText(code)}";
        if (_handle != IntPtr.Zero) MpvNative.mpv_terminate_destroy(_handle);
        _handle = IntPtr.Zero;
    }

    public static string ErrorText(int code)
    {
        try { return Marshal.PtrToStringUTF8(MpvNative.mpv_error_string(code)) ?? code.ToString(); }
        catch (Exception) { return code.ToString(); }
    }

    private int Command(bool async, ulong userdata, params string[] args)
    {
        if (!Ready) return -1;
        var ptrs = new IntPtr[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return async ? MpvNative.mpv_command_async(_handle, userdata, ptrs) : MpvNative.mpv_command(_handle, ptrs);
        }
        finally
        {
            foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
        }
    }

    /// Runs a command and waits for the result (short commands only).
    public int Cmd(params string[] args) => Command(false, 0, args);
    public int CmdAsync(params string[] args) => Command(true, 0, args);

    // The argv form avoids command-string parsing, so URL credentials are passed untouched.
    public int Load(string url) => Command(true, 1, "loadfile", url, "replace");
    public int Stop() => Command(true, 0, "stop");

    public int Seek(double seconds)
    {
        if (!double.IsFinite(seconds)) return -1;
        return Command(true, 0, "seek", seconds.ToString("0.000", CultureInfo.InvariantCulture), "absolute+exact");
    }

    public List<MpvEvent> DrainEvents()
    {
        var result = new List<MpvEvent>();
        if (!Ready) return result;
        for (var i = 0; i < 128; i++)
        {
            var ptr = MpvNative.mpv_wait_event(_handle, 0);
            if (ptr == IntPtr.Zero) break;
            var ev = Marshal.PtrToStructure<MpvNative.Event>(ptr);
            if (ev.EventId == MpvNative.EventNone) break;
            if (ev.EventId == MpvNative.EventFileLoaded) result.Add(new MpvEvent(MpvEventKind.Loaded, 0));
            else if (ev.EventId == MpvNative.EventEndFile && ev.Data != IntPtr.Zero)
            {
                var end = Marshal.PtrToStructure<MpvNative.EndFile>(ev.Data);
                if (end.Reason == MpvNative.EndError) result.Add(new MpvEvent(MpvEventKind.Failed, end.Error));
                else if (end.Reason == MpvNative.EndEof) result.Add(new MpvEvent(MpvEventKind.Ended, 0));
            }
            else if (ev.EventId == MpvNative.EventCommandReply && ev.ReplyUserdata == 1 && ev.Error < 0)
                result.Add(new MpvEvent(MpvEventKind.Failed, ev.Error));
        }
        return result;
    }

    public double Number(string name)
    {
        if (!Ready) return double.NaN;
        return MpvNative.mpv_get_property(_handle, MpvNative.U8(name), MpvNative.FormatDouble, out double v) >= 0 ? v : double.NaN;
    }

    public bool Flag(string name)
    {
        if (!Ready) return false;
        return MpvNative.mpv_get_property(_handle, MpvNative.U8(name), MpvNative.FormatFlag, out int v) >= 0 && v != 0;
    }

    public string Text(string name)
    {
        if (!Ready) return "";
        var p = MpvNative.mpv_get_property_string(_handle, MpvNative.U8(name));
        if (p == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(p) ?? ""; }
        finally { MpvNative.mpv_free(p); }
    }

    public int Set(string name, string value) =>
        Ready ? MpvNative.mpv_set_property_string(_handle, MpvNative.U8(name), MpvNative.U8(value)) : -1;

    public int Set(string name, double value)
    {
        if (!Ready || !double.IsFinite(value)) return -1;
        return MpvNative.mpv_set_property(_handle, MpvNative.U8(name), MpvNative.FormatDouble, ref value);
    }

    public List<MpvTrack> Tracks()
    {
        var list = new List<MpvTrack>();
        if (!Ready) return list;
        var count = Number("track-list/count");
        var n = double.IsFinite(count) ? (int)Math.Max(0, count) : 0;
        for (var i = 0; i < n; i++)
        {
            var b = $"track-list/{i}/";
            var id = int.TryParse(Text(b + "id"), out var x) ? x : 0;
            var ch = int.TryParse(Text(b + "demux-channel-count"), out var y) ? y : 0;
            list.Add(new MpvTrack(Text(b + "type"), id, Text(b + "selected") == "yes", Text(b + "title"), Text(b + "lang"), ch));
        }
        return list;
    }

    public void Shutdown()
    {
        if (_handle == IntPtr.Zero) return;
        var h = _handle;
        _handle = IntPtr.Zero;
        MpvNative.mpv_terminate_destroy(h);
    }
}
