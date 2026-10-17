import Foundation

enum Kind: String, Codable, CaseIterable {
    case live = "LIVE", movie = "MOVIE", series = "SERIES"
}

struct Category: Hashable {
    let id: String
    let name: String
}

struct Account: Codable, Equatable {
    var server: String
    var username: String
    var password: String

    static func normalize(_ input: String) -> String {
        var s = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if s.isEmpty { return s }
        let lower = s.lowercased()
        if !lower.hasPrefix("http://") && !lower.hasPrefix("https://") { s = "http://" + s }
        for marker in ["/player_api.php", "/get.php"] {
            if let r = s.range(of: marker) { s = String(s[..<r.lowerBound]) }
        }
        while s.hasSuffix("/") { s.removeLast() }
        return s
    }
}

/// A channel, movie or series as listed in the catalogue.
struct MediaEntry: Codable, Hashable {
    let kind: Kind
    let id: String
    let name: String
    let icon: String?
    let ext: String?
    let categoryId: String?
    var number: Int = 0

    var key: String { "\(kind.rawValue):\(id)" }
}

struct Episode: Codable, Hashable {
    let id: String
    let season: Int
    let number: Int
    let title: String
    let ext: String
    let image: String?
    let durationSec: Int
}

struct SeriesInfo {
    let name: String
    let plot: String?
    let cover: String?
    let episodesBySeason: [Int: [Episode]]

    var seasons: [Int] { episodesBySeason.keys.sorted() }
    var allEpisodes: [Episode] { seasons.flatMap { episodesBySeason[$0] ?? [] } }
}

struct EpgNow: Equatable {
    let title: String
    let start: TimeInterval
    let end: TimeInterval
}

/// One "İzlemeye Devam" card. Series are keyed per series, so each series has a single card.
struct Progress: Codable, Hashable {
    let key: String
    let kind: Kind
    let id: String
    let title: String
    let subtitle: String?
    let image: String?
    let ext: String?
    let episodeId: String?
    let season: Int
    let episode: Int
    let position: Double
    let duration: Double
    let updatedAt: Double

    var fraction: Double { duration > 0 ? min(1, max(0, position / duration)) : 0 }
}

// MARK: - Lenient JSON helpers (Xtream panels mix numbers, strings, "" and null)

func jsonString(_ value: Any?) -> String? {
    guard let value = value, !(value is NSNull) else { return nil }
    let s: String
    if let str = value as? String { s = str }
    else if let num = value as? NSNumber { s = num.stringValue }
    else { s = "\(value)" }
    let t = s.trimmingCharacters(in: .whitespacesAndNewlines)
    return (t.isEmpty || t == "null") ? nil : t
}

func jsonInt(_ value: Any?, _ fallback: Int = 0) -> Int {
    guard let s = jsonString(value), let d = Double(s), d.isFinite else { return fallback }
    return Int(d)
}

func jsonDouble(_ value: Any?, _ fallback: Double = 0) -> Double {
    guard let s = jsonString(value), let d = Double(s), d.isFinite else { return fallback }
    return d
}

func formatTime(_ seconds: Double) -> String {
    guard seconds.isFinite, seconds > 0 else { return "0:00" }
    let total = Int(seconds)
    let h = total / 3600, m = (total % 3600) / 60, s = total % 60
    return h > 0 ? String(format: "%d:%02d:%02d", h, m, s) : String(format: "%d:%02d", m, s)
}

private let searchLocale = Locale(identifier: "tr_TR")
private let searchFold: [Character: Character] = ["ı": "i", "ğ": "g", "ü": "u", "ş": "s", "ö": "o", "ç": "c", "â": "a", "î": "i"]

func normalizeForSearch(_ s: String) -> String {
    String(s.lowercased(with: searchLocale).map { searchFold[$0] ?? $0 })
}
