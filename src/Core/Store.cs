using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MNIPTV;

/// Persistent account, favourites and watch progress, kept in one JSON file. Used from the UI thread.
public sealed class Store
{
    private sealed class Data
    {
        public Account? Account { get; set; }
        public List<MediaEntry> Favorites { get; set; } = new();
        public List<Progress> Progress { get; set; } = new();
        public Dictionary<string, double> EpisodePositions { get; set; } = new();
        public HashSet<string> Watched { get; set; } = new();
        public double Volume { get; set; } = 80;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private Data _data;

    public static Store Shared { get; private set; } = null!;

    public static void Open(string path) { Shared = new Store(path); }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MN IPTV", "data.json");

    public Store(string path)
    {
        _path = path;
        _data = Load(path);
    }

    private static Data Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), Options) ?? new Data();
        }
        catch (Exception)
        {
            // A damaged file must not stop the app; keep a copy and start clean.
            try { File.Copy(path, path + ".bozuk", true); } catch (Exception) { }
        }
        return new Data();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Options));
            File.Move(tmp, _path, true);
        }
        catch (Exception)
        {
            // Saving is best effort (disk full, permissions); playback continues.
        }
    }

    public Account? Account
    {
        get => _data.Account;
        set { _data.Account = value; Save(); }
    }

    // favourites

    public IReadOnlyList<MediaEntry> Favorites => _data.Favorites;
    public HashSet<string> FavoriteKeys => _data.Favorites.Select(f => f.Key).ToHashSet();
    public bool IsFavorite(string key) => _data.Favorites.Any(f => f.Key == key);

    /// Returns true when the entry is now a favourite.
    public bool ToggleFavorite(MediaEntry entry)
    {
        var i = _data.Favorites.FindIndex(f => f.Key == entry.Key);
        bool added;
        if (i >= 0) { _data.Favorites.RemoveAt(i); added = false; }
        else
        {
            _data.Favorites.Insert(0, new MediaEntry
            {
                Kind = entry.Kind, Id = entry.Id, Name = entry.Name, Icon = entry.Icon, Ext = entry.Ext,
                CategoryId = entry.CategoryId, Number = entry.Number,
            });
            added = true;
        }
        Save();
        return added;
    }

    // continue watching

    public List<Progress> ProgressList => _data.Progress.OrderByDescending(p => p.UpdatedAt).ToList();
    public Progress? GetProgress(string key) => _data.Progress.FirstOrDefault(p => p.Key == key);

    public void SaveProgress(Progress p)
    {
        _data.Progress.RemoveAll(x => x.Key == p.Key);
        _data.Progress.Insert(0, p);
        _data.Progress = _data.Progress.OrderByDescending(x => x.UpdatedAt).Take(80).ToList();
        Save();
    }

    public void RemoveProgress(string key)
    {
        if (_data.Progress.RemoveAll(x => x.Key == key) > 0) Save();
    }

    // per-episode position / watched

    public double EpisodePosition(string id) => _data.EpisodePositions.TryGetValue(id, out var v) ? v : 0;

    public void SetEpisodePosition(string id, double position)
    {
        _data.EpisodePositions[id] = position;
        Save();
    }

    public bool IsWatched(string id) => _data.Watched.Contains(id);

    public void SetWatched(string id)
    {
        _data.Watched.Add(id);
        _data.EpisodePositions.Remove(id);
        Save();
    }

    public double Volume
    {
        get => _data.Volume;
        set { _data.Volume = value; Save(); }
    }
}
