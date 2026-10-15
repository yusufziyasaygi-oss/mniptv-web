import Foundation

/// Persistent account, favourites and watch progress. Used from the main thread.
final class Store {
    static let shared = Store()

    private let defaults: UserDefaults
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    var account: Account? {
        get {
            guard let data = defaults.data(forKey: "account") else { return nil }
            return try? decoder.decode(Account.self, from: data)
        }
        set {
            if let value = newValue, let data = try? encoder.encode(value) {
                defaults.set(data, forKey: "account")
            } else {
                defaults.removeObject(forKey: "account")
            }
        }
    }

    // MARK: favourites

    var favorites: [MediaEntry] {
        guard let data = defaults.data(forKey: "favorites") else { return [] }
        return (try? decoder.decode([MediaEntry].self, from: data)) ?? []
    }

    var favoriteKeys: Set<String> { Set(favorites.map(\.key)) }

    func isFavorite(_ key: String) -> Bool { favorites.contains { $0.key == key } }

    /// Returns true when the entry is now a favourite.
    @discardableResult
    func toggleFavorite(_ entry: MediaEntry) -> Bool {
        var list = favorites
        let added: Bool
        if let i = list.firstIndex(where: { $0.key == entry.key }) {
            list.remove(at: i)
            added = false
        } else {
            list.insert(entry, at: 0)
            added = true
        }
        if let data = try? encoder.encode(list) { defaults.set(data, forKey: "favorites") }
        return added
    }

    // MARK: continue watching

    var progressList: [Progress] {
        guard let data = defaults.data(forKey: "progress") else { return [] }
        return ((try? decoder.decode([Progress].self, from: data)) ?? []).sorted { $0.updatedAt > $1.updatedAt }
    }

    func progress(_ key: String) -> Progress? { progressList.first { $0.key == key } }

    func saveProgress(_ p: Progress) {
        var list = progressList.filter { $0.key != p.key }
        list.insert(p, at: 0)
        if let data = try? encoder.encode(Array(list.prefix(80))) { defaults.set(data, forKey: "progress") }
    }

    func removeProgress(_ key: String) {
        let list = progressList.filter { $0.key != key }
        if let data = try? encoder.encode(list) { defaults.set(data, forKey: "progress") }
    }

    // MARK: per-episode position / watched

    func episodePosition(_ id: String) -> Double { defaults.double(forKey: "epos_\(id)") }
    func setEpisodePosition(_ id: String, _ position: Double) { defaults.set(position, forKey: "epos_\(id)") }
    func isWatched(_ id: String) -> Bool { defaults.bool(forKey: "ew_\(id)") }
    func setWatched(_ id: String) {
        defaults.set(true, forKey: "ew_\(id)")
        defaults.set(0.0, forKey: "epos_\(id)")
    }

    var volume: Double {
        get { defaults.object(forKey: "volume") == nil ? 80 : defaults.double(forKey: "volume") }
        set { defaults.set(newValue, forKey: "volume") }
    }

    func resetAll() {
        if let id = Bundle.main.bundleIdentifier { defaults.removePersistentDomain(forName: id) }
    }
}
