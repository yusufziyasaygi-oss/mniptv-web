using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        public string? SyncDoc { get; set; }
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
        set
        {
            if (!SameAccount(_data.Account, value)) { _data.SyncDoc = null; _syncCache = null; }
            _data.Account = value;
            Save();
        }
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
        Record(global::MNIPTV.SyncDoc.Fav, entry.Key, new JsonObject { ["e"] = EntryToSync(entry) }, deleted: !added);
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
        Record(global::MNIPTV.SyncDoc.Prog, p.Key, new JsonObject { ["p"] = ProgressToSync(p) }, deleted: false, at: (long)(p.UpdatedAt * 1000));
    }

    public void RemoveProgress(string key)
    {
        if (_data.Progress.RemoveAll(x => x.Key == key) > 0)
        {
            Save();
            Record(global::MNIPTV.SyncDoc.Prog, key, new JsonObject(), deleted: true);
        }
    }

    // per-episode position / watched

    public double EpisodePosition(string id) => _data.EpisodePositions.TryGetValue(id, out var v) ? v : 0;

    public void SetEpisodePosition(string id, double position)
    {
        _data.EpisodePositions[id] = position;
        Save();
        RecordEpisode(id, position, IsWatched(id));
    }

    public bool IsWatched(string id) => _data.Watched.Contains(id);

    public void SetWatched(string id)
    {
        _data.Watched.Add(id);
        _data.EpisodePositions.Remove(id);
        Save();
        RecordEpisode(id, 0, true);
    }

    // sync document: every local change also lands here with a time stamp (deletions as
    // tombstones); SyncService merges it with the other devices' copy and hands it back.

    private JsonObject? _syncCache;

    private static bool SameAccount(Account? a, Account? b) =>
        a?.Server == b?.Server && a?.Username == b?.Username && a?.Password == b?.Password;

    public JsonObject SyncDoc()
    {
        if (_syncCache != null) return _syncCache;
        JsonObject? doc = null;
        if (_data.SyncDoc != null) { try { doc = JsonNode.Parse(_data.SyncDoc) as JsonObject; } catch (Exception) { } }
        if (doc == null)
        {
            // First run with sync: what this PC already has goes in as very old entries.
            doc = global::MNIPTV.SyncDoc.Empty();
            var f = global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Fav);
            for (var i = 0; i < _data.Favorites.Count; i++)
            {
                var e = _data.Favorites[i];
                f[e.Key] = new JsonObject { ["at"] = (long)(_data.Favorites.Count - i), ["del"] = false, ["e"] = EntryToSync(e) };
            }
            var pr = global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Prog);
            foreach (var p in _data.Progress)
                pr[p.Key] = new JsonObject { ["at"] = (long)(p.UpdatedAt * 1000), ["del"] = false, ["p"] = ProgressToSync(p) };
            var ep = global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Ep);
            foreach (var id in _data.EpisodePositions.Keys.Concat(_data.Watched).Distinct())
                ep[id] = new JsonObject { ["at"] = 1L, ["pos"] = (long)(EpisodePosition(id) * 1000), ["w"] = IsWatched(id) };
            SaveSync(doc);
        }
        _syncCache = doc;
        return doc;
    }

    private void SaveSync(JsonObject doc)
    {
        _syncCache = doc;
        _data.SyncDoc = doc.ToJsonString();
        Save();
    }

    private void Record(string section, string key, JsonObject body, bool deleted, long? at = null)
    {
        var doc = SyncDoc();
        body["at"] = at ?? global::MNIPTV.SyncDoc.NowMs;
        body["del"] = deleted;
        global::MNIPTV.SyncDoc.Section(doc, section)[key] = body;
        SaveSync(doc);
        SyncService.Shared.LocalChanged();
    }

    private void RecordEpisode(string id, double position, bool watched)
    {
        var doc = SyncDoc();
        global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Ep)[id] =
            new JsonObject { ["at"] = global::MNIPTV.SyncDoc.NowMs, ["pos"] = (long)(position * 1000), ["w"] = watched };
        SaveSync(doc);
        SyncService.Shared.LocalChanged();
    }

    /// Replace local favourites, progress and episode marks with the merged document.
    public void ApplySyncDoc(JsonObject doc)
    {
        var favs = new List<(long at, MediaEntry e)>();
        foreach (var (_, v) in global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Fav))
        {
            if (v is not JsonObject o || global::MNIPTV.SyncDoc.Bool(o["del"])) continue;
            if (o["e"] is JsonObject eo && EntryFromSync(eo) is { } e) favs.Add((global::MNIPTV.SyncDoc.At(o), e));
        }
        _data.Favorites = favs.OrderByDescending(x => x.at).Select(x => x.e).ToList();

        var progs = new List<Progress>();
        foreach (var (_, v) in global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Prog))
        {
            if (v is not JsonObject o || global::MNIPTV.SyncDoc.Bool(o["del"])) continue;
            if (o["p"] is JsonObject po && ProgressFromSync(po, global::MNIPTV.SyncDoc.At(o)) is { } p) progs.Add(p);
        }
        _data.Progress = progs.OrderByDescending(x => x.UpdatedAt).Take(80).ToList();

        foreach (var (id, v) in global::MNIPTV.SyncDoc.Section(doc, global::MNIPTV.SyncDoc.Ep))
        {
            if (v is not JsonObject o) continue;
            if (global::MNIPTV.SyncDoc.Bool(o["w"])) _data.Watched.Add(id); else _data.Watched.Remove(id);
            var pos = global::MNIPTV.SyncDoc.Long(o["pos"]) / 1000.0;
            if (pos > 0) _data.EpisodePositions[id] = pos; else _data.EpisodePositions.Remove(id);
        }
        SaveSync(doc);
    }

    private static JsonObject EntryToSync(MediaEntry e) => new()
    {
        ["kind"] = e.Kind.Raw(), ["id"] = e.Id, ["name"] = e.Name, ["icon"] = e.Icon ?? "", ["ext"] = e.Ext ?? "",
        ["cat"] = e.CategoryId ?? "", ["num"] = e.Number,
    };

    private static Kind? KindOf(string? raw) => raw switch { "LIVE" => Kind.Live, "MOVIE" => Kind.Movie, "SERIES" => Kind.Series, _ => null };

    private static MediaEntry? EntryFromSync(JsonObject o)
    {
        if (KindOf(global::MNIPTV.SyncDoc.Str(o["kind"])) is not { } kind || global::MNIPTV.SyncDoc.Str(o["id"]) is not { } id) return null;
        return new MediaEntry
        {
            Kind = kind, Id = id, Name = global::MNIPTV.SyncDoc.Str(o["name"]) ?? id, Icon = global::MNIPTV.SyncDoc.Str(o["icon"]),
            Ext = global::MNIPTV.SyncDoc.Str(o["ext"]), CategoryId = global::MNIPTV.SyncDoc.Str(o["cat"]), Number = (int)global::MNIPTV.SyncDoc.Long(o["num"]),
        };
    }

    private static JsonObject ProgressToSync(Progress p) => new()
    {
        ["key"] = p.Key, ["kind"] = p.Kind.Raw(), ["id"] = p.Id, ["title"] = p.Title, ["subtitle"] = p.Subtitle ?? "",
        ["image"] = p.Image ?? "", ["ext"] = p.Ext ?? "", ["episodeId"] = p.EpisodeId ?? "", ["season"] = p.Season,
        ["episode"] = p.Episode, ["pos"] = (long)(p.Position * 1000), ["dur"] = (long)(p.Duration * 1000),
    };

    private static Progress? ProgressFromSync(JsonObject o, long at)
    {
        if (KindOf(global::MNIPTV.SyncDoc.Str(o["kind"])) is not { } kind) return null;
        var key = global::MNIPTV.SyncDoc.Str(o["key"]);
        var id = global::MNIPTV.SyncDoc.Str(o["id"]);
        if (key == null || id == null) return null;
        return new Progress
        {
            Key = key, Kind = kind, Id = id, Title = global::MNIPTV.SyncDoc.Str(o["title"]) ?? "",
            Subtitle = global::MNIPTV.SyncDoc.Str(o["subtitle"]), Image = global::MNIPTV.SyncDoc.Str(o["image"]),
            Ext = global::MNIPTV.SyncDoc.Str(o["ext"]), EpisodeId = global::MNIPTV.SyncDoc.Str(o["episodeId"]),
            Season = (int)global::MNIPTV.SyncDoc.Long(o["season"]), Episode = (int)global::MNIPTV.SyncDoc.Long(o["episode"]),
            Position = global::MNIPTV.SyncDoc.Long(o["pos"]) / 1000.0, Duration = global::MNIPTV.SyncDoc.Long(o["dur"]) / 1000.0,
            UpdatedAt = at / 1000.0,
        };
    }

    public double Volume
    {
        get => _data.Volume;
        set { _data.Volume = value; Save(); }
    }
}
