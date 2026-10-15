using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MNIPTV;

public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

public sealed class XtreamApi
{
    public const string UserAgent = "MN-IPTV-Windows/2.1.0";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(90) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return c;
    }

    public static HttpClient Client => Http;

    public Account Account { get; }

    public XtreamApi(Account account) { Account = account; }

    public static string Encode(string s) => Uri.EscapeDataString(s);

    private Uri ApiUrl(string? action, IEnumerable<(string, string)>? extra)
    {
        var q = new StringBuilder($"username={Encode(Account.Username)}&password={Encode(Account.Password)}");
        if (action != null) q.Append("&action=").Append(action);
        if (extra != null) foreach (var (k, v) in extra) q.Append('&').Append(k).Append('=').Append(Encode(v));
        if (!Uri.TryCreate(Account.Server + "/player_api.php?" + q, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ApiException("Sunucu adresi geçersiz");
        return uri;
    }

    private async Task<JsonDocument?> Get(string? action, CancellationToken ct, params (string, string)[] extra)
    {
        var uri = ApiUrl(action, extra);
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ApiException($"Sunucu yanıtı: HTTP {(int)response.StatusCode}");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length == 0) return null;
        try
        {
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// null when login is OK, otherwise a user-facing message.
    public async Task<string?> Authenticate(CancellationToken ct = default)
    {
        using var doc = await Get(null, ct).ConfigureAwait(false);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return "Bu adres bir Xtream sunucusu gibi görünmüyor";
        var info = doc.RootElement.Prop("user_info");
        if (info is not { ValueKind: JsonValueKind.Object } ui) return "Sunucu yanıtı tanınmadı";
        if (J.Int(ui.Prop("auth")) != 1) return "Kullanıcı adı veya şifre hatalı";
        var status = J.Str(ui.Prop("status"));
        if (status != null && !status.Equals("active", StringComparison.OrdinalIgnoreCase)) return $"Hesap durumu: {status}";
        return null;
    }

    public async Task<List<Category>> Categories(Kind kind, CancellationToken ct = default)
    {
        var action = kind switch { Kind.Live => "get_live_categories", Kind.Movie => "get_vod_categories", _ => "get_series_categories" };
        using var doc = await Get(action, ct).ConfigureAwait(false);
        var list = new List<Category>();
        if (doc?.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var o in doc.RootElement.EnumerateArray())
        {
            var id = J.Str(o.Prop("category_id"));
            if (id == null) continue;
            list.Add(new Category(id, J.Str(o.Prop("category_name")) ?? id));
        }
        return list;
    }

    /// categoryId == null loads the whole catalogue of that kind (used for search and "Tümü").
    public async Task<List<MediaEntry>> Streams(Kind kind, string? categoryId, CancellationToken ct = default)
    {
        var action = kind switch { Kind.Live => "get_live_streams", Kind.Movie => "get_vod_streams", _ => "get_series" };
        using var doc = string.IsNullOrEmpty(categoryId)
            ? await Get(action, ct).ConfigureAwait(false)
            : await Get(action, ct, ("category_id", categoryId!)).ConfigureAwait(false);
        var output = new List<MediaEntry>();
        if (doc?.RootElement.ValueKind != JsonValueKind.Array) return output;
        foreach (var o in doc.RootElement.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object) continue;
            switch (kind)
            {
                case Kind.Live:
                {
                    var id = J.Str(o.Prop("stream_id"));
                    if (id == null) continue;
                    output.Add(new MediaEntry
                    {
                        Kind = Kind.Live, Id = id, Name = J.Str(o.Prop("name")) ?? id, Icon = J.Str(o.Prop("stream_icon")),
                        CategoryId = J.Str(o.Prop("category_id")), Number = J.Int(o.Prop("num")),
                    });
                    break;
                }
                case Kind.Movie:
                {
                    var id = J.Str(o.Prop("stream_id"));
                    if (id == null) continue;
                    output.Add(new MediaEntry
                    {
                        Kind = Kind.Movie, Id = id, Name = J.Str(o.Prop("name")) ?? id, Icon = J.Str(o.Prop("stream_icon")),
                        Ext = J.Str(o.Prop("container_extension")) ?? "mp4", CategoryId = J.Str(o.Prop("category_id")),
                    });
                    break;
                }
                default:
                {
                    var id = J.Str(o.Prop("series_id"));
                    if (id == null) continue;
                    output.Add(new MediaEntry
                    {
                        Kind = Kind.Series, Id = id, Name = J.Str(o.Prop("name")) ?? id, Icon = J.Str(o.Prop("cover")),
                        CategoryId = J.Str(o.Prop("category_id")),
                    });
                    break;
                }
            }
        }
        if (kind == Kind.Live && output.Any(e => e.Number > 0))
            output = output.OrderBy(e => e.Number > 0 ? e.Number : int.MaxValue).ToList(); // stable
        return output;
    }

    public async Task<SeriesInfo> SeriesInfo(string seriesId, CancellationToken ct = default)
    {
        using var doc = await Get("get_series_info", ct, ("series_id", seriesId)).ConfigureAwait(false);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) throw new ApiException("Dizi bilgisi alınamadı");
        var root = doc.RootElement;
        var info = root.Prop("info");
        var bySeason = new SortedDictionary<int, List<Episode>>();

        void Add(JsonElement o, int seasonHint)
        {
            if (o.ValueKind != JsonValueKind.Object) return;
            var id = J.Str(o.Prop("id"));
            if (id == null) return;
            var season = J.Int(o.Prop("season"), seasonHint);
            if (season <= 0) season = seasonHint;
            var epInfo = o.Prop("info");
            var number = J.Int(o.Prop("episode_num"));
            var ep = new Episode
            {
                Id = id, Season = season, Number = number,
                Title = J.Str(o.Prop("title")) ?? $"Bölüm {number}",
                Ext = J.Str(o.Prop("container_extension")) ?? "mp4",
                Image = epInfo is { } ei ? J.Str(ei.Prop("movie_image")) : null,
                DurationSec = epInfo is { } ej ? J.Int(ej.Prop("duration_secs")) : 0,
            };
            if (!bySeason.TryGetValue(season, out var list)) bySeason[season] = list = new List<Episode>();
            list.Add(ep);
        }

        var episodes = root.Prop("episodes");
        if (episodes is { ValueKind: JsonValueKind.Object } dict)
        {
            foreach (var p in dict.EnumerateObject())
            {
                var hint = int.TryParse(p.Name, out var h) ? h : 1;
                if (p.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in p.Value.EnumerateArray()) Add(item, hint);
            }
        }
        else if (episodes is { ValueKind: JsonValueKind.Array } array)
        {
            var i = 0;
            foreach (var item in array.EnumerateArray())
            {
                i++;
                if (item.ValueKind == JsonValueKind.Array) { foreach (var e in item.EnumerateArray()) Add(e, i); }
                else Add(item, 1);
            }
        }
        foreach (var k in bySeason.Keys.ToList()) bySeason[k] = bySeason[k].OrderBy(e => e.Number).ToList();
        return new SeriesInfo
        {
            Name = info is { } n ? J.Str(n.Prop("name")) ?? "" : "",
            Plot = info is { } pl ? J.Str(pl.Prop("plot")) : null,
            Cover = info is { } c ? J.Str(c.Prop("cover")) : null,
            EpisodesBySeason = bySeason,
        };
    }

    public async Task<EpgNow?> ShortEpg(string streamId, CancellationToken ct = default)
    {
        using var doc = await Get("get_short_epg", ct, ("stream_id", streamId), ("limit", "2")).ConfigureAwait(false);
        if (doc?.RootElement.Prop("epg_listings") is not { ValueKind: JsonValueKind.Array } list) return null;
        var now = Fmt.Now();
        foreach (var o in list.EnumerateArray())
        {
            var start = J.Dbl(o.Prop("start_timestamp"));
            var end = J.Dbl(o.Prop("stop_timestamp"));
            if (start <= now && now < end) return new EpgNow(Decode(J.Str(o.Prop("title"))), start, end);
        }
        return null;
    }

    private static string Decode(string? b64)
    {
        if (b64 == null) return "";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Trim(); }
        catch (FormatException) { return b64; }
    }

    private string Credentials => $"{Encode(Account.Username)}/{Encode(Account.Password)}";
    public string LiveUrl(string id, string ext) => $"{Account.Server}/live/{Credentials}/{id}.{ext}";
    public string MovieUrl(string id, string ext) => $"{Account.Server}/movie/{Credentials}/{id}.{ext}";
    public string EpisodeUrl(string id, string ext) => $"{Account.Server}/series/{Credentials}/{id}.{ext}";
}
