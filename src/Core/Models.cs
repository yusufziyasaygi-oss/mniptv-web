using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MNIPTV;

public enum Kind { Live, Movie, Series }

public static class KindExt
{
    public static string Raw(this Kind k) => k switch { Kind.Live => "LIVE", Kind.Movie => "MOVIE", _ => "SERIES" };
    public static Kind Parse(string? s) => s switch { "LIVE" => Kind.Live, "MOVIE" => Kind.Movie, _ => Kind.Series };
}

public sealed record Category(string Id, string Name);

public sealed class Account
{
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public static string Normalize(string input)
    {
        var s = (input ?? "").Trim();
        if (s.Length == 0) return s;
        var lower = s.ToLowerInvariant();
        if (!lower.StartsWith("http://") && !lower.StartsWith("https://")) s = "http://" + s;
        foreach (var marker in new[] { "/player_api.php", "/get.php" })
        {
            var i = s.IndexOf(marker, StringComparison.Ordinal);
            if (i >= 0) s = s[..i];
        }
        while (s.EndsWith("/")) s = s[..^1];
        return s;
    }
}

/// A channel, movie or series as listed in the catalogue.
public sealed class MediaEntry
{
    public Kind Kind { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    public string? Ext { get; set; }
    public string? CategoryId { get; set; }
    public int Number { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string Key => $"{Kind.Raw()}:{Id}";
}

public sealed class Episode
{
    public string Id { get; set; } = "";
    public int Season { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Ext { get; set; } = "mp4";
    public string? Image { get; set; }
    public int DurationSec { get; set; }
}

public sealed class SeriesInfo
{
    public string Name { get; init; } = "";
    public string? Plot { get; init; }
    public string? Cover { get; init; }
    public SortedDictionary<int, List<Episode>> EpisodesBySeason { get; init; } = new();

    public List<int> Seasons => EpisodesBySeason.Keys.ToList();
    public List<Episode> AllEpisodes => EpisodesBySeason.Values.SelectMany(x => x).ToList();
}

public sealed record EpgNow(string Title, double Start, double End);

/// One "İzlemeye Devam" card. Series are keyed per series, so each series has a single card.
public sealed class Progress
{
    public string Key { get; set; } = "";
    public Kind Kind { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Image { get; set; }
    public string? Ext { get; set; }
    public string? EpisodeId { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public double Position { get; set; }
    public double Duration { get; set; }
    public double UpdatedAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public double Fraction => Duration > 0 ? Math.Min(1, Math.Max(0, Position / Duration)) : 0;
}

/// Lenient JSON helpers: Xtream panels mix numbers, strings, "" and null.
public static class J
{
    public static JsonElement? Prop(this JsonElement o, string name)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;
        return o.TryGetProperty(name, out var v) ? v : null;
    }

    public static string? Str(JsonElement? v)
    {
        if (v is not { } e) return null;
        string s;
        switch (e.ValueKind)
        {
            case JsonValueKind.String: s = e.GetString() ?? ""; break;
            case JsonValueKind.Number: s = e.GetRawText(); break;
            case JsonValueKind.True: s = "true"; break;
            case JsonValueKind.False: s = "false"; break;
            default: return null;
        }
        s = s.Trim();
        return s.Length == 0 || s == "null" ? null : s;
    }

    public static int Int(JsonElement? v, int fallback = 0)
    {
        var s = Str(v);
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
            return (int)Math.Clamp(d, int.MinValue, int.MaxValue);
        return fallback;
    }

    public static double Dbl(JsonElement? v, double fallback = 0)
    {
        var s = Str(v);
        if (s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) return d;
        return fallback;
    }
}

public static class Fmt
{
    public static string Time(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return "0:00";
        var total = (long)seconds;
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public static string Clock(double unix) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000)).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static readonly CultureInfo Tr = new("tr-TR");

    public static string Fold(string s)
    {
        var t = s.ToLower(Tr);
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            sb.Append(c switch
            {
                'ı' => 'i', 'ğ' => 'g', 'ü' => 'u', 'ş' => 's', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                _ => c
            });
        }
        return sb.ToString();
    }

    private static readonly Dictionary<string, string> Languages = new()
    {
        ["tr"] = "Türkçe", ["tur"] = "Türkçe", ["en"] = "İngilizce", ["eng"] = "İngilizce",
        ["de"] = "Almanca", ["ger"] = "Almanca", ["deu"] = "Almanca", ["fr"] = "Fransızca", ["fre"] = "Fransızca", ["fra"] = "Fransızca",
        ["ar"] = "Arapça", ["ara"] = "Arapça", ["ru"] = "Rusça", ["rus"] = "Rusça", ["es"] = "İspanyolca", ["spa"] = "İspanyolca",
        ["it"] = "İtalyanca", ["ita"] = "İtalyanca", ["ja"] = "Japonca", ["jpn"] = "Japonca", ["ko"] = "Korece", ["kor"] = "Korece",
        ["zh"] = "Çince", ["chi"] = "Çince", ["zho"] = "Çince", ["pt"] = "Portekizce", ["por"] = "Portekizce",
        ["nl"] = "Felemenkçe", ["dut"] = "Felemenkçe", ["nld"] = "Felemenkçe", ["el"] = "Yunanca", ["gre"] = "Yunanca", ["ell"] = "Yunanca",
        ["fa"] = "Farsça", ["per"] = "Farsça", ["fas"] = "Farsça", ["ku"] = "Kürtçe", ["kur"] = "Kürtçe", ["az"] = "Azerbaycanca", ["aze"] = "Azerbaycanca",
        ["hi"] = "Hintçe", ["hin"] = "Hintçe", ["pl"] = "Lehçe", ["pol"] = "Lehçe", ["sv"] = "İsveççe", ["swe"] = "İsveççe",
        ["bg"] = "Bulgarca", ["bul"] = "Bulgarca", ["ro"] = "Rumence", ["rum"] = "Rumence", ["ron"] = "Rumence",
        ["sq"] = "Arnavutça", ["alb"] = "Arnavutça", ["sqi"] = "Arnavutça", ["bs"] = "Boşnakça", ["bos"] = "Boşnakça",
    };

    public static string Language(string code) =>
        Languages.TryGetValue(code.ToLowerInvariant(), out var n) ? n : code.ToUpperInvariant();
}
