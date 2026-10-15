import AppKit
import Foundation

struct TrackOption: Hashable {
    let id: Int
    let label: String
    let selected: Bool
}

/// One libmpv instance and video surface for the whole app. Re-creating the OpenGL
/// surface per screen left later screens black, and reusing it makes channel changes faster.
@MainActor
enum PlaybackEngine {
    private static var instance: MaviMPVBridge?
    private(set) static var failure: String?

    static var shared: MaviMPVBridge? {
        if let i = instance { return i }
        if failure != nil { return nil }
        let created = MaviMPVBridge()
        if created.ready {
            instance = created
        } else {
            failure = "Oynatıcı başlatılamadı (\(created.initializationStage) \(created.initializationError))"
            created.shutdown()
        }
        return instance
    }
}

/// Drives the shared MaviMPVBridge (libmpv) for live channels, movies and series.
@MainActor
final class PlayerController: ObservableObject {
    enum Status: Equatable { case loading, playing, paused, failed(String) }

    @Published private(set) var status: Status = .loading
    @Published private(set) var position: Double = 0
    @Published private(set) var duration: Double = 0
    @Published private(set) var videoHeight: Int = 0
    @Published private(set) var audioTracks: [TrackOption] = []
    @Published private(set) var subtitleTracks: [TrackOption] = []
    @Published private(set) var message: String?
    @Published private(set) var volume: Double
    @Published private(set) var muted = false
    @Published var fillScreen = false {
        didSet { _ = bridge?.set(number: fillScreen ? 1 : 0, property: "panscan") }
    }

    // live
    @Published private(set) var channels: [MediaEntry] = []
    @Published private(set) var current = 0
    @Published private(set) var epg: EpgNow?
    private(set) var previous = -1
    private var playing = -1
    private var liveExt = "ts"
    private var liveRetries = 0
    private var epgTask: Task<Void, Never>?
    private var tuneTask: Task<Void, Never>?
    private var retryTask: Task<Void, Never>?

    // vod
    let request: PlayRequest
    @Published private(set) var episodeIndex = 0
    private var vodRetries = 0
    private var pendingStart: Double = 0
    private var lastSave = Date.distantPast

    private let model: AppModel
    private var bridge: MaviMPVBridge?
    private var timer: Timer?
    private var loadedOnce = false
    private var currentURL = ""
    var onFinished: (() -> Void)?

    var isLive: Bool { request.mode == .live }
    var surface: NSView { bridge?.surface ?? NSView() }
    var engineReady: Bool { bridge?.ready ?? false }

    var currentEpisode: Episode? { request.episodes.indices.contains(episodeIndex) ? request.episodes[episodeIndex] : nil }
    var hasNextEpisode: Bool { request.mode == .series && episodeIndex + 1 < request.episodes.count }
    var hasPreviousEpisode: Bool { request.mode == .series && episodeIndex > 0 }

    var title: String {
        switch request.mode {
        case .live: return channels.indices.contains(current) ? channels[current].name : ""
        case .movie: return request.movie?.name ?? ""
        case .series: return request.seriesName
        }
    }

    var subtitle: String {
        guard request.mode == .series, let ep = currentEpisode else { return "" }
        return "Sezon \(ep.season) · Bölüm \(ep.number) — \(ep.title)"
    }

    init(request: PlayRequest, model: AppModel) {
        self.request = request
        self.model = model
        self.volume = model.store.volume
        if let engine = PlaybackEngine.shared {
            bridge = engine
            _ = engine.stopPlayback()
            _ = engine.drainEvents()
            _ = engine.set(number: volume, property: "volume")
            _ = engine.set(text: "no", property: "mute")
            _ = engine.set(number: 0, property: "panscan")
        } else {
            status = .failed(PlaybackEngine.failure ?? "Oynatıcı başlatılamadı")
        }
    }

    func start() {
        guard bridge != nil else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        switch request.mode {
        case .live:
            channels = request.channels
            tune(min(max(0, request.index), max(0, channels.count - 1)), immediate: true)
        case .movie:
            guard let m = request.movie, let api = model.api else { return }
            load(api.movieURL(m.id, ext: m.ext ?? "mp4"), start: request.start)
        case .series:
            playEpisode(request.index, start: request.start)
        }
    }

    func stop() {
        saveProgress()
        timer?.invalidate()
        timer = nil
        tuneTask?.cancel()
        retryTask?.cancel()
        epgTask?.cancel()
        _ = bridge?.stopPlayback()
        _ = bridge?.set(text: "no", property: "pause")
        bridge = nil
    }

    private func load(_ url: String, start: Double) {
        guard let bridge = bridge else { return }
        currentURL = url
        message = nil
        status = .loading
        position = start
        duration = 0
        pendingStart = start
        _ = bridge.set(text: start > 1 ? String(format: "%.1f", start) : "none", property: "start")
        _ = bridge.set(text: "auto", property: "aid")
        _ = bridge.set(text: "auto", property: "sid")
        _ = bridge.load(address: url)
        _ = bridge.set(text: "no", property: "pause")
    }

    // MARK: polling

    private func tick() {
        guard let bridge = bridge else { return }
        for event in bridge.drainEvents() {
            switch event["kind"] as? String {
            case "loaded":
                loadedOnce = true
                message = nil
                vodRetries = 0
                if isLive { liveRetries = 0 }
                _ = bridge.set(text: "none", property: "start")
                refreshTracks()
            case "failed":
                handleFailure(code: (event["code"] as? NSNumber)?.intValue ?? 0)
            case "ended":
                handleEnded()
            default:
                break
            }
        }
        let pos = bridge.number("time-pos")
        if pos.isFinite { position = pos }
        let dur = bridge.number("duration")
        duration = dur.isFinite ? dur : 0
        let h = bridge.number("video-params/h")
        videoHeight = h.isFinite ? Int(h) : 0
        if case .failed = status { return }
        let paused = bridge.flag("pause")
        let idle = bridge.flag("core-idle")
        let buffering = bridge.flag("paused-for-cache") || bridge.flag("seeking")
        if paused { status = .paused }
        else if idle || buffering || !pos.isFinite { status = .loading }
        else { status = .playing }
        if !isLive && Date().timeIntervalSince(lastSave) > 10 && position > 15 {
            saveProgress()
        }
    }

    func refreshTracks() {
        guard let bridge = bridge else { return }
        var audio: [TrackOption] = [], subs: [TrackOption] = []
        for t in bridge.tracks() {
            let type = t["type"] as? String ?? ""
            let id = (t["id"] as? NSNumber)?.intValue ?? 0
            let selected = (t["selected"] as? NSNumber)?.boolValue ?? false
            var parts: [String] = []
            if let title = t["title"] as? String, !title.isEmpty { parts.append(title) }
            if let lang = t["lang"] as? String, !lang.isEmpty, lang != "und" {
                let name = Locale(identifier: "tr_TR").localizedString(forLanguageCode: lang) ?? lang
                if !parts.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) { parts.append(name) }
            }
            if type == "audio", let ch = (t["demux-channel-count"] as? NSNumber)?.intValue {
                switch ch { case 1: parts.append("Mono"); case 2: parts.append("Stereo"); case 6: parts.append("5.1"); case 8: parts.append("7.1"); default: break }
            }
            let label = parts.isEmpty ? "Parça \(id)" : parts.joined(separator: " · ")
            if type == "audio" { audio.append(TrackOption(id: id, label: label, selected: selected)) }
            if type == "sub" { subs.append(TrackOption(id: id, label: label, selected: selected)) }
        }
        audioTracks = audio
        subtitleTracks = subs
    }

    func selectAudio(_ id: Int) {
        _ = bridge?.set(text: String(id), property: "aid")
        refreshTracks()
    }

    func selectSubtitle(_ id: Int?) {
        _ = bridge?.set(text: id.map(String.init) ?? "no", property: "sid")
        refreshTracks()
    }

    // MARK: errors / end

    private func handleFailure(code: Int) {
        if isLive {
            guard channels.indices.contains(current) else { return }
            let ch = channels[current]
            if liveExt == "ts" {
                liveExt = "m3u8"
                if let api = model.api { load(api.liveURL(ch.id, ext: "m3u8"), start: 0) }
                return
            }
            if liveRetries < 2 {
                liveRetries += 1
                liveExt = "ts"
                message = "Bağlantı yeniden kuruluyor…"
                retryTask?.cancel()
                retryTask = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: 2_000_000_000)
                    guard let self = self, !Task.isCancelled, self.channels.indices.contains(self.current),
                          self.channels[self.current].id == ch.id, let api = self.model.api else { return }
                    self.load(api.liveURL(ch.id, ext: "ts"), start: 0)
                }
                return
            }
            status = .failed("Bu kanal açılamadı.")
        } else {
            if vodRetries < 2 {
                vodRetries += 1
                message = "Bağlantı yeniden kuruluyor…"
                let resumeAt = position
                let url = currentURL
                retryTask?.cancel()
                retryTask = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: 2_000_000_000)
                    guard let self = self, !Task.isCancelled else { return }
                    self.load(url, start: resumeAt)
                }
                return
            }
            status = .failed("Oynatılamadı. Sunucu yayını vermedi veya biçim desteklenmiyor.")
        }
    }

    private func handleEnded() {
        switch request.mode {
        case .live:
            // A live stream that ends is usually a dropped connection: reconnect.
            if channels.indices.contains(current) { startLive() }
        case .movie:
            if let m = request.movie { model.store.removeProgress(m.key) }
            onFinished?()
        case .series:
            guard let ep = currentEpisode else { return }
            model.store.setWatched(ep.id)
            if hasNextEpisode {
                playEpisode(episodeIndex + 1, start: 0)
            } else {
                model.store.removeProgress("SERIES:\(request.seriesId)")
                onFinished?()
            }
        }
    }

    // MARK: live

    func tune(_ index: Int, immediate: Bool) {
        guard channels.indices.contains(index) else { return }
        current = index
        epg = nil
        status = .loading
        tuneTask?.cancel()
        retryTask?.cancel()
        if immediate {
            startLive()
        } else {
            tuneTask = Task { [weak self] in
                try? await Task.sleep(nanoseconds: 450_000_000)
                guard let self = self, !Task.isCancelled else { return }
                self.startLive()
            }
        }
    }

    func zap(_ delta: Int, repeating: Bool) {
        let n = channels.count
        guard n > 0 else { return }
        tune(((current + delta) % n + n) % n, immediate: !repeating)
    }

    func goPrevious() {
        if channels.indices.contains(previous) { tune(previous, immediate: true) }
    }

    func restartLive() { startLive() }

    private func startLive() {
        guard channels.indices.contains(current), let api = model.api else { return }
        if playing != current {
            if playing >= 0 { previous = playing }
            playing = current
        }
        liveExt = "ts"
        liveRetries = 0
        let ch = channels[current]
        load(api.liveURL(ch.id, ext: liveExt), start: 0)
        epgTask?.cancel()
        epgTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 600_000_000)
            guard let self = self, !Task.isCancelled, let api = self.model.api else { return }
            let result = try? await api.shortEpg(ch.id)
            if Task.isCancelled { return }
            if self.channels.indices.contains(self.current), self.channels[self.current].id == ch.id {
                self.epg = result ?? nil
            }
        }
    }

    func jump(toNumber n: Int) -> Bool {
        var idx = channels.firstIndex { $0.number == n }
        if idx == nil && !channels.contains(where: { $0.number > 0 }) && n >= 1 && n <= channels.count { idx = n - 1 }
        guard let i = idx else { return false }
        tune(i, immediate: true)
        return true
    }

    // MARK: vod

    func playEpisode(_ index: Int, start: Double) {
        guard request.episodes.indices.contains(index), let api = model.api else { return }
        if episodeIndex != index { saveProgress() }
        episodeIndex = index
        let ep = request.episodes[index]
        model.store.saveProgress(seriesProgress(ep, position: start, duration: Double(ep.durationSec)))
        load(api.episodeURL(ep.id, ext: ep.ext), start: start)
    }

    func togglePause() {
        guard let bridge = bridge else { return }
        if case .failed = status {
            if isLive { startLive() } else { load(currentURL, start: position) }
            return
        }
        _ = bridge.set(text: bridge.flag("pause") ? "no" : "yes", property: "pause")
    }

    func seek(by seconds: Double) {
        guard !isLive else { return }
        var target = max(0, position + seconds)
        if duration > 0 { target = min(target, max(0, duration - 1)) }
        position = target
        _ = bridge?.seek(seconds: target, absolute: true)
    }

    func seek(to seconds: Double) {
        guard !isLive else { return }
        position = seconds
        _ = bridge?.seek(seconds: seconds, absolute: true)
    }

    func changeVolume(by delta: Double) {
        setVolume(volume + delta)
    }

    func setVolume(_ value: Double) {
        volume = min(100, max(0, value))
        model.store.volume = volume
        if muted && volume > 0 { muted = false; _ = bridge?.set(text: "no", property: "mute") }
        _ = bridge?.set(number: volume, property: "volume")
    }

    func toggleMute() {
        muted.toggle()
        _ = bridge?.set(text: muted ? "yes" : "no", property: "mute")
    }

    private func seriesProgress(_ ep: Episode, position: Double, duration: Double) -> Progress {
        Progress(key: "SERIES:\(request.seriesId)", kind: .series, id: request.seriesId, title: request.seriesName,
                 subtitle: "S\(ep.season) B\(ep.number) · \(ep.title)", image: request.seriesCover, ext: ep.ext,
                 episodeId: ep.id, season: ep.season, episode: ep.number, position: position, duration: duration,
                 updatedAt: Date().timeIntervalSince1970)
    }

    func saveProgress() {
        lastSave = Date()
        guard !isLive, loadedOnce || position > 0 else { return }
        let store = model.store
        let pos = position, dur = duration
        let nearEnd = dur > 0 && pos >= dur * 0.94
        switch request.mode {
        case .live:
            return
        case .movie:
            guard let m = request.movie else { return }
            if nearEnd {
                store.removeProgress(m.key)
            } else if pos > 15 {
                store.saveProgress(Progress(key: m.key, kind: .movie, id: m.id, title: m.name, subtitle: nil, image: m.icon,
                                            ext: m.ext, episodeId: nil, season: 0, episode: 0, position: pos, duration: dur,
                                            updatedAt: Date().timeIntervalSince1970))
            }
        case .series:
            guard let ep = currentEpisode else { return }
            if nearEnd {
                store.setWatched(ep.id)
                if hasNextEpisode {
                    let next = request.episodes[episodeIndex + 1]
                    store.saveProgress(seriesProgress(next, position: 0, duration: Double(next.durationSec)))
                } else {
                    store.removeProgress("SERIES:\(request.seriesId)")
                }
            } else if pos > 15 {
                store.setEpisodePosition(ep.id, pos)
                store.saveProgress(seriesProgress(ep, position: pos, duration: dur))
            }
        }
    }
}
