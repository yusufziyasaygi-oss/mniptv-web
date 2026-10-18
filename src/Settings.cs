using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MNPlayer;

/// Persistent preferences, resume positions and window frame (one JSON file).
public sealed class Settings
{
    public double Volume { get; set; } = 80;
    public double Rate { get; set; } = 1;
    public Dictionary<string, long> Positions { get; set; } = new();
    public List<string> Order { get; set; } = new();
    public double WinLeft { get; set; }
    public double WinTop { get; set; }
    public double WinWidth { get; set; }
    public double WinHeight { get; set; }

    public static Settings Current { get; private set; } = new();
    public static string Path { get; private set; } = DefaultPath;

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MN Player", "settings.json");

    public static void Open(string path)
    {
        Path = path;
        try
        {
            if (File.Exists(path)) Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch (Exception)
        {
            Current = new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this));
            File.Move(tmp, Path, true);
        }
        catch (Exception)
        {
            // Preferences are best effort.
        }
    }

    public void RememberPosition(string key, long ms, long length)
    {
        if (length <= 0 || ms <= 5000) return;
        Order.Remove(key);
        if (ms < length - 5000) { Positions[key] = ms; Order.Insert(0, key); }
        else Positions.Remove(key);
        for (var i = 200; i < Order.Count; i++) Positions.Remove(Order[i]);
        if (Order.Count > 200) Order.RemoveRange(200, Order.Count - 200);
    }

    public void ForgetPosition(string key)
    {
        Positions.Remove(key);
        Order.Remove(key);
    }
}
