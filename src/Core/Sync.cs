using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MNIPTV;

/// Cross-device sync of favourites, "İzlemeye Devam" and episode positions — same format as the
/// TV and Mac apps (see ci/mnsync.py). Items are merged by time stamp; the document is stored
/// AES-256-GCM encrypted (key derived from the Xtream login) in a private GitHub repository.
public static class SyncConfig
{
    public const string Api = "https://api.github.com";
    public const string Repo = "yusufziyasaygi-oss/mn-sync";
    // XOR-obfuscated access token (contents of the mn-sync repository only). Empty: sync off.
    private const string TokenX = "KidZGwwMPF1TRG0HHCghMgwJYiJJARBCWGJQUVhcIgwfEWlDHjQzSkN3agJpCQkyehxFSzQlGVl7BwJQZClQODsMbgZIX1BGWEZ1eVxfPTodBGwwSzgLb0MGUQVA";
    private const string Mask = "MN-sync-2026-key";

    public static string Token
    {
        get
        {
            if (TokenX.Length == 0) return "";
            var x = Convert.FromBase64String(TokenX);
            for (var i = 0; i < x.Length; i++) x[i] ^= (byte)Mask[i % Mask.Length];
            return Encoding.UTF8.GetString(x);
        }
    }
}

public static class SyncDoc
{
    public const string Fav = "fav", Prog = "prog", Ep = "ep";
    private const long TombstoneMs = 90L * 24 * 3600 * 1000;

    public static JsonObject Empty() => new() { ["v"] = 1, [Fav] = new JsonObject(), [Prog] = new JsonObject(), [Ep] = new JsonObject() };

    public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static JsonObject Section(JsonObject doc, string name)
    {
        if (doc[name] is JsonObject o) return o;
        var n = new JsonObject();
        doc[name] = n;
        return n;
    }

    public static long At(JsonNode? n)
    {
        if (n is not JsonObject o || o["at"] is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        return 0;
    }

    public static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public static long Long(JsonNode? n)
    {
        if (n is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) return (long)p;
        return 0;
    }

    public static string? Str(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        var s = v.TryGetValue<string>(out var str) ? str : v.ToJsonString();
        s = s.Trim();
        return s.Length == 0 || s == "null" ? null : s;
    }

    /// Per item the newer time stamp wins; on a tie the local copy is kept.
    public static JsonObject Merge(JsonObject local, JsonObject? remote)
    {
        var outDoc = Empty();
        foreach (var name in new[] { Fav, Prog, Ep })
        {
            var dst = Section(outDoc, name);
            if (local[name] is JsonObject a)
                foreach (var (k, v) in a) if (v != null) dst[k] = v.DeepClone();
            if (remote?[name] is JsonObject b)
                foreach (var (k, v) in b)
                    if (v != null && At(v) > At(dst[k])) dst[k] = v.DeepClone();
        }
        Prune(outDoc);
        return outDoc;
    }

    private static void Prune(JsonObject doc)
    {
        var limit = NowMs - TombstoneMs;
        foreach (var name in new[] { Fav, Prog })
        {
            var s = Section(doc, name);
            foreach (var k in s.Select(p => p.Key).ToList())
            {
                var o = s[k] as JsonObject;
                if (o == null || (Bool(o["del"]) && At(o) < limit)) s.Remove(k);
            }
        }
        Trim(Section(doc, Prog), 150);
        Trim(Section(doc, Ep), 3000);
    }

    private static void Trim(JsonObject s, int max)
    {
        if (s.Count <= max) return;
        foreach (var k in s.OrderByDescending(p => At(p.Value)).Skip(max).Select(p => p.Key).ToList()) s.Remove(k);
    }

    /// Key-order independent comparison.
    public static bool Same(JsonNode? a, JsonNode? b) => Canon(a) == Canon(b);

    private static string Canon(JsonNode? n) => n switch
    {
        null => "null",
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canon(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canon)) + "]",
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v when v.TryGetValue<string>(out var s) => JsonSerializer.Serialize(s),
        JsonValue v => NumberText(v),
    };

    private static string NumberText(JsonValue v)
    {
        if (v.TryGetValue<long>(out var l)) return l.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (v.TryGetValue<double>(out var d))
            return d == Math.Floor(d) && Math.Abs(d) < 9e15 ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var t = v.ToJsonString();
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p) && p == Math.Floor(p)
            ? ((long)p).ToString(System.Globalization.CultureInfo.InvariantCulture) : t;
    }
}

public static class SyncCrypto
{
    private static byte[] Sha(string s) => SHA256.HashData(Encoding.UTF8.GetBytes(s));

    public static string FileId(Account a) =>
        Convert.ToHexString(Sha($"mn-sync-id-v1|{a.Server.ToLowerInvariant()}|{a.Username}|{a.Password}")).ToLowerInvariant();

    public static byte[] Key(Account a) => Sha($"mn-sync-key-v1|{a.Server.ToLowerInvariant()}|{a.Username}|{a.Password}");

    public static string Seal(JsonObject doc, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(doc.ToJsonString());
        var ct = new byte[plain.Length];
        var tag = new byte[16];
        using (var g = new AesGcm(key, 16)) g.Encrypt(nonce, plain, ct, tag);
        var all = new byte[ct.Length + 16];
        Buffer.BlockCopy(ct, 0, all, 0, ct.Length);
        Buffer.BlockCopy(tag, 0, all, ct.Length, 16);
        return new JsonObject { ["v"] = 1, ["n"] = Convert.ToBase64String(nonce), ["c"] = Convert.ToBase64String(all) }.ToJsonString();
    }

    public static JsonObject Unseal(string text, byte[] key)
    {
        var o = JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException();
        var nonce = Convert.FromBase64String((string)o["n"]!);
        var all = Convert.FromBase64String((string)o["c"]!);
        if (all.Length < 16) throw new InvalidOperationException();
        var ct = all.AsSpan(0, all.Length - 16);
        var tag = all.AsSpan(all.Length - 16);
        var plain = new byte[ct.Length];
        using (var g = new AesGcm(key, 16)) g.Decrypt(nonce, ct, tag, plain);
        return JsonNode.Parse(plain) as JsonObject ?? throw new InvalidOperationException();
    }
}

/// Pull / merge / push. Local changes are pushed a few seconds later (at most every 45 s).
/// Runs on the UI thread (awaits return there).
public sealed class SyncService
{
    public static SyncService Shared { get; } = new();

    /// CI only: the mock server on this machine, used with a dummy token.
    public string? TestBase { get; set; }
    public string Status { get; private set; } = "Kapalı";
    public event Action? Changed;

    private bool _pending, _running, _again;
    private DateTime _lastRun = DateTime.MinValue, _lastPush = DateTime.MinValue;
    private CancellationTokenSource? _pushCts;
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(25),
    };

    public bool Enabled => TestBase != null || SyncConfig.Token.Length > 0;

    public void LocalChanged()
    {
        if (!Enabled) return;
        _pending = true;
        var delay = Math.Max(6, 45 - (DateTime.UtcNow - _lastPush).TotalSeconds);
        Schedule(delay);
    }

    private void Schedule(double seconds)
    {
        _pushCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pushCts = cts;
        _ = Later();

        async Task Later()
        {
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token); }
            catch (TaskCanceledException) { return; }
            await SyncNow();
        }
    }

    public void Flush()
    {
        if (!Enabled || !_pending) return;
        _pushCts?.Cancel();
        _ = SyncNow();
    }

    public void Pull(double minGapSeconds = 20)
    {
        if (!Enabled || (DateTime.UtcNow - _lastRun).TotalSeconds < minGapSeconds) return;
        _ = SyncNow();
    }

    public async Task SyncNow()
    {
        if (!Enabled) return;
        if (_running) { _again = true; return; }
        _running = true;
        try
        {
            do
            {
                _again = false;
                await SyncOnce();
            } while (_again);
        }
        finally { _running = false; }
    }

    private async Task SyncOnce()
    {
        var store = Store.Shared;
        if (store.Account is not { } account) return;
        _lastRun = DateTime.UtcNow;
        var hadPending = _pending;
        _pending = false;
        var fileId = SyncCrypto.FileId(account);
        var key = SyncCrypto.Key(account);
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var (remote, sha) = await Fetch(fileId, key);
                var local = store.SyncDoc();
                var merged = SyncDoc.Merge(local, remote);
                if (!SyncDoc.Same(merged, local))
                {
                    store.ApplySyncDoc(merged);
                    Changed?.Invoke();
                }
                if (SyncDoc.Same(merged, remote)) { Status = "Güncel"; return; }
                var code = await Upload(fileId, SyncCrypto.Seal(merged, key), sha);
                if (code is 200 or 201)
                {
                    _lastPush = DateTime.UtcNow;
                    Status = "Güncel";
                    return;
                }
                if (code is not (409 or 422)) throw new HttpRequestException($"HTTP {code}");
            }
            throw new HttpRequestException("çakışma");
        }
        catch (Exception)
        {
            Status = "Bağlanamadı";
            if (hadPending)
            {
                _pending = true;
                Schedule(60);
            }
        }
    }

    private string Base => TestBase ?? SyncConfig.Api;
    private string AuthToken => TestBase != null ? "test" : SyncConfig.Token;

    private HttpRequestMessage Request(HttpMethod method, string fileId, string query = "")
    {
        var r = new HttpRequestMessage(method, $"{Base}/repos/{SyncConfig.Repo}/contents/v1/{fileId}.json{query}");
        r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AuthToken);
        r.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        r.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        r.Headers.TryAddWithoutValidation("User-Agent", "MN-IPTV-Windows");
        r.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        return r;
    }

    /// A stored file that cannot be decrypted is treated as empty (and overwritten).
    private async Task<(JsonObject?, string?)> Fetch(string fileId, byte[] key)
    {
        using var resp = await Http.SendAsync(Request(HttpMethod.Get, fileId, "?t=" + SyncDoc.NowMs));
        if (resp.StatusCode == HttpStatusCode.NotFound) return (null, null);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
        var o = JsonNode.Parse(await resp.Content.ReadAsStringAsync()) as JsonObject;
        var sha = SyncDoc.Str(o?["sha"]);
        var b64 = (SyncDoc.Str(o?["content"]) ?? "").Replace("\n", "").Replace("\\n", "");
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            return (SyncCrypto.Unseal(raw, key), sha);
        }
        catch (Exception) { return (null, sha); }
    }

    private async Task<int> Upload(string fileId, string sealedText, string? sha)
    {
        var body = new JsonObject { ["message"] = "sync windows", ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(sealedText)) };
        if (sha != null) body["sha"] = sha;
        using var req = Request(HttpMethod.Put, fileId);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req);
        return (int)resp.StatusCode;
    }
}
