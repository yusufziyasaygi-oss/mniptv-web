using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MNIPTV;

public readonly record struct TrackOption(int Id, string Label, bool Selected);

/// One libmpv instance for the whole app, drawing into the persistent video window.
public static class PlaybackEngine
{
    private static MpvEngine? _instance;
    public static string? Failure { get; private set; }
    public static IntPtr VideoWindow { get; set; }
    public static MpvEngine? Peek => _instance;

    public static MpvEngine? Shared
    {
        get
        {
            if (_instance != null) return _instance;
            if (Failure != null) return null;
            if (VideoWindow == IntPtr.Zero) { Failure = "Oynatıcı başlatılamadı (video penceresi yok)"; return null; }
            var ca = Path.Combine(AppContext.BaseDirectory, "cacert.pem");
            var created = new MpvEngine(VideoWindow, ca);
            if (created.Ready) _instance = created;
            else Failure = $"Oynatıcı başlatılamadı ({created.Error})";
            return _instance;
        }
    }

    public static void Shutdown()
    {
        _instance?.Shutdown();
        _instance = null;
    }
}

public enum PlayStatus { Loading, Playing, Paused, Failed }

/// Drives the shared mpv engine for live channels, movies and series.
public sealed class PlayerController
{
    public event Action? Changed;

    public PlayStatus Status { get; private set; } = PlayStatus.Loading;
    public string FailureText { get; private set; } = "";
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public int VideoHeight { get; private set; }
    public List<TrackOption> AudioTracks { get; private set; } = new();
    public List<TrackOption> SubtitleTracks { get; private set; } = new();
    public string? Message { get; private set; }
    public double Volume { get; private set; }
    public bool Muted { get; private set; }
    private bool _fill;
    public bool FillScreen
    {
        get => _fill;
        set { _fill = value; _engine?.Set("panscan", value ? 1.0 : 0.0); }
    }

    // live
    public List<MediaEntry> Channels { get; private set; } = new();
    public int Current { get; private set; }
    public EpgNow? Epg { get; private set; }
    public event Action? ChannelChanged;
    private int _previous = -1, _playing = -1;
    private string _liveExt = "ts";
    private int _liveRetries;
    private CancellationTokenSource? _epgCts, _tuneCts, _retryCts;

    // vod
    public PlayRequest Request { get; }
    public int EpisodeIndex { get; private set; }
    private int _vodRetries;
    private DateTime _lastSave = DateTime.MinValue;

    private readonly AppModel _model;
    private MpvEngine? _engine;
    private DispatcherTimer? _timer;
    private bool _loadedOnce;
    private string _currentUrl = "";
    public Action? OnFinished { get; set; }

    public bool IsLive => Request.Mode == PlayMode.Live;
    public Episode? CurrentEpisode => EpisodeIndex >= 0 && EpisodeIndex < Request.Episodes.Count ? Request.Episodes[EpisodeIndex] : null;
    public bool HasNextEpisode => Request.Mode == PlayMode.Series && EpisodeIndex + 1 < Request.Episodes.Count;
    public bool HasPreviousEpisode => Request.Mode == PlayMode.Series && EpisodeIndex > 0;
    public MediaEntry? CurrentChannel => Current >= 0 && Current < Channels.Count ? Channels[Current] : null;

    public string Title => Request.Mode switch
    {
        PlayMode.Live => CurrentChannel?.Name ?? "",
        PlayMode.Movie => Request.Movie?.Name ?? "",
        _ => Request.SeriesName,
    };

    public string Subtitle => Request.Mode == PlayMode.Series && CurrentEpisode is { } ep
        ? $"Sezon {ep.Season} · Bölüm {ep.Number} — {ep.Title}" : "";

    public PlayerController(PlayRequest request, AppModel model)
    {
        Request = request;
        _model = model;
        Volume = model.Store.Volume;
        if (PlaybackEngine.Shared is { } engine)
        {
            _engine = engine;
            engine.Stop();
            engine.DrainEvents();
            engine.Set("volume", Volume);
            engine.Set("mute", "no");
            engine.Set("panscan", 0.0);
        }
        else
        {
            Fail(PlaybackEngine.Failure ?? "Oynatıcı başlatılamadı");
        }
    }

    private void Notify() => Changed?.Invoke();

    private void Fail(string text)
    {
        Status = PlayStatus.Failed;
        FailureText = text;
    }

    public void Start()
    {
        if (_engine == null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        switch (Request.Mode)
        {
            case PlayMode.Live:
                Channels = Request.Channels;
                Tune(Math.Min(Math.Max(0, Request.Index), Math.Max(0, Channels.Count - 1)), immediate: true);
                break;
            case PlayMode.Movie:
                if (Request.Movie is { } m && _model.Api is { } api) Load(api.MovieUrl(m.Id, m.Ext ?? "mp4"), Request.Start);
                break;
            default:
                PlayEpisode(Request.Index, Request.Start);
                break;
        }
    }

    public void Stop()
    {
        SaveProgress();
        _timer?.Stop();
        _timer = null;
        _tuneCts?.Cancel();
        _retryCts?.Cancel();
        _epgCts?.Cancel();
        _engine?.Stop();
        _engine?.Set("pause", "no");
        _engine = null;
    }

    private void Load(string url, double start)
    {
        if (_engine == null) return;
        _currentUrl = url;
        Message = null;
        Status = PlayStatus.Loading;
        Position = start;
        Duration = 0;
        _engine.Set("start", start > 1 ? start.ToString("0.0", CultureInfo.InvariantCulture) : "none");
        _engine.Set("aid", "auto");
        _engine.Set("sid", "auto");
        _engine.Load(url);
        _engine.Set("pause", "no");
        Notify();
    }

    // polling

    private void Tick()
    {
        if (_engine is not { } engine) return;
        foreach (var ev in engine.DrainEvents())
        {
            switch (ev.Kind)
            {
                case MpvEventKind.Loaded:
                    _loadedOnce = true;
                    Message = null;
                    _vodRetries = 0;
                    if (IsLive) _liveRetries = 0;
                    engine.Set("start", "none");
                    RefreshTracks();
                    break;
                case MpvEventKind.Failed:
                    HandleFailure(ev.Code);
                    break;
                case MpvEventKind.Ended:
                    HandleEnded();
                    break;
            }
            if (_engine == null) return;
        }
        var pos = engine.Number("time-pos");
        if (double.IsFinite(pos)) Position = pos;
        var dur = engine.Number("duration");
        Duration = double.IsFinite(dur) ? dur : 0;
        var h = engine.Number("video-params/h");
        VideoHeight = double.IsFinite(h) ? (int)h : 0;
        if (Status != PlayStatus.Failed)
        {
            var paused = engine.Flag("pause");
            var idle = engine.Flag("core-idle");
            var buffering = engine.Flag("paused-for-cache") || engine.Flag("seeking");
            if (paused) Status = PlayStatus.Paused;
            else if (idle || buffering || !double.IsFinite(pos)) Status = PlayStatus.Loading;
            else Status = PlayStatus.Playing;
            if (!IsLive && (DateTime.UtcNow - _lastSave).TotalSeconds > 10 && Position > 15) SaveProgress();
        }
        Notify();
    }

    public void RefreshTracks()
    {
        if (_engine == null) return;
        var audio = new List<TrackOption>();
        var subs = new List<TrackOption>();
        foreach (var t in _engine.Tracks())
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(t.Title)) parts.Add(t.Title);
            if (!string.IsNullOrWhiteSpace(t.Lang) && t.Lang != "und")
            {
                var name = Fmt.Language(t.Lang);
                if (!parts.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase))) parts.Add(name);
            }
            if (t.Type == "audio")
            {
                var ch = t.Channels switch { 1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1", _ => null };
                if (ch != null) parts.Add(ch);
            }
            var label = parts.Count == 0 ? $"Parça {t.Id}" : string.Join(" · ", parts);
            if (t.Type == "audio") audio.Add(new TrackOption(t.Id, label, t.Selected));
            if (t.Type == "sub") subs.Add(new TrackOption(t.Id, label, t.Selected));
        }
        AudioTracks = audio;
        SubtitleTracks = subs;
    }

    public void SelectAudio(int id)
    {
        _engine?.Set("aid", id.ToString(CultureInfo.InvariantCulture));
        RefreshTracks();
    }

    public void SelectSubtitle(int? id)
    {
        _engine?.Set("sid", id?.ToString(CultureInfo.InvariantCulture) ?? "no");
        RefreshTracks();
    }

    // errors / end

    private async void HandleFailure(int code)
    {
        if (IsLive)
        {
            if (CurrentChannel is not { } ch) return;
            if (_liveExt == "ts")
            {
                _liveExt = "m3u8";
                if (_model.Api is { } a) Load(a.LiveUrl(ch.Id, "m3u8"), 0);
                return;
            }
            if (_liveRetries < 2)
            {
                _liveRetries++;
                _liveExt = "ts";
                Message = "Bağlantı yeniden kuruluyor…";
                _retryCts?.Cancel();
                var cts = _retryCts = new CancellationTokenSource();
                try { await Task.Delay(2000, cts.Token); } catch (TaskCanceledException) { return; }
                if (_engine == null || CurrentChannel?.Id != ch.Id || _model.Api is not { } api) return;
                Load(api.LiveUrl(ch.Id, "ts"), 0);
                return;
            }
            Fail("Bu kanal açılamadı.");
        }
        else
        {
            if (_vodRetries < 2)
            {
                _vodRetries++;
                Message = "Bağlantı yeniden kuruluyor…";
                var resumeAt = Position;
                var url = _currentUrl;
                _retryCts?.Cancel();
                var cts = _retryCts = new CancellationTokenSource();
                try { await Task.Delay(2000, cts.Token); } catch (TaskCanceledException) { return; }
                if (_engine == null) return;
                Load(url, resumeAt);
                return;
            }
            Fail("Oynatılamadı. Sunucu yayını vermedi veya biçim desteklenmiyor.");
        }
        Notify();
    }

    private void HandleEnded()
    {
        switch (Request.Mode)
        {
            case PlayMode.Live:
                // A live stream that ends is usually a dropped connection: reconnect.
                if (CurrentChannel != null) StartLive();
                break;
            case PlayMode.Movie:
                if (Request.Movie is { } m) _model.Store.RemoveProgress(m.Key);
                _loadedOnce = false;
                Position = 0;
                OnFinished?.Invoke();
                break;
            default:
                if (CurrentEpisode is not { } ep) return;
                _model.Store.SetWatched(ep.Id);
                if (HasNextEpisode) PlayEpisode(EpisodeIndex + 1, 0);
                else
                {
                    _model.Store.RemoveProgress("SERIES:" + Request.SeriesId);
                    _loadedOnce = false;
                    Position = 0;
                    OnFinished?.Invoke();
                }
                break;
        }
    }

    // live

    public async void Tune(int index, bool immediate)
    {
        if (index < 0 || index >= Channels.Count) return;
        Current = index;
        Epg = null;
        Status = PlayStatus.Loading;
        _tuneCts?.Cancel();
        _retryCts?.Cancel();
        ChannelChanged?.Invoke();
        Notify();
        if (immediate) { StartLive(); return; }
        var cts = _tuneCts = new CancellationTokenSource();
        try { await Task.Delay(450, cts.Token); } catch (TaskCanceledException) { return; }
        StartLive();
    }

    public void Zap(int delta, bool repeating)
    {
        var n = Channels.Count;
        if (n == 0) return;
        Tune(((Current + delta) % n + n) % n, immediate: !repeating);
    }

    public void GoPrevious()
    {
        if (_previous >= 0 && _previous < Channels.Count) Tune(_previous, immediate: true);
    }

    private async void StartLive()
    {
        if (CurrentChannel is not { } ch || _model.Api is not { } api || _engine == null) return;
        if (_playing != Current)
        {
            if (_playing >= 0) _previous = _playing;
            _playing = Current;
        }
        _liveExt = "ts";
        _liveRetries = 0;
        if (Status == PlayStatus.Failed) Status = PlayStatus.Loading;
        Load(api.LiveUrl(ch.Id, _liveExt), 0);
        _epgCts?.Cancel();
        var cts = _epgCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(600, cts.Token);
            var result = await api.ShortEpg(ch.Id, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (CurrentChannel?.Id == ch.Id) { Epg = result; Notify(); }
        }
        catch (Exception)
        {
            // EPG is optional.
        }
    }

    public bool JumpToNumber(int n)
    {
        var idx = Channels.FindIndex(c => c.Number == n);
        if (idx < 0 && !Channels.Any(c => c.Number > 0) && n >= 1 && n <= Channels.Count) idx = n - 1;
        if (idx < 0) return false;
        Tune(idx, immediate: true);
        return true;
    }

    // vod

    public void PlayEpisode(int index, double start)
    {
        if (index < 0 || index >= Request.Episodes.Count || _model.Api is not { } api) return;
        if (EpisodeIndex != index) SaveProgress();
        EpisodeIndex = index;
        var ep = Request.Episodes[index];
        _model.Store.SaveProgress(SeriesProgress(ep, start, ep.DurationSec));
        if (Status == PlayStatus.Failed) Status = PlayStatus.Loading;
        _loadedOnce = false;
        Load(api.EpisodeUrl(ep.Id, ep.Ext), start);
    }

    public void TogglePause()
    {
        if (_engine is not { } engine) return;
        if (Status == PlayStatus.Failed)
        {
            Status = PlayStatus.Loading;
            if (IsLive) StartLive();
            else Load(_currentUrl, Position);
            return;
        }
        engine.Set("pause", engine.Flag("pause") ? "no" : "yes");
    }

    public void SeekBy(double seconds)
    {
        if (IsLive) return;
        var target = Math.Max(0, Position + seconds);
        if (Duration > 0) target = Math.Min(target, Math.Max(0, Duration - 1));
        Position = target;
        _engine?.Seek(target);
    }

    public void SeekTo(double seconds)
    {
        if (IsLive) return;
        Position = seconds;
        _engine?.Seek(seconds);
    }

    public void ChangeVolume(double delta) => SetVolume(Volume + delta);

    public void SetVolume(double value)
    {
        Volume = Math.Min(100, Math.Max(0, value));
        _model.Store.Volume = Volume;
        if (Muted && Volume > 0) { Muted = false; _engine?.Set("mute", "no"); }
        _engine?.Set("volume", Volume);
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        _engine?.Set("mute", Muted ? "yes" : "no");
    }

    private Progress SeriesProgress(Episode ep, double position, double duration) => new()
    {
        Key = "SERIES:" + Request.SeriesId, Kind = Kind.Series, Id = Request.SeriesId, Title = Request.SeriesName,
        Subtitle = $"S{ep.Season} B{ep.Number} · {ep.Title}", Image = Request.SeriesCover, Ext = ep.Ext,
        EpisodeId = ep.Id, Season = ep.Season, Episode = ep.Number, Position = position, Duration = duration,
        UpdatedAt = Fmt.Now(),
    };

    public void SaveProgress()
    {
        _lastSave = DateTime.UtcNow;
        if (IsLive || !(_loadedOnce || Position > 0)) return;
        var store = _model.Store;
        double pos = Position, dur = Duration;
        var nearEnd = dur > 0 && pos >= dur * 0.94;
        if (Request.Mode == PlayMode.Movie)
        {
            if (Request.Movie is not { } m) return;
            if (nearEnd) store.RemoveProgress(m.Key);
            else if (pos > 15)
                store.SaveProgress(new Progress
                {
                    Key = m.Key, Kind = Kind.Movie, Id = m.Id, Title = m.Name, Image = m.Icon, Ext = m.Ext,
                    Position = pos, Duration = dur, UpdatedAt = Fmt.Now(),
                });
        }
        else if (Request.Mode == PlayMode.Series)
        {
            if (CurrentEpisode is not { } ep) return;
            if (nearEnd)
            {
                store.SetWatched(ep.Id);
                if (HasNextEpisode)
                {
                    var next = Request.Episodes[EpisodeIndex + 1];
                    store.SaveProgress(SeriesProgress(next, 0, next.DurationSec));
                }
                else store.RemoveProgress("SERIES:" + Request.SeriesId);
            }
            else if (pos > 15)
            {
                store.SetEpisodePosition(ep.Id, pos);
                store.SaveProgress(SeriesProgress(ep, pos, dur));
            }
        }
    }
}
