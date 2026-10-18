using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MNPlayer;

/// Command line codec check: MNPlayer.exe --verify-media out.txt file...  (no window, no audio).
internal static class Verify
{
    public static int Run(string[] args)
    {
        var outPath = args.Length > 1 ? args[1] : "verify.txt";
        var lines = new List<string>();
        var failed = false;
        var engine = new MpvEngine(IntPtr.Zero, null, headless: true);
        if (!engine.Ready)
        {
            File.WriteAllText(outPath, "ENGINE_ERROR " + engine.Error);
            return 2;
        }
        lines.Add("ENGINE_OK mpv " + engine.Text("mpv-version"));
        foreach (var file in args.Skip(2))
        {
            engine.DrainEvents();
            engine.Load(file);
            var deadline = DateTime.UtcNow.AddSeconds(12);
            var ok = false;
            var loaded = false;
            while (DateTime.UtcNow < deadline)
            {
                var done = false;
                foreach (var ev in engine.DrainEvents())
                {
                    if (ev.Kind == MpvEventKind.Loaded) loaded = true;
                    else if (ev.Kind == MpvEventKind.Failed) done = true;
                    else if (ev.Kind == MpvEventKind.Ended) { ok = loaded; done = true; }
                }
                var t = engine.Number("time-pos");
                if (loaded && double.IsFinite(t) && t > 0.25) { ok = true; break; }
                if (done) break;
                Thread.Sleep(100);
            }
            var w = engine.Text("video-params/w");
            var h = engine.Text("video-params/h");
            lines.Add($"{(ok ? "PASS" : "FAIL")} {Path.GetFileName(file)} {w}x{h} codec={engine.Text("video-codec")} rotate={engine.Text("video-params/rotate")} disp={engine.Text("dwidth")}x{engine.Text("dheight")}");
            if (!ok) failed = true;
            engine.Stop();
        }
        engine.Shutdown();
        File.WriteAllText(outPath, string.Join("\n", lines));
        return failed ? 1 : 0;
    }
}
