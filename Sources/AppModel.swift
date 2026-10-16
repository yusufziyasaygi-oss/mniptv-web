import AppKit
import Foundation
import SwiftUI

enum Section: Int, CaseIterable {
    case continueWatching, live, movies, series, favorites, search

    var title: String {
        switch self {
        case .continueWatching: return "İzlemeye Devam"
        case .live: return "Canlı TV"
        case .movies: return "Filmler"
        case .series: return "Diziler"
        case .favorites: return "Favoriler"
        case .search: return "Ara"
        }
    }

    var symbol: String {
        switch self {
        case .continueWatching: return "clock.arrow.circlepath"
        case .live: return "dot.radiowaves.left.and.right"
        case .movies: return "film"
        case .series: return "rectangle.stack"
        case .favorites: return "star"
        case .search: return "magnifyingglass"
        }
    }

    var kind: Kind? {
        switch self {
        case .live: return .live
        case .movies: return .movie
        case .series: return .series
        default: return nil
        }
    }
}

enum Tile: Hashable {
    case media(MediaEntry, favorite: Bool, showKind: Bool)
    case resume(Progress)

    var key: String {
        switch self {
        case .media(let e, _, _): return e.key
        case .resume(let p): return "R:" + p.key
        }
    }

    var isLive: Bool {
        if case .media(let e, _, _) = self { return e.kind == .live }
        return false
    }
}

struct PlayRequest: Equatable {
    enum Mode { case live, movie, series }
    let token = UUID()
    let mode: Mode
    var channels: [MediaEntry] = []
    var index: Int = 0
    var listTitle: String = ""
    var movie: MediaEntry?
    var start: Double = 0
    var seriesId: String = ""
    var seriesName: String = ""
    var seriesCover: String?
    var episodes: [Episode] = []

    static func == (a: PlayRequest, b: PlayRequest) -> Bool { a.token == b.token }
}

enum Route: Equatable {
    case series(MediaEntry)
    case player(PlayRequest)
}

@MainActor
final class AppModel: ObservableObject {
    static let allCategoryId = "__all__"

    @Published private(set) var account: Account?
    @Published private(set) var section: Section = .live
    @Published private(set) var categories: [Category] = []
    @Published private(set) var selectedCategory: Int = -1
    @Published private(set) var tiles: [Tile] = []
    @Published private(set) var contentTitle = ""
    @Published private(set) var loading = false
    @Published private(set) var emptyMessage: String?
    @Published var query = ""
    @Published private(set) var route: [Route] = []
    @Published var toast: String?
    /// Bumped when favourites/progress change so detail screens refresh.
    @Published private(set) var revision = 0

    let store = Store.shared
    private(set) var api: XtreamAPI?
    private var categoryCache: [Kind: [Category]] = [:]
    private var streamCache: [String: [MediaEntry]] = [:]
    private var seriesCache: [String: SeriesInfo] = [:]
    private var categoryTask: Task<Void, Never>?
    private var contentTask: Task<Void, Never>?
    private var toastTask: Task<Void, Never>?

    init() {
        if let acc = store.account {
            account = acc
            api = XtreamAPI(account: acc)
        }
    }

    /// The player currently on screen (used by the smoke test and menu commands).
    weak var activePlayer: PlayerController?
    private var didStart = false

    func startIfNeeded() {
        guard !didStart else { return }
        didStart = true
        start()
    }

    /// Menu command: leave player/series screens and open a section.
    func goToSection(_ s: Section) {
        guard account != nil else { return }
        if !route.isEmpty {
            route = []
            refreshAfterReturn()
        }
        select(s)
    }

    func start() {
        didStart = true
        guard account != nil else { return }
        select(store.progressList.isEmpty ? .live : .continueWatching, force: true)
    }

    // MARK: account

    func login(server: String, username: String, password: String) async -> String? {
        let acc = Account(server: Account.normalize(server),
                          username: username.trimmingCharacters(in: .whitespacesAndNewlines),
                          password: password.trimmingCharacters(in: .whitespacesAndNewlines))
        if acc.server.isEmpty || acc.username.isEmpty || acc.password.isEmpty {
            return "Sunucu adresi, kullanıcı adı ve şifre gerekli"
        }
        let candidate = XtreamAPI(account: acc)
        do {
            if let error = try await candidate.authenticate() { return error }
        } catch {
            return "Sunucuya bağlanılamadı: \(error.localizedDescription)"
        }
        store.account = acc
        api = candidate
        clearCache()
        route = []
        account = acc
        start()
        return nil
    }

    func signOut() {
        categoryTask?.cancel()
        contentTask?.cancel()
        route = []
        account = nil
        api = nil
        clearCache()
    }

    func clearCache() {
        categoryCache.removeAll()
        streamCache.removeAll()
        seriesCache.removeAll()
    }

    func reloadSection() {
        clearCache()
        select(section, force: true)
        showToast("Liste yenileniyor")
    }

    // MARK: data

    private func requireAPI() throws -> XtreamAPI {
        guard let api = api else { throw APIError.badResponse("Hesap bilgisi yok") }
        return api
    }

    func categories(_ kind: Kind) async throws -> [Category] {
        if let c = categoryCache[kind] { return c }
        let list = try await requireAPI().categories(kind)
        categoryCache[kind] = list
        return list
    }

    func streams(_ kind: Kind, _ categoryId: String?) async throws -> [MediaEntry] {
        let key = "\(kind.rawValue)|\(categoryId ?? "*")"
        if let c = streamCache[key] { return c }
        let list = try await requireAPI().streams(kind, categoryId: categoryId)
        streamCache[key] = list
        return list
    }

    func seriesInfo(_ id: String) async throws -> SeriesInfo {
        if let c = seriesCache[id] { return c }
        let info = try await requireAPI().seriesInfo(id)
        seriesCache[id] = info
        return info
    }

    // MARK: sections

    func select(_ s: Section, force: Bool = false) {
        guard force || s != section || (s.kind != nil && categories.isEmpty) else { return }
        section = s
        categoryTask?.cancel()
        contentTask?.cancel()
        categories = []
        selectedCategory = -1
        emptyMessage = nil
        switch s {
        case .live, .movies, .series:
            tiles = []
            contentTitle = ""
            loadCategories(s.kind!)
        case .continueWatching:
            showContinue()
        case .favorites:
            showFavorites()
        case .search:
            contentTitle = "Arama"
            if query.trimmingCharacters(in: .whitespaces).count >= 2 { runSearch() }
            else { setTiles([], empty: "En az 2 harf yaz") }
        }
    }

    private func loadCategories(_ kind: Kind) {
        loading = true
        categoryTask = Task { [weak self] in
            guard let self = self else { return }
            do {
                let cats = try await self.categories(kind)
                if Task.isCancelled { return }
                self.categories = cats + [Category(id: Self.allCategoryId, name: "Tümü")]
                self.selectCategory(0, force: true)
            } catch {
                if Task.isCancelled { return }
                self.setTiles([], empty: "Liste alınamadı: \(error.localizedDescription)\nBölümü tekrar seçerek yeniden dene.")
            }
        }
    }

    func selectCategory(_ index: Int, force: Bool = false) {
        guard let kind = section.kind, categories.indices.contains(index) else { return }
        if !force && index == selectedCategory && !tiles.isEmpty { return }
        selectedCategory = index
        let cat = categories[index]
        contentTitle = cat.name
        loading = true
        emptyMessage = nil
        contentTask?.cancel()
        contentTask = Task { [weak self] in
            guard let self = self else { return }
            do {
                let list = try await self.streams(kind, cat.id == Self.allCategoryId ? nil : cat.id)
                if Task.isCancelled { return }
                let favs = self.store.favoriteKeys
                self.setTiles(list.map { .media($0, favorite: favs.contains($0.key), showKind: false) },
                              empty: "Bu kategoride içerik yok")
            } catch {
                if Task.isCancelled { return }
                self.setTiles([], empty: "İçerik alınamadı: \(error.localizedDescription)\nKategoriyi tekrar seçerek yeniden dene.")
            }
        }
    }

    func showContinue() {
        contentTitle = Section.continueWatching.title
        setTiles(store.progressList.map { .resume($0) }, empty: "Yarım kalan film veya dizi yok.")
    }

    func showFavorites() {
        contentTitle = Section.favorites.title
        setTiles(store.favorites.map { .media($0, favorite: true, showKind: $0.kind != .live) },
                 empty: "Favori yok. Bir kanalın, filmin veya dizinin üzerinde sağ tıklayarak ekleyebilirsin.")
    }

    func runSearch() {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines)
        contentTask?.cancel()
        guard q.count >= 2 else {
            contentTitle = "Arama"
            setTiles([], empty: "En az 2 harf yaz")
            return
        }
        contentTitle = "Aranıyor: \(q)"
        loading = true
        emptyMessage = nil
        contentTask = Task { [weak self] in
            guard let self = self else { return }
            let needle = normalizeForSearch(q)
            var results: [MediaEntry] = []
            var failures = 0
            for kind in Kind.allCases {
                do {
                    let all = try await self.streams(kind, nil)
                    if Task.isCancelled { return }
                    var added = 0
                    for e in all where added < 300 {
                        if normalizeForSearch(e.name).contains(needle) {
                            results.append(e)
                            added += 1
                        }
                    }
                } catch {
                    if Task.isCancelled { return }
                    failures += 1
                }
            }
            if Task.isCancelled { return }
            let favs = self.store.favoriteKeys
            self.contentTitle = "\"\(q)\" · \(results.count) sonuç"
            self.setTiles(results.map { .media($0, favorite: favs.contains($0.key), showKind: $0.kind != .live) },
                          empty: failures == Kind.allCases.count ? "Arama yapılamadı, bağlantıyı kontrol et." : "Sonuç bulunamadı")
        }
    }

    private func setTiles(_ list: [Tile], empty: String) {
        loading = false
        tiles = list
        emptyMessage = list.isEmpty ? empty : nil
    }

    /// Called when coming back to the catalogue: progress and favourites may have changed.
    func refreshAfterReturn() {
        revision += 1
        switch section {
        case .continueWatching: showContinue()
        case .favorites: showFavorites()
        default:
            let favs = store.favoriteKeys
            tiles = tiles.map { tile in
                if case .media(let e, let fav, let showKind) = tile, fav != favs.contains(e.key) {
                    return .media(e, favorite: !fav, showKind: showKind)
                }
                return tile
            }
        }
    }

    // MARK: actions

    func open(_ tile: Tile) {
        switch tile {
        case .media(let e, _, _):
            switch e.kind {
            case .live:
                let lives = tiles.compactMap { t -> MediaEntry? in
                    if case .media(let x, _, _) = t, x.kind == .live { return x }
                    return nil
                }
                let index = lives.firstIndex { $0.key == e.key } ?? 0
                push(.player(PlayRequest(mode: .live, channels: lives, index: index, listTitle: contentTitle)))
            case .movie:
                playMovie(e, start: nil)
            case .series:
                push(.series(e))
            }
        case .resume(let p):
            if p.kind == .movie {
                let e = MediaEntry(kind: .movie, id: p.id, name: p.title, icon: p.image, ext: p.ext ?? "mp4", categoryId: nil)
                push(.player(PlayRequest(mode: .movie, movie: e, start: p.position)))
            } else {
                push(.series(MediaEntry(kind: .series, id: p.id, name: p.title, icon: p.image, ext: nil, categoryId: nil)))
            }
        }
    }

    /// start == nil: resume from saved progress when there is one.
    func playMovie(_ e: MediaEntry, start: Double?) {
        let saved = store.progress(e.key)?.position ?? 0
        push(.player(PlayRequest(mode: .movie, movie: e, start: start ?? (saved > 30 ? saved : 0))))
    }

    func playSeries(id: String, name: String, cover: String?, episodes: [Episode], index: Int, start: Double) {
        push(.player(PlayRequest(mode: .series, index: index, start: start, seriesId: id, seriesName: name,
                                 seriesCover: cover, episodes: episodes)))
    }

    func toggleFavorite(_ entry: MediaEntry) {
        let added = store.toggleFavorite(entry)
        showToast(added ? "Favorilere eklendi" : "Favorilerden çıkarıldı")
        refreshAfterReturn()
    }

    func removeProgress(_ p: Progress) {
        store.removeProgress(p.key)
        refreshAfterReturn()
    }

    func push(_ r: Route) {
        if case .player(let request) = r, store.playbackApp == "mnplayer" {
            openInMNPlayer(request)
            return
        }
        route.append(r)
    }

    var usesMNPlayer: Bool { store.playbackApp == "mnplayer" }

    func setPlaybackApp(mnPlayer: Bool) {
        store.playbackApp = mnPlayer ? "mnplayer" : "builtin"
        revision += 1
        showToast(mnPlayer ? "Oynatıcı: MN Player" : "Oynatıcı: MN IPTV")
    }

    /// Address of the item a play request would start, for an external player.
    func streamAddress(_ r: PlayRequest) -> String? {
        guard let api = api else { return nil }
        switch r.mode {
        case .live:
            guard r.channels.indices.contains(r.index) else { return nil }
            return api.liveURL(r.channels[r.index].id, ext: "ts")
        case .movie:
            guard let m = r.movie else { return nil }
            return api.movieURL(m.id, ext: m.ext ?? "mp4")
        case .series:
            guard r.episodes.indices.contains(r.index) else { return nil }
            let ep = r.episodes[r.index]
            store.saveProgress(Progress(key: "SERIES:\(r.seriesId)", kind: .series, id: r.seriesId, title: r.seriesName,
                                        subtitle: "S\(ep.season) B\(ep.number) · \(ep.title)", image: r.seriesCover, ext: ep.ext,
                                        episodeId: ep.id, season: ep.season, episode: ep.number, position: 0,
                                        duration: Double(ep.durationSec), updatedAt: Date().timeIntervalSince1970))
            return api.episodeURL(ep.id, ext: ep.ext)
        }
    }

    /// Last message shown for an MN Player hand-off (used by the self test).
    private(set) var lastExternalResult = ""

    func openInMNPlayer(_ r: PlayRequest) {
        let installed = URL(fileURLWithPath: "/Applications/MN Player.app")
        let appURL = FileManager.default.fileExists(atPath: installed.path)
            ? installed : NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.mavinokta.player")
        guard let appURL = appURL else {
            lastExternalResult = "missing"
            showToast("MN Player bulunamadı. Hesap menüsünden oynatıcıyı MN IPTV yapabilirsin.")
            return
        }
        guard let address = streamAddress(r) else { return }
        var link = URLComponents()
        link.scheme = "mnplayer"
        link.host = "play"
        link.queryItems = [URLQueryItem(name: "url", value: address)]
        guard let url = link.url else { return }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.open([url], withApplicationAt: appURL, configuration: configuration) { [weak self] _, error in
            Task { @MainActor in
                self?.lastExternalResult = error == nil ? "opened" : "failed"
                self?.showToast(error == nil ? "MN Player'da açıldı" : "MN Player açılamadı")
                self?.refreshAfterReturn()
            }
        }
    }

    func back() {
        guard !route.isEmpty else { return }
        route.removeLast()
        refreshAfterReturn()
    }

    func showToast(_ text: String) {
        toast = text
        toastTask?.cancel()
        toastTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 2_000_000_000)
            if !Task.isCancelled { self?.toast = nil }
        }
    }
}
