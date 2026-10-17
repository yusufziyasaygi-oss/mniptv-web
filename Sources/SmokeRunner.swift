import AppKit
import IOKit.pwr_mgt
import SwiftUI

/// CI-only self test. Active only when MN_SMOKE_DIR is set; drives the real UI with
/// posted keyboard events against ci/mock_xtream.py and records screenshots + checks.
@MainActor
final class SmokeRunner {
    static var directory: URL? {
        ProcessInfo.processInfo.environment["MN_SMOKE_DIR"].map { URL(fileURLWithPath: $0, isDirectory: true) }
    }

    private let model: AppModel
    private let form: LoginForm
    private let dir: URL
    private var lines: [String] = []
    private var checks: [[String: Any]] = []

    init(model: AppModel, form: LoginForm, directory: URL) {
        self.model = model
        self.form = form
        self.dir = directory
    }

    private var window: NSWindow? { NSApp.windows.first { $0.isVisible && $0.contentView != nil } }

    private func wait(_ seconds: Double) async {
        try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
    }

    private func waitUntil(_ timeout: Double, _ condition: () -> Bool) async -> Bool {
        let end = Date().addingTimeInterval(timeout)
        while Date() < end {
            if condition() { return true }
            await wait(0.25)
        }
        return condition()
    }

    private func log(_ text: String) {
        let p = model.activePlayer
        let state = p.map { "pos=\(String(format: "%.1f", $0.position)) dur=\(String(format: "%.0f", $0.duration)) h=\($0.videoHeight) status=\($0.status) cur=\($0.current)" } ?? "-"
        var diag = PlaybackEngine.peek?.diagnostics() ?? "engine=nil"
        if let e = PlaybackEngine.peek {
            diag += " hwdec=\(e.text("hwdec-current")) pixfmt=\(e.text("video-params/pixelformat")) hwpix=\(e.text("video-params/hw-pixelformat"))"
            diag += " vid=\(e.text("vid")) voconf=\(e.text("vo-configured")) out=\(e.text("video-out-params/w"))x\(e.text("video-out-params/h")) drops=\(e.text("frame-drop-count"))/\(e.text("decoder-frame-drop-count")) vo=\(e.text("current-vo")) container=\(e.text("file-format"))"
        }
        let line = "\(text) | section=\(model.section.title) route=\(model.route.count) tiles=\(model.tiles.count) | \(state) | \(diag)"
        lines.append(line)
        try? lines.joined(separator: "\n").write(to: dir.appendingPathComponent("steps.txt"), atomically: true, encoding: .utf8)
    }

    private func check(_ name: String, _ ok: Bool, _ detail: String = "") {
        checks.append(["name": name, "ok": ok, "detail": detail])
        log("CHECK \(ok ? "OK  " : "FAIL") \(name) \(detail)")
    }

    private func shot(_ name: String) {
        guard let win = window, let view = win.contentView else { return }
        if let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
            view.cacheDisplay(in: view.bounds, to: rep)
            if let png = rep.representation(using: .png, properties: [:]) {
                try? png.write(to: dir.appendingPathComponent("\(name).png"))
            }
        }
        // Full window including the OpenGL video layer, when the runner allows screen capture.
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        task.arguments = ["-x", "-o", "-l\(win.windowNumber)", dir.appendingPathComponent("\(name)-screen.png").path]
        try? task.run()
        task.waitUntilExit()
        log("SHOT \(name)")
    }

    private func key(_ code: UInt16, _ chars: String, shift: Bool = false) async {
        guard let win = window else { return }
        let flags: NSEvent.ModifierFlags = shift ? [.shift] : []
        for type in [NSEvent.EventType.keyDown, .keyUp] {
            if let ev = NSEvent.keyEvent(with: type, location: .zero, modifierFlags: flags,
                                         timestamp: ProcessInfo.processInfo.systemUptime, windowNumber: win.windowNumber,
                                         context: nil, characters: chars, charactersIgnoringModifiers: chars,
                                         isARepeat: false, keyCode: code) {
                NSApp.postEvent(ev, atStart: false)
            }
        }
        await wait(0.6)
    }

    private func fn(_ scalar: Int) -> String { String(Character(UnicodeScalar(UInt32(scalar))!)) }
    private func up() async { await key(126, fn(NSUpArrowFunctionKey)) }
    private func down() async { await key(125, fn(NSDownArrowFunctionKey)) }
    private func left() async { await key(123, fn(NSLeftArrowFunctionKey)) }
    private func right(shift: Bool = false) async { await key(124, fn(NSRightArrowFunctionKey), shift: shift) }
    private func enter() async { await key(36, "\r") }
    private func escape() async { await key(53, "\u{1b}") }
    private func space() async { await key(49, " ") }
    private func letter(_ c: String, _ code: UInt16) async { await key(code, c) }

    private func toSidebar() async {
        for _ in 0..<4 { await left() }
    }

    /// What macOS itself reports: the power-management assertions this process holds, by name.
    private func sleepAssertionHeld() -> Bool {
        var out: Unmanaged<CFDictionary>?
        guard IOPMCopyAssertionsByProcess(&out) == kIOReturnSuccess,
              let all = out?.takeRetainedValue() as? [NSNumber: [[String: Any]]] else { return false }
        let mine = all[NSNumber(value: getpid())] ?? []
        let names = mine.map { "\($0["AssertType"] ?? "?"): \($0["AssertName"] ?? "?")" }
        log("ASSERTIONS \(names)")
        return mine.contains { ($0["AssertName"] as? String) == SleepGuard.reason && ($0["AssertType"] as? String) == "PreventUserIdleDisplaySleep" }
    }

    private func httpGet(_ url: String) async {
        guard let u = URL(string: url) else { return }
        _ = try? await URLSession.shared.data(from: u)
    }

    private func finish() {
        let data = try? JSONSerialization.data(withJSONObject: checks, options: [.prettyPrinted])
        try? data?.write(to: dir.appendingPathComponent("results.json"))
        let failed = checks.filter { ($0["ok"] as? Bool) != true }.count
        try? "checks=\(checks.count) failed=\(failed)".write(to: dir.appendingPathComponent("finished.txt"), atomically: true, encoding: .utf8)
    }

    func run() async {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let server = ProcessInfo.processInfo.environment["MN_SMOKE_SERVER"] ?? "http://127.0.0.1:8080"
        await wait(2)
        if let screen = NSScreen.main?.visibleFrame {
            window?.setFrame(NSRect(x: screen.minX, y: screen.minY, width: min(1440, screen.width), height: min(880, screen.height)), display: true)
        }
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        await wait(1)

        // 1. Login with the keyboard (Enter triggers the default button)
        shot("01-login")
        form.server = server
        form.username = "u"
        form.password = "p"
        await wait(0.5)
        shot("02-login-filled")
        await enter()
        let loggedIn = await waitUntil(15) { self.model.account != nil }
        check("login", loggedIn)
        guard loggedIn else { finish(); NSApp.terminate(nil); return }
        let loaded = await waitUntil(15) { !self.model.tiles.isEmpty }
        await wait(1)
        shot("03-home-live")
        check("live categories loaded", model.categories.count == 3, "categories=\(model.categories.count)")
        check("live channels loaded", loaded && model.tiles.count == 15, "tiles=\(model.tiles.count)")

        // 2. Keyboard navigation: sidebar -> categories -> grid, open a channel
        await right(); await right(); await down(); await right()
        shot("04-grid-focus")
        await enter()
        let livePlayer = await waitUntil(10) { self.model.activePlayer != nil && self.model.route.count == 1 }
        check("live player opened", livePlayer)
        let liveStarted = await waitUntil(20) { (self.model.activePlayer?.position ?? 0) > 1 }
        check("live stream plays", liveStarted, "pos=\(model.activePlayer?.position ?? 0) h=\(model.activePlayer?.videoHeight ?? 0)")
        let c0 = model.activePlayer?.current ?? -1
        check("opened the focused tile (not the first)", c0 > 0, "current=\(c0)")
        shot("05-live-playing")

        await down()
        await wait(5)
        check("zap down -> next channel", model.activePlayer?.current == c0 + 1, "current=\(model.activePlayer?.current ?? -1)")
        shot("06-live-zap")
        await left()
        await wait(4)
        check("left -> previous channel", model.activePlayer?.current == c0, "current=\(model.activePlayer?.current ?? -1)")
        await enter()
        await wait(1)
        shot("07-live-list")
        await down(); await down(); await enter()
        await wait(4)
        check("channel list pick", model.activePlayer?.current == c0 + 2, "current=\(model.activePlayer?.current ?? -1)")
        await letter("1", 18); await letter("2", 19)
        await wait(3)
        let ch = model.activePlayer.flatMap { p in p.channels.indices.contains(p.current) ? p.channels[p.current] : nil }
        check("number keys 1-2 -> channel 12", ch?.number == 12, "number=\(ch?.number ?? -1)")
        shot("08-live-number")
        await escape()
        await wait(1.5)
        check("esc leaves live player", model.route.isEmpty)
        shot("09-back-home")

        // 3. Movies via sidebar keyboard
        await toSidebar()
        await down()
        let moviesLoaded = await waitUntil(10) { self.model.section == .movies && !self.model.tiles.isEmpty }
        check("movies section", moviesLoaded, "tiles=\(model.tiles.count)")
        await right(); await right()
        shot("10-movies-grid")
        await enter()
        let movieStarted = await waitUntil(25) { (self.model.activePlayer?.position ?? 0) > 18 }
        check("movie plays past 18s", movieStarted, "pos=\(model.activePlayer?.position ?? 0)")
        check("Mac kept awake while playing", SleepGuard.active && sleepAssertionHeld(), "guard=\(SleepGuard.active)")
        shot("11-movie-playing")
        let before = model.activePlayer?.position ?? 0
        await right(); await right()
        await wait(2)
        let after = model.activePlayer?.position ?? 0
        check("right arrow seeks +20s", after - before >= 17, "before=\(before) after=\(after)")
        shot("12-movie-seek")
        await space()
        await wait(1)
        check("space pauses", model.activePlayer?.status == .paused, "status=\(String(describing: model.activePlayer?.status))")
        check("paused: Mac may sleep again", !SleepGuard.active && !sleepAssertionHeld(), "guard=\(SleepGuard.active)")
        shot("13-movie-paused")
        await space()
        await wait(1)
        await escape()
        await wait(1.5)
        let saved = model.store.progress("MOVIE:2001")
        check("movie progress saved", saved != nil && (saved?.position ?? 0) > 15, "pos=\(saved?.position ?? 0)")

        // 4. Continue watching
        await toSidebar()
        await up(); await up()
        await wait(1)
        check("continue watching shows movie", model.section == .continueWatching && model.tiles.count == 1, "tiles=\(model.tiles.count)")
        shot("14-continue")
        await right()
        await enter()
        let resumed = await waitUntil(15) { (self.model.activePlayer?.position ?? 0) >= (saved?.position ?? 0) - 3 }
        check("resume from saved position", resumed, "pos=\(model.activePlayer?.position ?? 0) saved=\(saved?.position ?? 0)")
        await escape()
        await wait(1.5)

        // 5. Series
        await toSidebar()
        await down(); await down(); await down()
        let seriesLoaded = await waitUntil(10) { self.model.section == .series && !self.model.tiles.isEmpty }
        check("series section", seriesLoaded, "tiles=\(model.tiles.count)")
        await right(); await right(); await enter()
        await wait(3)
        shot("15-series-detail")
        check("series screen open", { if case .series? = model.route.last { return true }; return false }())
        await enter()
        let episodeStarted = await waitUntil(25) { (self.model.activePlayer?.position ?? 0) > 18 }
        check("episode plays", episodeStarted, "pos=\(model.activePlayer?.position ?? 0)")
        shot("16-episode-playing")
        await escape()
        await wait(2)
        let sp = model.store.progress("SERIES:3001")
        check("series progress saved (one card)", sp != nil && sp?.episodeId == "4101", "ep=\(sp?.episodeId ?? "-") pos=\(sp?.position ?? 0)")
        shot("17-series-resume")
        await down()
        await right()
        await wait(1)
        shot("18-series-season-2")
        await escape()
        await wait(1)

        // 6. Series without episodes must not crash
        model.push(.series(MediaEntry(kind: .series, id: "3008", name: "Dizi 8", icon: nil, ext: nil, categoryId: nil)))
        await wait(3)
        shot("19-series-empty")
        await escape()
        await wait(1)

        // 7. Favourite with F, then favourites section
        model.goToSection(.movies)
        _ = await waitUntil(10) { !self.model.tiles.isEmpty }
        await toSidebar()
        await right(); await right()
        await letter("f", 3)
        await wait(1)
        check("F adds favourite", model.store.favorites.contains { $0.key == "MOVIE:2001" })
        model.goToSection(.favorites)
        await wait(1)
        shot("20-favorites")
        check("favourites list", model.tiles.count >= 1, "tiles=\(model.tiles.count)")

        // 8. Search
        model.goToSection(.search)
        await wait(1)
        model.query = "kanal 1"
        let searched = await waitUntil(10) { self.model.tiles.count == 7 }
        check("search 'kanal 1'", searched, "tiles=\(model.tiles.count)")
        await wait(1)
        shot("21-search")

        // 8b. Slow panel: one list download at a time, "Spor" answers in 10 s. Leaving "Spor" for
        // "Tümü" must abort the Spor download, so "Tümü" appears within a few seconds.
        await httpGet(server + "/__slow?on=1")
        model.clearCache()
        model.goToSection(.live)
        _ = await waitUntil(10) { self.model.tiles.count == 15 && !self.model.loading }
        model.selectCategory(1)
        await wait(1.5)
        model.selectCategory(2)
        let switched = await waitUntil(4) { self.model.tiles.count == 30 && !self.model.loading }
        check("slow panel: leaving a slow category does not block the next one", switched,
              "tiles=\(model.tiles.count) loading=\(model.loading)")
        shot("21b-slow-switch")
        log("slow: select Spor again t=\(Date().timeIntervalSince1970)")
        model.selectCategory(1)
        await wait(8.5)
        check("slow panel: waiting hint shown", model.loading && model.slowLoading, "loading=\(model.loading) slow=\(model.slowLoading)")
        shot("21c-slow-hint")
        let slowLoaded = await waitUntil(6) { self.model.tiles.count == 15 && !self.model.loading }
        check("slow panel: slow category arrives", slowLoaded, "tiles=\(model.tiles.count) loading=\(model.loading) empty=\(model.emptyMessage ?? "-") t=\(Date().timeIntervalSince1970)")
        await httpGet(server + "/__slow?on=0")

        // 9. Player choice: MN Player hand-off instead of the built-in player
        model.setPlaybackApp(mnPlayer: true)
        model.goToSection(.movies)
        _ = await waitUntil(10) { !self.model.tiles.isEmpty }
        if let first = model.tiles.first { model.open(first) }
        let handed = await waitUntil(10) { !self.model.lastExternalResult.isEmpty }
        await wait(4)
        let mnPlayerRunning = !NSRunningApplication.runningApplications(withBundleIdentifier: "com.mavinokta.player").isEmpty
        let installed = FileManager.default.fileExists(atPath: "/Applications/MN Player.app")
        let expected = installed ? (model.lastExternalResult == "opened" && mnPlayerRunning) : model.lastExternalResult == "missing"
        check("MN Player option hands the movie to MN Player", handed && model.route.isEmpty && expected,
              "result=\(model.lastExternalResult) route=\(model.route.count) mnplayer=\(mnPlayerRunning)")
        shot("22-mn-player")
        NSRunningApplication.runningApplications(withBundleIdentifier: "com.mavinokta.player").forEach { $0.terminate() }
        model.setPlaybackApp(mnPlayer: false)
        await wait(1)

        finish()
        await wait(1)
        NSApp.terminate(nil)
    }
}
