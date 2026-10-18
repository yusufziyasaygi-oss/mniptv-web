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
            if newValue != account { defaults.removeObject(forKey: "syncDoc") }
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
        record(SyncDoc.fav, entry.key, ["e": entryToSync(entry)], deleted: !added)
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
        record(SyncDoc.prog, p.key, ["p": progressToSync(p)], deleted: false, at: Int64(p.updatedAt * 1000))
    }

    func removeProgress(_ key: String) {
        let old = progressList
        let list = old.filter { $0.key != key }
        guard list.count != old.count else { return }
        if let data = try? encoder.encode(list) { defaults.set(data, forKey: "progress") }
        record(SyncDoc.prog, key, [:], deleted: true)
    }

    // MARK: per-episode position / watched

    func episodePosition(_ id: String) -> Double { defaults.double(forKey: "epos_\(id)") }
    func setEpisodePosition(_ id: String, _ position: Double) {
        defaults.set(position, forKey: "epos_\(id)")
        recordEpisode(id, position: position, watched: isWatched(id))
    }
    func isWatched(_ id: String) -> Bool { defaults.bool(forKey: "ew_\(id)") }
    func setWatched(_ id: String) {
        defaults.set(true, forKey: "ew_\(id)")
        defaults.set(0.0, forKey: "epos_\(id)")
        recordEpisode(id, position: 0, watched: true)
    }

    // MARK: sync document
    // Every local change also lands here with a time stamp (deletions as tombstones); SyncService
    // merges it with the other devices' copy and hands the result back through applySyncDoc.

    private var nowMs: Int64 { Int64(Date().timeIntervalSince1970 * 1000) }

    var syncDoc: SyncObject {
        if let data = defaults.data(forKey: "syncDoc"),
           let doc = try? JSONSerialization.jsonObject(with: data) as? SyncObject { return doc }
        // First run with sync: what this Mac already has goes in as very old entries.
        var doc = SyncDoc.empty()
        let favs = favorites
        var f = SyncObject()
        for (i, e) in favs.enumerated() { f[e.key] = ["at": Int64(favs.count - i), "del": false, "e": entryToSync(e)] }
        doc[SyncDoc.fav] = f
        var p = SyncObject()
        for x in progressList { p[x.key] = ["at": Int64(x.updatedAt * 1000), "del": false, "p": progressToSync(x)] }
        doc[SyncDoc.prog] = p
        var eps = SyncObject()
        for (k, _) in defaults.dictionaryRepresentation() {
            let id: String
            if k.hasPrefix("epos_") { id = String(k.dropFirst(5)) } else if k.hasPrefix("ew_") { id = String(k.dropFirst(3)) } else { continue }
            if eps[id] != nil { continue }
            let pos = episodePosition(id), w = isWatched(id)
            if pos > 0 || w { eps[id] = ["at": Int64(1), "pos": Int64(pos * 1000), "w": w] }
        }
        doc[SyncDoc.ep] = eps
        saveSyncDoc(doc)
        return doc
    }

    private func saveSyncDoc(_ doc: SyncObject) { defaults.set(SyncDoc.data(doc), forKey: "syncDoc") }

    private func record(_ section: String, _ key: String, _ body: SyncObject, deleted: Bool, at: Int64? = nil) {
        var doc = syncDoc
        var s = (doc[section] as? SyncObject) ?? [:]
        var b = body
        b["at"] = at ?? nowMs
        b["del"] = deleted
        s[key] = b
        doc[section] = s
        saveSyncDoc(doc)
        Task { @MainActor in SyncService.shared.localChanged() }
    }

    private func recordEpisode(_ id: String, position: Double, watched: Bool) {
        var doc = syncDoc
        var s = (doc[SyncDoc.ep] as? SyncObject) ?? [:]
        s[id] = ["at": nowMs, "pos": Int64(position * 1000), "w": watched]
        doc[SyncDoc.ep] = s
        saveSyncDoc(doc)
        Task { @MainActor in SyncService.shared.localChanged() }
    }

    /// Replace local favourites, progress and episode marks with the merged document.
    func applySyncDoc(_ doc: SyncObject) {
        saveSyncDoc(doc)
        let f = (doc[SyncDoc.fav] as? SyncObject) ?? [:]
        let favs: [MediaEntry] = f.compactMap { (_, v) -> (Int64, MediaEntry)? in
            guard let o = v as? SyncObject, (o["del"] as? Bool) != true,
                  let e = (o["e"] as? SyncObject).flatMap(entryFromSync) else { return nil }
            return (SyncDoc.at(o), e)
        }.sorted { $0.0 > $1.0 }.map(\.1)
        if let data = try? encoder.encode(favs) { defaults.set(data, forKey: "favorites") }

        let p = (doc[SyncDoc.prog] as? SyncObject) ?? [:]
        let progs: [Progress] = p.compactMap { (_, v) -> Progress? in
            guard let o = v as? SyncObject, (o["del"] as? Bool) != true, let x = o["p"] as? SyncObject else { return nil }
            return progressFromSync(x, at: SyncDoc.at(o))
        }.sorted { $0.updatedAt > $1.updatedAt }
        if let data = try? encoder.encode(Array(progs.prefix(80))) { defaults.set(data, forKey: "progress") }

        for (id, v) in (doc[SyncDoc.ep] as? SyncObject) ?? [:] {
            guard let o = v as? SyncObject else { continue }
            let w = (o["w"] as? Bool) ?? false
            let pos = Double((o["pos"] as? NSNumber)?.int64Value ?? 0) / 1000
            if isWatched(id) != w { defaults.set(w, forKey: "ew_\(id)") }
            if episodePosition(id) != pos { defaults.set(pos, forKey: "epos_\(id)") }
        }
    }

    private func entryToSync(_ e: MediaEntry) -> SyncObject {
        ["kind": e.kind.rawValue, "id": e.id, "name": e.name, "icon": e.icon ?? "", "ext": e.ext ?? "",
         "cat": e.categoryId ?? "", "num": e.number]
    }

    private func entryFromSync(_ o: SyncObject) -> MediaEntry? {
        guard let kind = (o["kind"] as? String).flatMap(Kind.init(rawValue:)), let id = jsonString(o["id"]) else { return nil }
        return MediaEntry(kind: kind, id: id, name: jsonString(o["name"]) ?? id, icon: jsonString(o["icon"]),
                          ext: jsonString(o["ext"]), categoryId: jsonString(o["cat"]), number: jsonInt(o["num"]))
    }

    private func progressToSync(_ p: Progress) -> SyncObject {
        ["key": p.key, "kind": p.kind.rawValue, "id": p.id, "title": p.title, "subtitle": p.subtitle ?? "",
         "image": p.image ?? "", "ext": p.ext ?? "", "episodeId": p.episodeId ?? "", "season": p.season,
         "episode": p.episode, "pos": Int64(p.position * 1000), "dur": Int64(p.duration * 1000)]
    }

    private func progressFromSync(_ o: SyncObject, at: Int64) -> Progress? {
        guard let kind = (o["kind"] as? String).flatMap(Kind.init(rawValue:)),
              let key = jsonString(o["key"]), let id = jsonString(o["id"]) else { return nil }
        return Progress(key: key, kind: kind, id: id, title: jsonString(o["title"]) ?? "", subtitle: jsonString(o["subtitle"]),
                        image: jsonString(o["image"]), ext: jsonString(o["ext"]), episodeId: jsonString(o["episodeId"]),
                        season: jsonInt(o["season"]), episode: jsonInt(o["episode"]),
                        position: jsonDouble(o["pos"]) / 1000, duration: jsonDouble(o["dur"]) / 1000,
                        updatedAt: Double(at) / 1000)
    }

    /// "builtin" (MN IPTV's own player) or "mnplayer" (open in the MN Player app).
    var playbackApp: String {
        get { defaults.string(forKey: "playbackApplication") == "mnplayer" ? "mnplayer" : "builtin" }
        set { defaults.set(newValue, forKey: "playbackApplication") }
    }

    var volume: Double {
        get { defaults.object(forKey: "volume") == nil ? 80 : defaults.double(forKey: "volume") }
        set { defaults.set(newValue, forKey: "volume") }
    }

    func resetAll() {
        if let id = Bundle.main.bundleIdentifier { defaults.removePersistentDomain(forName: id) }
    }
}
