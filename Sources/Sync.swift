import CryptoKit
import Foundation

/// Cross-device sync of favourites, "İzlemeye Devam" and episode positions — same format as the
/// TV and Windows apps (see ci/mnsync.py). Items are merged by time stamp; the document is stored
/// AES-256-GCM encrypted (key derived from the Xtream login) in a private GitHub repository.
enum SyncConfig {
    static let api = "https://api.github.com"
    static let repo = "yusufziyasaygi-oss/mn-sync"
    /// XOR-obfuscated access token (contents of the mn-sync repository only). Empty: sync off.
    private static let tokenX = ""
    private static let mask = Array("MN-sync-2026-key".utf8)

    static var token: String {
        guard !tokenX.isEmpty, let x = Data(base64Encoded: tokenX) else { return "" }
        let bytes = x.enumerated().map { $0.element ^ mask[$0.offset % mask.count] }
        return String(decoding: bytes, as: UTF8.self)
    }
}

typealias SyncObject = [String: Any]

enum SyncDoc {
    static let fav = "fav", prog = "prog", ep = "ep"
    private static let tombstoneMs: Int64 = 90 * 24 * 3600 * 1000

    static func empty() -> SyncObject { ["v": 1, fav: SyncObject(), prog: SyncObject(), ep: SyncObject()] }

    static func at(_ o: Any?) -> Int64 { ((o as? SyncObject)?["at"] as? NSNumber)?.int64Value ?? 0 }

    /// Per item the newer time stamp wins; on a tie the local copy is kept.
    static func merge(_ local: SyncObject, _ remote: SyncObject?) -> SyncObject {
        var out = empty()
        for name in [fav, prog, ep] {
            var dst = (local[name] as? SyncObject) ?? [:]
            for (k, v) in (remote?[name] as? SyncObject) ?? [:] where at(v) > at(dst[k]) {
                dst[k] = v
            }
            out[name] = dst
        }
        return prune(out)
    }

    private static func prune(_ doc: SyncObject) -> SyncObject {
        var doc = doc
        let limit = Int64(Date().timeIntervalSince1970 * 1000) - tombstoneMs
        for name in [fav, prog] {
            var s = (doc[name] as? SyncObject) ?? [:]
            for (k, v) in s {
                let o = v as? SyncObject
                if o == nil || ((o?["del"] as? Bool) == true && at(o) < limit) { s[k] = nil }
            }
            doc[name] = s
        }
        doc[prog] = trim((doc[prog] as? SyncObject) ?? [:], 150)
        doc[ep] = trim((doc[ep] as? SyncObject) ?? [:], 3000)
        return doc
    }

    private static func trim(_ s: SyncObject, _ max: Int) -> SyncObject {
        guard s.count > max else { return s }
        let keep = s.keys.sorted { at(s[$0]) > at(s[$1]) }.prefix(max)
        var out = SyncObject()
        for k in keep { out[k] = s[k] }
        return out
    }

    static func data(_ doc: SyncObject) -> Data {
        (try? JSONSerialization.data(withJSONObject: doc, options: [.sortedKeys])) ?? Data("{}".utf8)
    }

    static func same(_ a: SyncObject?, _ b: SyncObject?) -> Bool {
        guard let a = a, let b = b else { return a == nil && b == nil }
        return data(a) == data(b)
    }
}

enum SyncCrypto {
    private static func sha(_ s: String) -> SHA256.Digest { SHA256.hash(data: Data(s.utf8)) }

    static func fileId(_ a: Account) -> String {
        sha("mn-sync-id-v1|\(a.server.lowercased())|\(a.username)|\(a.password)").map { String(format: "%02x", $0) }.joined()
    }

    static func key(_ a: Account) -> SymmetricKey {
        SymmetricKey(data: Data(sha("mn-sync-key-v1|\(a.server.lowercased())|\(a.username)|\(a.password)")))
    }

    static func seal(_ doc: SyncObject, _ key: SymmetricKey) throws -> String {
        let box = try AES.GCM.seal(SyncDoc.data(doc), using: key)
        let wrapper: SyncObject = [
            "v": 1,
            "n": Data(box.nonce).base64EncodedString(),
            "c": (box.ciphertext + box.tag).base64EncodedString(),
        ]
        return String(decoding: try JSONSerialization.data(withJSONObject: wrapper), as: UTF8.self)
    }

    static func unseal(_ text: Data, _ key: SymmetricKey) throws -> SyncObject {
        guard let o = try JSONSerialization.jsonObject(with: text) as? SyncObject,
              let n = (o["n"] as? String).flatMap({ Data(base64Encoded: $0) }),
              let c = (o["c"] as? String).flatMap({ Data(base64Encoded: $0) }), c.count >= 16
        else { throw URLError(.cannotDecodeContentData) }
        let box = try AES.GCM.SealedBox(nonce: AES.GCM.Nonce(data: n), ciphertext: c.prefix(c.count - 16), tag: c.suffix(16))
        let plain = try AES.GCM.open(box, using: key)
        guard let doc = try JSONSerialization.jsonObject(with: plain) as? SyncObject else { throw URLError(.cannotDecodeContentData) }
        return doc
    }
}

/// Pull / merge / push. Local changes are pushed a few seconds later (at most every 45 s).
@MainActor
final class SyncService {
    static let shared = SyncService()

    /// CI only: the mock server on this machine, used with a dummy token.
    var testBase: String?
    private(set) var status = "Kapalı"
    var onChange: (() -> Void)?

    private var pending = false
    private var running = false
    private var again = false
    private var lastRun = Date.distantPast
    private var lastPush = Date.distantPast
    private var pushTask: Task<Void, Never>?
    private let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.requestCachePolicy = .reloadIgnoringLocalCacheData
        c.urlCache = nil
        c.timeoutIntervalForRequest = 25
        return URLSession(configuration: c)
    }()

    var enabled: Bool { testBase != nil || !SyncConfig.token.isEmpty }

    func localChanged() {
        guard enabled else { return }
        pending = true
        let delay = max(6, 45 - Date().timeIntervalSince(lastPush))
        pushTask?.cancel()
        pushTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
            if Task.isCancelled { return }
            await self?.syncNow()
        }
    }

    func flush() {
        guard enabled, pending else { return }
        pushTask?.cancel()
        Task { await syncNow() }
    }

    func pull(minGap: TimeInterval = 20) {
        guard enabled, Date().timeIntervalSince(lastRun) >= minGap else { return }
        Task { await syncNow() }
    }

    func syncNow() async {
        guard enabled else { return }
        if running { again = true; return }
        running = true
        defer { running = false }
        repeat {
            again = false
            await syncOnce()
        } while again
    }

    private func syncOnce() async {
        let store = Store.shared
        guard let account = store.account else { return }
        lastRun = Date()
        let hadPending = pending
        pending = false
        let fileId = SyncCrypto.fileId(account)
        let key = SyncCrypto.key(account)
        do {
            for _ in 0..<3 {
                let (remote, sha) = try await fetch(fileId, key)
                let local = store.syncDoc
                let merged = SyncDoc.merge(local, remote)
                if !SyncDoc.same(merged, local) {
                    store.applySyncDoc(merged)
                    onChange?()
                }
                if SyncDoc.same(merged, remote) { status = "Güncel"; return }
                let code = try await upload(fileId, try SyncCrypto.seal(merged, key), sha)
                if code == 200 || code == 201 {
                    lastPush = Date()
                    status = "Güncel"
                    return
                }
                if code != 409 && code != 422 { throw URLError(.badServerResponse) }
            }
            throw URLError(.badServerResponse)
        } catch {
            status = "Bağlanamadı"
            if hadPending {
                pending = true
                pushTask?.cancel()
                pushTask = Task { [weak self] in
                    try? await Task.sleep(nanoseconds: 60_000_000_000)
                    if Task.isCancelled { return }
                    await self?.syncNow()
                }
            }
        }
    }

    private var base: String { testBase ?? SyncConfig.api }
    private var token: String { testBase != nil ? "test" : SyncConfig.token }

    private func request(_ fileId: String, query: String = "") -> URLRequest {
        var r = URLRequest(url: URL(string: "\(base)/repos/\(SyncConfig.repo)/contents/v1/\(fileId).json\(query)")!)
        r.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        r.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        r.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        r.setValue("MN-IPTV-Mac", forHTTPHeaderField: "User-Agent")
        r.cachePolicy = .reloadIgnoringLocalCacheData
        return r
    }

    /// A stored file that cannot be decrypted is treated as empty (and overwritten).
    private func fetch(_ fileId: String, _ key: SymmetricKey) async throws -> (SyncObject?, String?) {
        let (data, resp) = try await session.data(for: request(fileId, query: "?t=\(Int(Date().timeIntervalSince1970 * 1000))"))
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        if code == 404 { return (nil, nil) }
        guard code == 200, let o = try JSONSerialization.jsonObject(with: data) as? SyncObject else {
            throw URLError(.badServerResponse)
        }
        let sha = o["sha"] as? String
        let b64 = ((o["content"] as? String) ?? "").replacingOccurrences(of: "\n", with: "")
        guard let raw = Data(base64Encoded: b64) else { return (nil, sha) }
        return ((try? SyncCrypto.unseal(raw, key)), sha)
    }

    private func upload(_ fileId: String, _ sealed: String, _ sha: String?) async throws -> Int {
        var body: SyncObject = ["message": "sync mac", "content": Data(sealed.utf8).base64EncodedString()]
        if let sha = sha { body["sha"] = sha }
        var r = request(fileId)
        r.httpMethod = "PUT"
        r.setValue("application/json", forHTTPHeaderField: "Content-Type")
        r.httpBody = try JSONSerialization.data(withJSONObject: body)
        let (_, resp) = try await session.data(for: r)
        return (resp as? HTTPURLResponse)?.statusCode ?? 0
    }
}
