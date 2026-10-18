using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using MNPlayer.UI;

namespace MNPlayer;

public static class Program
{
    public static string? SmokeDir => Environment.GetEnvironmentVariable("MN_SMOKE_DIR");

    private static string LogPath => Path.Combine(Path.GetDirectoryName(Settings.DefaultPath)!, "hata.log");

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify-media") return Verify.Run(args);

        var service = args.Contains("--preview-service");
        var pi = Array.IndexOf(args, "--preview");
        var previewPath = pi >= 0 && pi + 1 < args.Length ? args[pi + 1] : null;
        var items = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(Normalize).Where(x => x != null).Cast<string>().ToList();

        if (SmokeDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            var settings = Path.Combine(dir, "settings.json");
            if (File.Exists(settings)) File.Delete(settings);
            Settings.Open(settings);
        }
        else
        {
            Settings.Open(Settings.DefaultPath);
        }

        return service || previewPath != null ? RunPreview(previewPath) : RunMain(items);
    }

    /// File path, URL, or an mnplayer://play?url=… link from MN IPTV.
    private static string? Normalize(string arg)
    {
        if (arg.StartsWith("mnplayer:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(arg);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var target = query["url"];
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception) { return null; }
        }
        return arg;
    }

    private static int RunMain(List<string> items)
    {
        var smoke = SmokeDir;
        using var single = new Mutex(true, "MNPlayer-Main-" + Environment.UserName, out var first);
        if (!first && smoke == null)
        {
            Ipc.Send(Ipc.MainPipe, items.Count > 0 ? "OPEN\t" + string.Join("\n", items) : "FOCUS", 1500);
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) Log(x); };

        var shell = new PlayerShell(false);
        Ipc.Serve(Ipc.MainPipe, message => app.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (message == "PAUSE") { shell.Pause(); return; }
            if (message.StartsWith("OPEN\t", StringComparison.Ordinal))
                shell.Add(message.Substring(5).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            if (shell.WindowState == WindowState.Minimized) shell.WindowState = WindowState.Normal;
            shell.Activate();
        })));
        if (items.Count > 0) shell.Add(items);
        if (smoke != null) shell.Loaded += (_, _) => new SmokeRunner(shell, smoke).Run();
        return app.Run(shell);
    }

    private static int RunPreview(string? path)
    {
        var smoke = SmokeDir;
        using var single = new Mutex(true, "MNPlayer-Preview-" + Environment.UserName, out var first);
        if (!first && smoke == null)
        {
            if (path != null) Ipc.Send(Ipc.PreviewPipe, path, 1500);
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log(e.Exception); e.Handled = true; };
        var service = new PreviewService();
        service.Start(true);
        if (smoke != null) app.Startup += (_, _) => new PreviewSmokeRunner(service, smoke, path).Run();
        else if (path != null) app.Startup += (_, _) => service.Open(path);
        return app.Run();
    }

    private static void Log(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:s} {e}\n\n");
        }
        catch (Exception) { }
    }
}
