using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MNIPTV;

public enum Section { ContinueWatching, Live, Movies, Series, Favorites, Search }

public static class SectionExt
{
    public static readonly Section[] All =
        { Section.ContinueWatching, Section.Live, Section.Movies, Section.Series, Section.Favorites, Section.Search };

    public static string Title(this Section s) => s switch
    {
        Section.ContinueWatching => "İzlemeye Devam",
        Section.Live => "Canlı TV",
        Section.Movies => "Filmler",
        Section.Series => "Diziler",
        Section.Favorites => "Favoriler",
        _ => "Ara",
    };

    /// Segoe Fluent Icons / Segoe MDL2 Assets glyphs.
    public static string Glyph(this Section s) => s switch
    {
        Section.ContinueWatching => "",
        Section.Live => "",
        Section.Movies => "",
        Section.Series => "",
        Section.Favorites => "",
        _ => "",
    };

    public static Kind? Kind(this Section s) => s switch
    {
        Section.Live => MNIPTV.Kind.Live,
        Section.Movies => MNIPTV.Kind.Movie,
        Section.Series => MNIPTV.Kind.Series,
        _ => null,
    };
}

public sealed class Tile
{
    public MediaEntry? Media { get; init; }
    public bool Favorite { get; set; }
    public bool ShowKind { get; init; }
    public Progress? Resume { get; init; }

    public string Key => Media != null ? Media.Key : "R:" + Resume!.Key;
    public bool IsLive => Media?.Kind == MNIPTV.Kind.Live;
}

public enum PlayMode { Live, Movie, Series }

public sealed class PlayRequest
{
    public Guid Token { get; } = Guid.NewGuid();
    public PlayMode Mode { get; init; }
    public List<MediaEntry> Channels { get; init; } = new();
    public int Index { get; init; }
    public string ListTitle { get; init; } = "";
    public MediaEntry? Movie { get; init; }
    public double Start { get; init; }
    public string SeriesId { get; init; } = "";
    public string SeriesName { get; init; } = "";
    public string? SeriesCover { get; init; }
    public List<Episode> Episodes { get; init; } = new();
}

public abstract record Route;
public sealed record SeriesRoute(MediaEntry Entry) : Route;
public sealed record PlayerRoute(PlayRequest Request) : Route;

/// Catalogue state shared by all screens. Lives on the UI thread; async work resumes there.
public sealed class AppModel
{
    public const string AllCategoryId = "__all__";

    public event Action<string>? Changed;

    public Account? Account { get; private set; }
    public Section Section { get; private set; } = Section.Live;
    public List<Category> Categories { get; private set; } = new();
    public int SelectedCategory { get; private set; } = -1;
    public List<Tile> Tiles { get; private set; } = new();
    public string ContentTitle { get; private set; } = "";
    public bool Loading { get; private set; }
    public string? EmptyMessage { get; private set; }
    public string Query { get; set; } = "";
    public List<Route> RouteStack { get; } = new();
    public string? Toast { get; private set; }
    public int Revision { get; private set; }

    public Store Store => Store.Shared;
    public XtreamApi? Api { get; private set; }

    /// The player currently on screen (used by the smoke test).
    public PlayerController? ActivePlayer { get; set; }

    private readonly Dictionary<Kind, List<Category>> _categoryCache = new();
    private readonly Dictionary<string, List<MediaEntry>> _streamCache = new();
    private readonly Dictionary<string, SeriesInfo> _seriesCache = new();
    private CancellationTokenSource? _categoryCts, _contentCts, _toastCts;
    private bool _didStart;

    public AppModel()
    {
        if (Store.Account is { } acc)
        {
            Account = acc;
            Api = new XtreamApi(acc);
        }
    }

    private void Raise(string what) => Changed?.Invoke(what);

    public void StartIfNeeded()
    {
        if (_didStart) return;
        Start();
    }

    private void Start()
    {
        _didStart = true;
        if (Account == null) return;
        Select(Store.ProgressList.Count == 0 ? Section.Live : Section.ContinueWatching, force: true);
    }

    /// Menu command: leave player/series screens and open a section.
    public void GoToSection(Section s)
    {
        if (Account == null) return;
        if (RouteStack.Count > 0)
        {
            RouteStack.Clear();
            Raise("route");
            RefreshAfterReturn();
        }
        Select(s);
    }

    // account

    public async Task<string?> Login(string server, string username, string password)
    {
        var acc = new Account { Server = Account.Normalize(server), Username = username.Trim(), Password = password.Trim() };
        if (acc.Server.Length == 0 || acc.Username.Length == 0 || acc.Password.Length == 0)
            return "Sunucu adresi, kullanıcı adı ve şifre gerekli";
        var candidate = new XtreamApi(acc);
        try
        {
            var error = await candidate.Authenticate();
            if (error != null) return error;
        }
        catch (Exception e)
        {
            return "Sunucuya bağlanılamadı: " + Describe(e);
        }
        Store.Account = acc;
        Api = candidate;
        ClearCache();
        RouteStack.Clear();
        Account = acc;
        Raise("account");
        Raise("route");
        Start();
        return null;
    }

    public static string Describe(Exception e) => e switch
    {
        ApiException a => a.Message,
        TaskCanceledException => "zaman aşımı",
        System.Net.Http.HttpRequestException h => h.InnerException?.Message ?? h.Message,
        _ => e.Message,
    };

    public void SignOut()
    {
        _categoryCts?.Cancel();
        _contentCts?.Cancel();
        RouteStack.Clear();
        Account = null;
        Api = null;
        ClearCache();
        Raise("route");
        Raise("account");
    }

    public void ClearCache()
    {
        _categoryCache.Clear();
        _streamCache.Clear();
        _seriesCache.Clear();
    }

    public void ReloadSection()
    {
        ClearCache();
        Select(Section, force: true);
        ShowToast("Liste yenileniyor");
    }

    // data

    private XtreamApi RequireApi() => Api ?? throw new ApiException("Hesap bilgisi yok");

    public async Task<List<Category>> GetCategories(Kind kind, CancellationToken ct)
    {
        if (_categoryCache.TryGetValue(kind, out var c)) return c;
        var list = await RequireApi().Categories(kind, ct);
        _categoryCache[kind] = list;
        return list;
    }

    public async Task<List<MediaEntry>> GetStreams(Kind kind, string? categoryId, CancellationToken ct)
    {
        var key = $"{kind.Raw()}|{categoryId ?? "*"}";
        if (_streamCache.TryGetValue(key, out var c)) return c;
        var list = await RequireApi().Streams(kind, categoryId, ct);
        _streamCache[key] = list;
        return list;
    }

    public async Task<SeriesInfo> GetSeriesInfo(string id)
    {
        if (_seriesCache.TryGetValue(id, out var c)) return c;
        var info = await RequireApi().SeriesInfo(id);
        _seriesCache[id] = info;
        return info;
    }

    // sections

    public void Select(Section s, bool force = false)
    {
        if (!(force || s != Section || (s.Kind() != null && Categories.Count == 0))) return;
        Section = s;
        _categoryCts?.Cancel();
        _contentCts?.Cancel();
        Categories = new List<Category>();
        SelectedCategory = -1;
        EmptyMessage = null;
        Raise("section");
        Raise("categories");
        switch (s)
        {
            case Section.Live:
            case Section.Movies:
            case Section.Series:
                Tiles = new List<Tile>();
                ContentTitle = "";
                Raise("tiles");
                Raise("title");
                LoadCategories(s.Kind()!.Value);
                break;
            case Section.ContinueWatching:
                ShowContinue();
                break;
            case Section.Favorites:
                ShowFavorites();
                break;
            default:
                ContentTitle = "Arama";
                Raise("title");
                if (Query.Trim().Length >= 2) RunSearch();
                else SetTiles(new List<Tile>(), "En az 2 harf yaz");
                break;
        }
    }

    private async void LoadCategories(Kind kind)
    {
        var cts = _categoryCts = new CancellationTokenSource();
        SetLoading(true);
        try
        {
            var cats = await GetCategories(kind, cts.Token);
            if (cts.IsCancellationRequested) return;
            Categories = cats.Concat(new[] { new Category(AllCategoryId, "Tümü") }).ToList();
            Raise("categories");
            SelectCategory(0, force: true);
        }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            SetTiles(new List<Tile>(), $"Liste alınamadı: {Describe(e)}\nBölümü tekrar seçerek yeniden dene.");
        }
    }

    public async void SelectCategory(int index, bool force = false)
    {
        if (Section.Kind() is not { } kind || index < 0 || index >= Categories.Count) return;
        if (!force && index == SelectedCategory && Tiles.Count > 0) return;
        SelectedCategory = index;
        var cat = Categories[index];
        ContentTitle = cat.Name;
        EmptyMessage = null;
        Raise("categories");
        Raise("title");
        SetLoading(true);
        _contentCts?.Cancel();
        var cts = _contentCts = new CancellationTokenSource();
        try
        {
            var list = await GetStreams(kind, cat.Id == AllCategoryId ? null : cat.Id, cts.Token);
            if (cts.IsCancellationRequested) return;
            var favs = Store.FavoriteKeys;
            SetTiles(list.Select(e => new Tile { Media = e, Favorite = favs.Contains(e.Key) }).ToList(), "Bu kategoride içerik yok");
        }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            SetTiles(new List<Tile>(), $"İçerik alınamadı: {Describe(e)}\nKategoriyi tekrar seçerek yeniden dene.");
        }
    }

    public void ShowContinue()
    {
        ContentTitle = Section.ContinueWatching.Title();
        Raise("title");
        SetTiles(Store.ProgressList.Select(p => new Tile { Resume = p }).ToList(), "Yarım kalan film veya dizi yok.");
    }

    public void ShowFavorites()
    {
        ContentTitle = Section.Favorites.Title();
        Raise("title");
        SetTiles(Store.Favorites.Select(f => new Tile { Media = f, Favorite = true, ShowKind = f.Kind != Kind.Live }).ToList(),
            "Favori yok. Bir kanalın, filmin veya dizinin üzerinde sağ tıklayarak ya da F tuşuyla ekleyebilirsin.");
    }

    public async void RunSearch()
    {
        var q = Query.Trim();
        _contentCts?.Cancel();
        if (q.Length < 2)
        {
            ContentTitle = "Arama";
            Raise("title");
            SetTiles(new List<Tile>(), "En az 2 harf yaz");
            return;
        }
        ContentTitle = $"Aranıyor: {q}";
        EmptyMessage = null;
        Raise("title");
        SetLoading(true);
        var cts = _contentCts = new CancellationTokenSource();
        var needle = Fmt.Fold(q);
        var results = new List<MediaEntry>();
        var failures = 0;
        foreach (var kind in new[] { Kind.Live, Kind.Movie, Kind.Series })
        {
            try
            {
                var all = await GetStreams(kind, null, cts.Token);
                if (cts.IsCancellationRequested) return;
                var found = await Task.Run(() => all.Where(e => Fmt.Fold(e.Name).Contains(needle)).Take(300).ToList());
                if (cts.IsCancellationRequested) return;
                results.AddRange(found);
            }
            catch (Exception)
            {
                if (cts.IsCancellationRequested) return;
                failures++;
            }
        }
        if (cts.IsCancellationRequested) return;
        var favs = Store.FavoriteKeys;
        ContentTitle = $"\"{q}\" · {results.Count} sonuç";
        Raise("title");
        SetTiles(results.Select(e => new Tile { Media = e, Favorite = favs.Contains(e.Key), ShowKind = e.Kind != Kind.Live }).ToList(),
            failures == 3 ? "Arama yapılamadı, bağlantıyı kontrol et." : "Sonuç bulunamadı");
    }

    private void SetLoading(bool value)
    {
        Loading = value;
        Raise("loading");
    }

    private void SetTiles(List<Tile> list, string empty)
    {
        Loading = false;
        Tiles = list;
        EmptyMessage = list.Count == 0 ? empty : null;
        Raise("tiles");
        Raise("loading");
    }

    /// Called when coming back to the catalogue: progress and favourites may have changed.
    public void RefreshAfterReturn()
    {
        Revision++;
        Raise("revision");
        switch (Section)
        {
            case Section.ContinueWatching: ShowContinue(); break;
            case Section.Favorites: ShowFavorites(); break;
            default:
                var favs = Store.FavoriteKeys;
                var changed = false;
                foreach (var t in Tiles)
                {
                    if (t.Media == null) continue;
                    var f = favs.Contains(t.Media.Key);
                    if (f != t.Favorite) { t.Favorite = f; changed = true; }
                }
                if (changed) Raise("tiles-state");
                break;
        }
    }

    // actions

    public void Open(Tile tile)
    {
        if (tile.Media is { } e)
        {
            switch (e.Kind)
            {
                case Kind.Live:
                    var lives = Tiles.Where(t => t.IsLive).Select(t => t.Media!).ToList();
                    var index = Math.Max(0, lives.FindIndex(x => x.Key == e.Key));
                    Push(new PlayerRoute(new PlayRequest { Mode = PlayMode.Live, Channels = lives, Index = index, ListTitle = ContentTitle }));
                    break;
                case Kind.Movie:
                    PlayMovie(e, null);
                    break;
                default:
                    Push(new SeriesRoute(e));
                    break;
            }
        }
        else if (tile.Resume is { } p)
        {
            if (p.Kind == Kind.Movie)
            {
                var m = new MediaEntry { Kind = Kind.Movie, Id = p.Id, Name = p.Title, Icon = p.Image, Ext = p.Ext ?? "mp4" };
                Push(new PlayerRoute(new PlayRequest { Mode = PlayMode.Movie, Movie = m, Start = p.Position }));
            }
            else
            {
                Push(new SeriesRoute(new MediaEntry { Kind = Kind.Series, Id = p.Id, Name = p.Title, Icon = p.Image }));
            }
        }
    }

    /// start == null: resume from saved progress when there is one.
    public void PlayMovie(MediaEntry e, double? start)
    {
        var saved = Store.GetProgress(e.Key)?.Position ?? 0;
        Push(new PlayerRoute(new PlayRequest { Mode = PlayMode.Movie, Movie = e, Start = start ?? (saved > 30 ? saved : 0) }));
    }

    public void PlaySeries(string id, string name, string? cover, List<Episode> episodes, int index, double start)
    {
        Push(new PlayerRoute(new PlayRequest
        {
            Mode = PlayMode.Series, Index = index, Start = start, SeriesId = id, SeriesName = name, SeriesCover = cover, Episodes = episodes,
        }));
    }

    public void ToggleFavorite(MediaEntry entry)
    {
        var added = Store.ToggleFavorite(entry);
        ShowToast(added ? "Favorilere eklendi" : "Favorilerden çıkarıldı");
        RefreshAfterReturn();
    }

    public void RemoveProgress(Progress p)
    {
        Store.RemoveProgress(p.Key);
        RefreshAfterReturn();
    }

    public void Push(Route r)
    {
        RouteStack.Add(r);
        Raise("route");
    }

    public void Back()
    {
        if (RouteStack.Count == 0) return;
        RouteStack.RemoveAt(RouteStack.Count - 1);
        Raise("route");
        RefreshAfterReturn();
    }

    public async void ShowToast(string text)
    {
        Toast = text;
        Raise("toast");
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        try { await Task.Delay(2000, cts.Token); }
        catch (TaskCanceledException) { return; }
        Toast = null;
        Raise("toast");
    }
}
