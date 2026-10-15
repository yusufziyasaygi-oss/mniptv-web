using System;
using System.IO;
using System.Threading;
using System.Windows;
using MNIPTV.UI;

namespace MNIPTV;

public static class Program
{
    public static string? SmokeDir => Environment.GetEnvironmentVariable("MN_SMOKE_DIR");

    private static string LogPath => Path.Combine(Path.GetDirectoryName(Store.DefaultPath)!, "hata.log");

    [STAThread]
    public static void Main()
    {
        using var single = new Mutex(true, "MNIPTV-Windows-Single", out var first);
        if (!first && SmokeDir == null)
        {
            MessageBox.Show("MN IPTV zaten açık.", "MN IPTV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SmokeDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "data.json");
            if (File.Exists(path)) File.Delete(path);
            Store.Open(path);
        }
        else
        {
            Store.Open(Store.DefaultPath);
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception x) Log(x); };

        var model = new AppModel();
        var window = new MainWindow(model);
        if (SmokeDir is { } smoke) window.Loaded += (_, _) => new SmokeRunner(model, window, smoke).Run();
        app.Run(window);
    }

    public static void Log(Exception e)
    {
        try
        {
            var path = SmokeDir is { } d ? Path.Combine(d, "hata.log") : LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e}\n\n");
        }
        catch (Exception)
        {
            // Nothing else to do.
        }
    }
}
