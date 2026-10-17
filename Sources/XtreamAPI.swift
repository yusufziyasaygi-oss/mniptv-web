import Foundation

let userAgent = "MN-IPTV-Mac/2.1.3"

enum APIError: LocalizedError {
    case badAddress
    case http(Int)
    case badResponse(String)

    var errorDescription: String? {
        switch self {
        case .badAddress: return "Sunucu adresi geçersiz"
        case .http(let code): return "Sunucu yanıtı: HTTP \(code)"
        case .badResponse(let text): return text
        }
    }
}

final class XtreamAPI {
    let account: Account
    /// Small answers (login, categories, series info, EPG), one category, the whole catalogue.
    private let session: URLSession
    private let categorySession: URLSession
    private let bulkSession: URLSession

    init(account: Account) {
        self.account = account
        func make(_ total: TimeInterval) -> URLSession {
            let config = URLSessionConfiguration.default
            config.timeoutIntervalForRequest = 20
            config.timeoutIntervalForResource = total
            config.httpAdditionalHeaders = ["User-Agent": userAgent]
            return URLSession(configuration: config)
        }
        session = make(25)
        categorySession = make(40)
        bulkSession = make(120)  // search and "Tümü" fetch the whole catalogue: tens of MB on large panels
    }

    private static let unreserved = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")

    static func encode(_ s: String) -> String {
        s.addingPercentEncoding(withAllowedCharacters: unreserved) ?? s
    }

    private func apiURL(_ action: String?, _ extra: [(String, String)] = []) throws -> URL {
        var query = "username=\(Self.encode(account.username))&password=\(Self.encode(account.password))"
        if let action = action { query += "&action=\(action)" }
        for (k, v) in extra { query += "&\(k)=\(Self.encode(v))" }
        guard let url = URL(string: account.server + "/player_api.php?" + query),
              let scheme = url.scheme, ["http", "https"].contains(scheme.lowercased()) else {
            throw APIError.badAddress
        }
        return url
    }

    /// Connection errors are retried once; a timeout is reported as such instead of a spinner forever.
    private func get(_ action: String?, _ extra: [(String, String)] = [], using: URLSession? = nil) async throws -> Any {
        let url = try apiURL(action, extra)
        let s = using ?? session
        var attempt = 0
        while true {
            do {
                let (data, response) = try await s.data(from: url)
                if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                    throw APIError.http(http.statusCode)
                }
                if data.isEmpty { return [Any]() }
                return (try? JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])) ?? NSNull()
            } catch let e as URLError where e.code == .timedOut {
                throw APIError.badResponse("Sunucu \(Int(s.configuration.timeoutIntervalForResource)) saniyede yanıt vermedi")
            } catch let e as URLError where e.code != .cancelled && !Task.isCancelled && attempt == 0 {
                _ = e
                attempt += 1
                try await Task.sleep(nanoseconds: 800_000_000)
            }
        }
    }

    /// nil when login is OK, otherwise a user-facing message.
    func authenticate() async throws -> String? {
        guard let root = try await get(nil) as? [String: Any] else {
            return "Bu adres bir Xtream sunucusu gibi görünmüyor"
        }
        guard let info = root["user_info"] as? [String: Any] else { return "Sunucu yanıtı tanınmadı" }
        if jsonInt(info["auth"]) != 1 { return "Kullanıcı adı veya şifre hatalı" }
        if let status = jsonString(info["status"]), status.lowercased() != "active" {
            return "Hesap durumu: \(status)"
        }
        return nil
    }

    func categories(_ kind: Kind) async throws -> [Category] {
        let action: String
        switch kind {
        case .live: action = "get_live_categories"
        case .movie: action = "get_vod_categories"
        case .series: action = "get_series_categories"
        }
        let list = (try await get(action) as? [Any]) ?? []
        return list.compactMap { item in
            guard let o = item as? [String: Any], let id = jsonString(o["category_id"]) else { return nil }
            return Category(id: id, name: jsonString(o["category_name"]) ?? id)
        }
    }

    /// categoryId == nil loads the whole catalogue of that kind (used for search and "Tümü").
    func streams(_ kind: Kind, categoryId: String?) async throws -> [MediaEntry] {
        let action: String
        switch kind {
        case .live: action = "get_live_streams"
        case .movie: action = "get_vod_streams"
        case .series: action = "get_series"
        }
        var extra: [(String, String)] = []
        if let c = categoryId, !c.isEmpty { extra.append(("category_id", c)) }
        let list = (try await get(action, extra, using: extra.isEmpty ? bulkSession : categorySession) as? [Any]) ?? []
        var out: [MediaEntry] = []
        out.reserveCapacity(list.count)
        for item in list {
            guard let o = item as? [String: Any] else { continue }
            switch kind {
            case .live:
                guard let id = jsonString(o["stream_id"]) else { continue }
                out.append(MediaEntry(kind: .live, id: id, name: jsonString(o["name"]) ?? id,
                                      icon: jsonString(o["stream_icon"]), ext: nil,
                                      categoryId: jsonString(o["category_id"]), number: jsonInt(o["num"])))
            case .movie:
                guard let id = jsonString(o["stream_id"]) else { continue }
                out.append(MediaEntry(kind: .movie, id: id, name: jsonString(o["name"]) ?? id,
                                      icon: jsonString(o["stream_icon"]), ext: jsonString(o["container_extension"]) ?? "mp4",
                                      categoryId: jsonString(o["category_id"])))
            case .series:
                guard let id = jsonString(o["series_id"]) else { continue }
                out.append(MediaEntry(kind: .series, id: id, name: jsonString(o["name"]) ?? id,
                                      icon: jsonString(o["cover"]), ext: nil,
                                      categoryId: jsonString(o["category_id"])))
            }
        }
        if kind == .live && out.contains(where: { $0.number > 0 }) {
            out.sort { ($0.number > 0 ? $0.number : Int.max) < ($1.number > 0 ? $1.number : Int.max) }
        }
        return out
    }

    func seriesInfo(_ seriesId: String) async throws -> SeriesInfo {
        guard let root = try await get("get_series_info", [("series_id", seriesId)]) as? [String: Any] else {
            throw APIError.badResponse("Dizi bilgisi alınamadı")
        }
        let info = root["info"] as? [String: Any]
        var bySeason: [Int: [Episode]] = [:]

        func add(_ o: [String: Any], seasonHint: Int) {
            guard let id = jsonString(o["id"]) else { return }
            var season = jsonInt(o["season"], seasonHint)
            if season <= 0 { season = seasonHint }
            let epInfo = o["info"] as? [String: Any]
            let number = jsonInt(o["episode_num"])
            let ep = Episode(id: id, season: season, number: number,
                             title: jsonString(o["title"]) ?? "Bölüm \(number)",
                             ext: jsonString(o["container_extension"]) ?? "mp4",
                             image: jsonString(epInfo?["movie_image"]),
                             durationSec: jsonInt(epInfo?["duration_secs"]))
            bySeason[season, default: []].append(ep)
        }

        if let dict = root["episodes"] as? [String: Any] {
            for (key, value) in dict {
                let hint = Int(key) ?? 1
                for item in (value as? [Any]) ?? [] {
                    if let o = item as? [String: Any] { add(o, seasonHint: hint) }
                }
            }
        } else if let array = root["episodes"] as? [Any] {
            for (i, item) in array.enumerated() {
                if let inner = item as? [Any] {
                    for e in inner { if let o = e as? [String: Any] { add(o, seasonHint: i + 1) } }
                } else if let o = item as? [String: Any] {
                    add(o, seasonHint: 1)
                }
            }
        }
        let sorted = bySeason.mapValues { $0.sorted { $0.number < $1.number } }
        return SeriesInfo(name: jsonString(info?["name"]) ?? "", plot: jsonString(info?["plot"]),
                          cover: jsonString(info?["cover"]), episodesBySeason: sorted)
    }

    func shortEpg(_ streamId: String) async throws -> EpgNow? {
        guard let root = try await get("get_short_epg", [("stream_id", streamId), ("limit", "2")]) as? [String: Any],
              let list = root["epg_listings"] as? [Any] else { return nil }
        let now = Date().timeIntervalSince1970
        for item in list {
            guard let o = item as? [String: Any] else { continue }
            let start = jsonDouble(o["start_timestamp"]), end = jsonDouble(o["stop_timestamp"])
            if start <= now && now < end {
                return EpgNow(title: Self.decode(jsonString(o["title"])), start: start, end: end)
            }
        }
        return nil
    }

    private static func decode(_ b64: String?) -> String {
        guard let b64 = b64 else { return "" }
        if let data = Data(base64Encoded: b64), let s = String(data: data, encoding: .utf8) {
            return s.trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return b64
    }

    private var credentialsPath: String { "\(Self.encode(account.username))/\(Self.encode(account.password))" }
    func liveURL(_ id: String, ext: String) -> String { "\(account.server)/live/\(credentialsPath)/\(id).\(ext)" }
    func movieURL(_ id: String, ext: String) -> String { "\(account.server)/movie/\(credentialsPath)/\(id).\(ext)" }
    func episodeURL(_ id: String, ext: String) -> String { "\(account.server)/series/\(credentialsPath)/\(id).\(ext)" }
}
