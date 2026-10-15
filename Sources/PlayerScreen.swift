import AppKit
import SwiftUI

/// Hosts the libmpv OpenGL surface.
struct MPVSurfaceView: NSViewRepresentable {
    let surface: NSView

    func makeNSView(context: Context) -> NSView {
        let container = NSView()
        container.wantsLayer = true
        container.layer?.backgroundColor = NSColor.black.cgColor
        attach(surface, to: container)
        return container
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        if surface.superview !== nsView { attach(surface, to: nsView) }
    }

    private func attach(_ view: NSView, to container: NSView) {
        view.removeFromSuperview()
        view.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(view)
        NSLayoutConstraint.activate([
            view.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            view.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            view.topAnchor.constraint(equalTo: container.topAnchor),
            view.bottomAnchor.constraint(equalTo: container.bottomAnchor),
        ])
    }
}

struct PlayerScreen: View {
    let request: PlayRequest
    @ObservedObject var model: AppModel
    @StateObject private var player: PlayerController

    @State private var controlsVisible = true
    @State private var infoVisible = true
    @State private var listVisible = false
    @State private var listIndex = 0
    @State private var hideTask: Task<Void, Never>?
    @State private var infoTask: Task<Void, Never>?
    @State private var numberBuffer = ""
    @State private var numberTask: Task<Void, Never>?
    @State private var hud: String?
    @State private var hudTask: Task<Void, Never>?
    @State private var scrubbing: Double?
    @FocusState private var keyFocus: Bool

    init(request: PlayRequest, model: AppModel) {
        self.request = request
        self.model = model
        _player = StateObject(wrappedValue: PlayerController(request: request, model: model))
    }

    var body: some View {
        ZStack {
            Color.black
            MPVSurfaceView(surface: player.surface)
                .allowsHitTesting(false)
            Color.clear
                .contentShape(Rectangle())
                .onTapGesture(count: 2) { toggleFullScreen() }
                .onTapGesture { poke(); if player.isLive && listVisible { listVisible = false } }

            if player.status == .loading && player.message == nil {
                ProgressView().controlSize(.large).allowsHitTesting(false)
            }
            if let text = overlayMessage {
                Text(text)
                    .font(.system(size: 16)).foregroundColor(.white).multilineTextAlignment(.center)
                    .padding(20).background(RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.75)))
                    .frame(maxWidth: 560)
            }
            if let hud = hud {
                VStack {
                    Spacer()
                    Text(hud).font(.system(size: 20, weight: .bold)).foregroundColor(.white)
                        .padding(.horizontal, 22).padding(.vertical, 10)
                        .background(RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.75)))
                        .padding(.bottom, controlsVisible && !player.isLive ? 130 : 60)
                }
                .allowsHitTesting(false)
            }
            if !numberBuffer.isEmpty {
                VStack {
                    HStack { Spacer(); Text(numberBuffer).font(.system(size: 40, weight: .bold)).foregroundColor(.white)
                        .padding(.horizontal, 22).padding(.vertical, 8)
                        .background(RoundedRectangle(cornerRadius: 10).fill(Color.black.opacity(0.75))) }
                    Spacer()
                }
                .padding(40)
                .allowsHitTesting(false)
            }

            if controlsVisible || listVisible { topBar }
            if player.isLive {
                if infoVisible || controlsVisible { liveInfo }
                if listVisible { channelList }
            } else if controlsVisible {
                vodControls
            }
        }
        .background(Color.black)
        .focusable()
        .focused($keyFocus)
        .focusEffectDisabled()
        .onKeyPress(phases: [.down, .repeat]) { handleKey($0) }
        .onContinuousHover { phase in
            if case .active = phase { poke() }
        }
        .onAppear {
            keyFocus = true
            model.activePlayer = player
            player.onFinished = { model.back() }
            player.start()
            poke()
            flashInfo()
        }
        .onDisappear {
            player.stop()
            hideTask?.cancel()
            NSCursor.setHiddenUntilMouseMoves(false)
        }
        .onChange(of: player.current) { flashInfo() }
    }

    private var overlayMessage: String? {
        if case .failed(let text) = player.status { return text + "\nTekrar denemek için Space'e bas." }
        return player.message
    }

    // MARK: top bar

    private var topBar: some View {
        VStack {
            HStack(spacing: 14) {
                Button(action: { goBack() }) { Label("Geri", systemImage: "chevron.left") }
                    .buttonStyle(PillButtonStyle())
                VStack(alignment: .leading, spacing: 2) {
                    Text(player.title).font(.system(size: 18, weight: .bold)).foregroundColor(.white).lineLimit(1)
                    if !player.subtitle.isEmpty {
                        Text(player.subtitle).font(.system(size: 13)).foregroundColor(MN.text2).lineLimit(1)
                    }
                }
                Spacer()
                if player.isLive {
                    Button(action: { toggleList() }) { Label("Kanallar", systemImage: "list.bullet") }
                        .buttonStyle(PillButtonStyle(prominent: listVisible))
                    if player.channels.indices.contains(player.current) {
                        let ch = player.channels[player.current]
                        let fav = { _ = model.revision; return model.store.isFavorite(ch.key) }()
                        Button(action: { model.toggleFavorite(ch) }) {
                            Image(systemName: fav ? "star.fill" : "star")
                        }
                        .buttonStyle(PillButtonStyle())
                        .help(fav ? "Favorilerden çıkar" : "Favorilere ekle")
                    }
                }
                trackMenu
                Button(action: { player.fillScreen.toggle(); showHUD(player.fillScreen ? "Görüntü: Doldur" : "Görüntü: Sığdır") }) {
                    Image(systemName: player.fillScreen ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right")
                }
                .buttonStyle(PillButtonStyle())
                .help("Görüntü oranı")
                Button(action: { toggleFullScreen() }) { Image(systemName: "rectangle.inset.filled") }
                    .buttonStyle(PillButtonStyle())
                    .help("Tam ekran (F)")
            }
            .padding(.horizontal, 28)
            .padding(.top, 34)
            .padding(.bottom, 40)
            .background(LinearGradient(colors: [Color.black.opacity(0.85), .clear], startPoint: .top, endPoint: .bottom))
            Spacer()
        }
    }

    private var trackMenu: some View {
        Menu {
            SwiftUI.Section("Ses") {
                if player.audioTracks.isEmpty { Text("Ses izi yok") }
                ForEach(player.audioTracks, id: \.id) { t in
                    Button(t.selected ? "✓ \(t.label)" : t.label) { player.selectAudio(t.id) }
                }
            }
            SwiftUI.Section("Altyazı") {
                Button(player.subtitleTracks.contains(where: { $0.selected }) ? "Kapalı" : "✓ Kapalı") { player.selectSubtitle(nil) }
                ForEach(player.subtitleTracks, id: \.id) { t in
                    Button(t.selected ? "✓ \(t.label)" : t.label) { player.selectSubtitle(t.id) }
                }
            }
        } label: {
            Image(systemName: "captions.bubble")
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .padding(.horizontal, 14)
        .frame(height: 38)
        .background(Capsule().fill(Color(red: 0.145, green: 0.165, blue: 0.212)))
        .help("Ses ve altyazı")
        .onHover { if $0 { player.refreshTracks() } }
    }

    // MARK: live

    private var liveInfo: some View {
        VStack {
            Spacer()
            if player.channels.indices.contains(player.current) {
                let ch = player.channels[player.current]
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 16) {
                        RemoteImage(url: ch.icon, fill: false).frame(width: 60, height: 60)
                        Text(ch.number > 0 ? "\(ch.number)" : "\(player.current + 1)")
                            .font(.system(size: 22, weight: .bold)).foregroundColor(MN.blue)
                        Text(ch.name).font(.system(size: 24, weight: .bold)).foregroundColor(.white).lineLimit(1)
                        Spacer()
                        if let q = quality { Badge(text: q) }
                    }
                    if let e = player.epg, !e.title.isEmpty {
                        Text("\(e.title)   \(clock(e.start)) – \(clock(e.end))")
                            .font(.system(size: 15)).foregroundColor(MN.text2).lineLimit(1)
                        ProgressLine(fraction: (Date().timeIntervalSince1970 - e.start) / max(1, e.end - e.start))
                    }
                    Text("↑↓ kanal   ← önceki kanal   Enter kanal listesi   Rakamlar kanal no   Space duraklat   F tam ekran")
                        .font(.system(size: 12)).foregroundColor(MN.muted)
                }
                .padding(.horizontal, 40).padding(.top, 50).padding(.bottom, 28)
                .background(LinearGradient(colors: [.clear, Color.black.opacity(0.9)], startPoint: .top, endPoint: .bottom))
            }
        }
        .allowsHitTesting(false)
    }

    private var quality: String? {
        let h = player.videoHeight
        if h >= 2000 { return "4K" }
        if h >= 1000 { return "FHD" }
        if h >= 700 { return "HD" }
        if h > 0 { return "SD" }
        return nil
    }

    private func clock(_ t: TimeInterval) -> String {
        let f = DateFormatter()
        f.dateFormat = "HH:mm"
        return f.string(from: Date(timeIntervalSince1970: t))
    }

    private var channelList: some View {
        HStack {
            VStack(alignment: .leading, spacing: 0) {
                Text(request.listTitle.isEmpty ? "Kanallar" : request.listTitle)
                    .font(.system(size: 14)).foregroundColor(MN.text2)
                    .padding(.leading, 12).padding(.top, 96).padding(.bottom, 10)
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 2) {
                            ForEach(Array(player.channels.enumerated()), id: \.element.key) { i, ch in
                                channelRow(ch, index: i)
                                    .id(i)
                                    .onTapGesture {
                                        listIndex = i
                                        listVisible = false
                                        if i != player.current { player.tune(i, immediate: true) }
                                    }
                            }
                        }
                        .padding(.horizontal, 8)
                        .padding(.bottom, 20)
                    }
                    .onAppear { proxy.scrollTo(listIndex, anchor: .center) }
                    .onChange(of: listIndex) { proxy.scrollTo(listIndex) }
                }
            }
            .frame(width: 400)
            .background(Color(red: 0.024, green: 0.031, blue: 0.047).opacity(0.94))
            Spacer()
        }
    }

    private func channelRow(_ ch: MediaEntry, index: Int) -> some View {
        let focused = index == listIndex
        let current = index == player.current
        let fav = { _ = model.revision; return model.store.isFavorite(ch.key) }()
        return HStack(spacing: 10) {
            Text(ch.number > 0 ? "\(ch.number)" : "\(index + 1)")
                .font(.system(size: 13)).foregroundColor(focused ? .white : (current ? MN.blue : MN.text2))
                .frame(width: 44, alignment: .leading)
            RemoteImage(url: ch.icon, fill: false).frame(width: 36, height: 36)
            Text(ch.name).font(.system(size: 15)).foregroundColor(.white).lineLimit(1)
            Spacer(minLength: 0)
            if fav { Image(systemName: "star.fill").font(.system(size: 11)).foregroundColor(focused ? .white : MN.blue) }
        }
        .padding(.horizontal, 10)
        .frame(height: 50)
        .background(RoundedRectangle(cornerRadius: 9).fill(focused ? MN.blue : (current ? MN.card : .clear)))
        .contentShape(Rectangle())
    }

    // MARK: vod

    private var vodControls: some View {
        VStack {
            Spacer()
            VStack(spacing: 10) {
                HStack(spacing: 12) {
                    Text(formatTime(scrubbing ?? player.position)).font(.system(size: 13, design: .monospaced)).foregroundColor(.white)
                    Slider(value: Binding(
                        get: { scrubbing ?? player.position },
                        set: { scrubbing = $0 }
                    ), in: 0...max(1, player.duration), onEditingChanged: { editing in
                        if !editing, let target = scrubbing {
                            player.seek(to: target)
                            scrubbing = nil
                        }
                        poke()
                    })
                    .tint(MN.blue)
                    Text(formatTime(player.duration)).font(.system(size: 13, design: .monospaced)).foregroundColor(MN.text2)
                }
                HStack(spacing: 12) {
                    if player.hasPreviousEpisode {
                        Button(action: { player.playEpisode(player.episodeIndex - 1, start: 0) }) { Image(systemName: "backward.end.fill") }
                            .buttonStyle(PillButtonStyle()).help("Önceki bölüm (P)")
                    }
                    Button(action: { player.seek(by: -10); showSeekHUD(-10) }) { Image(systemName: "gobackward.10") }
                        .buttonStyle(PillButtonStyle())
                    Button(action: { player.togglePause(); poke() }) {
                        Image(systemName: player.status == .paused ? "play.fill" : "pause.fill").frame(width: 20)
                    }
                    .buttonStyle(PillButtonStyle(prominent: true))
                    Button(action: { player.seek(by: 10); showSeekHUD(10) }) { Image(systemName: "goforward.10") }
                        .buttonStyle(PillButtonStyle())
                    if player.hasNextEpisode {
                        Button(action: { player.playEpisode(player.episodeIndex + 1, start: 0) }) {
                            Label("Sonraki bölüm", systemImage: "forward.end.fill")
                        }
                        .buttonStyle(PillButtonStyle()).help("Sonraki bölüm (N)")
                    }
                    Spacer()
                    volumeControl
                }
            }
            .padding(.horizontal, 32).padding(.top, 40).padding(.bottom, 24)
            .background(LinearGradient(colors: [.clear, Color.black.opacity(0.9)], startPoint: .top, endPoint: .bottom))
        }
    }

    private var volumeControl: some View {
        HStack(spacing: 8) {
            Button(action: { player.toggleMute() }) {
                Image(systemName: player.muted || player.volume == 0 ? "speaker.slash.fill" : "speaker.wave.2.fill").frame(width: 20)
            }
            .buttonStyle(PillButtonStyle())
            Slider(value: Binding(get: { player.volume }, set: { player.setVolume($0) }), in: 0...100)
                .frame(width: 110)
                .tint(MN.blue)
        }
    }

    // MARK: helpers

    private func poke() {
        controlsVisible = true
        NSCursor.setHiddenUntilMouseMoves(false)
        hideTask?.cancel()
        hideTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 3_500_000_000)
            if Task.isCancelled || scrubbing != nil { return }
            controlsVisible = false
            if !listVisible { NSCursor.setHiddenUntilMouseMoves(true) }
        }
    }

    private func flashInfo() {
        infoVisible = true
        infoTask?.cancel()
        infoTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 5_000_000_000)
            if !Task.isCancelled { infoVisible = false }
        }
    }

    private func showHUD(_ text: String) {
        hud = text
        hudTask?.cancel()
        hudTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_200_000_000)
            if !Task.isCancelled { hud = nil }
        }
    }

    private func showSeekHUD(_ delta: Double) {
        let total = player.duration > 0 ? " / \(formatTime(player.duration))" : ""
        showHUD("\(delta < 0 ? "◀◀" : "▶▶")  \(formatTime(player.position))\(total)")
    }

    private func toggleList() {
        listIndex = player.current
        listVisible.toggle()
        poke()
    }

    private func toggleFullScreen() {
        (NSApp.keyWindow ?? NSApp.windows.first)?.toggleFullScreen(nil)
    }

    private var isFullScreen: Bool {
        (NSApp.keyWindow ?? NSApp.windows.first)?.styleMask.contains(.fullScreen) ?? false
    }

    private func goBack() {
        player.saveProgress()
        model.back()
    }

    private func appendDigit(_ d: String) {
        if numberBuffer.count >= 5 { numberBuffer = "" }
        numberBuffer += d
        numberTask?.cancel()
        numberTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            if Task.isCancelled { return }
            let n = Int(numberBuffer) ?? 0
            numberBuffer = ""
            if !player.jump(toNumber: n) { model.showToast("\(n) numaralı kanal bu listede yok") }
        }
    }

    // MARK: keyboard

    private func handleKey(_ press: KeyPress) -> KeyPress.Result {
        guard case .player(let top)? = model.route.last, top == request else { return .ignored }
        let repeating = press.phase == .repeat
        let chars = press.characters.lowercased()

        // Shared keys
        if press.key == .escape {
            if player.isLive && listVisible { listVisible = false }
            else if isFullScreen { toggleFullScreen() }
            else { goBack() }
            return .handled
        }
        if chars == "f" && !repeating { toggleFullScreen(); return .handled }
        if chars == "m" && !repeating { player.toggleMute(); showHUD(player.muted ? "Ses kapalı" : "Ses: \(Int(player.volume))"); return .handled }
        if chars == "+" || chars == "=" { player.changeVolume(by: 5); showHUD("Ses: \(Int(player.volume))"); return .handled }
        if chars == "-" { player.changeVolume(by: -5); showHUD("Ses: \(Int(player.volume))"); return .handled }

        if player.isLive {
            if listVisible {
                switch press.key {
                case .upArrow: listIndex = max(0, listIndex - 1); return .handled
                case .downArrow: listIndex = min(player.channels.count - 1, listIndex + 1); return .handled
                case .return, .rightArrow:
                    listVisible = false
                    if listIndex != player.current { player.tune(listIndex, immediate: true) }
                    return .handled
                case .leftArrow: listVisible = false; return .handled
                default: break
                }
                if chars == "l" { listVisible = false; return .handled }
                return .ignored
            }
            switch press.key {
            case .upArrow: player.zap(-1, repeating: repeating); flashInfo(); return .handled
            case .downArrow: player.zap(1, repeating: repeating); flashInfo(); return .handled
            case .leftArrow: if !repeating { player.goPrevious() }; return .handled
            case .rightArrow: if !repeating { if infoVisible { infoVisible = false } else { flashInfo() } }; return .handled
            case .return: if !repeating { toggleList() }; return .handled
            case .space: if !repeating { player.togglePause() }; return .handled
            default: break
            }
            if chars == "l" && !repeating { toggleList(); return .handled }
            if chars.count == 1, let c = chars.first, c.isNumber, !repeating { appendDigit(String(c)); return .handled }
            return .ignored
        }

        // Movie / series
        let step: Double = press.modifiers.contains(.shift) ? 60 : 10
        switch press.key {
        case .leftArrow: player.seek(by: -step); showSeekHUD(-step); poke(); return .handled
        case .rightArrow: player.seek(by: step); showSeekHUD(step); poke(); return .handled
        case .upArrow: player.changeVolume(by: 5); showHUD("Ses: \(Int(player.volume))"); return .handled
        case .downArrow: player.changeVolume(by: -5); showHUD("Ses: \(Int(player.volume))"); return .handled
        case .space, .return: if !repeating { player.togglePause(); poke() }; return .handled
        default: break
        }
        if chars == "n" && !repeating && player.hasNextEpisode { player.playEpisode(player.episodeIndex + 1, start: 0); return .handled }
        if chars == "p" && !repeating && player.hasPreviousEpisode { player.playEpisode(player.episodeIndex - 1, start: 0); return .handled }
        return .ignored
    }
}
