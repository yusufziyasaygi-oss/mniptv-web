import SwiftUI

struct SeriesView: View {
    let entry: MediaEntry
    @EnvironmentObject var model: AppModel

    enum Area { case buttons, seasons, episodes }

    @State private var info: SeriesInfo?
    @State private var error: String?
    @State private var seasonIndex = 0
    @State private var area: Area = .episodes
    @State private var buttonIndex = 0
    @State private var episodeIndex = 0
    @State private var pendingPlay: (Episode, Double)?
    @State private var askResume = false
    @FocusState private var keyFocus: Bool

    private var name: String { info?.name.isEmpty == false ? info!.name : entry.name }
    private var cover: String? { entry.icon ?? info?.cover }
    private var seasons: [Int] { info?.seasons ?? [] }
    private var episodes: [Episode] {
        guard let info = info, seasons.indices.contains(seasonIndex) else { return [] }
        return info.episodesBySeason[seasons[seasonIndex]] ?? []
    }
    private var resume: (Progress, Episode)? {
        _ = model.revision
        guard let p = model.store.progress("SERIES:\(entry.id)"), let id = p.episodeId,
              let ep = info?.allEpisodes.first(where: { $0.id == id }) else { return nil }
        return (p, ep)
    }
    private var buttonCount: Int { resume == nil ? 1 : 2 }
    private var isFavorite: Bool { _ = model.revision; return model.store.isFavorite(entry.key) }

    var body: some View {
        ZStack(alignment: .topLeading) {
            MN.black
            Color.clear
                .overlay(RemoteImage(url: cover).opacity(0.16))
                .clipped()
                .allowsHitTesting(false)
            HStack(alignment: .top, spacing: 36) {
                ZStack {
                    Rectangle().fill(MN.card)
                    RemoteImage(url: cover)
                }
                .frame(width: 220, height: 330)
                .clipShape(RoundedRectangle(cornerRadius: 10))
                details
            }
            .padding(.horizontal, 48)
            .padding(.top, 70)

            Button(action: { model.back() }) {
                Label("Geri", systemImage: "chevron.left")
            }
            .buttonStyle(PillButtonStyle())
            .padding(.leading, 48)
            .padding(.top, 20)
        }
        .focusable()
        .focused($keyFocus)
        .focusEffectDisabled()
        .onKeyPress(phases: .down) { handleKey($0) }
        .onAppear {
            keyFocus = true
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 300_000_000)
                keyFocus = true
            }
        }
        .task(id: entry.id) { await load() }
        .confirmationDialog(pendingTitle, isPresented: $askResume, titleVisibility: .visible) {
            if let pp = pendingPlay {
                Button("Kaldığın yerden devam et (\(formatTime(pp.1)))") { play(pp.0, start: pp.1) }
                Button("Baştan başla") { play(pp.0, start: 0) }
            }
            Button("Vazgeç", role: .cancel) {}
        }
    }

    private var pendingTitle: String {
        guard let ep = pendingPlay?.0 else { return "" }
        return "S\(ep.season) B\(ep.number) · \(ep.title)"
    }

    private var details: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(name).font(.system(size: 30, weight: .bold)).foregroundColor(.white).lineLimit(2)
            if let plot = info?.plot, !plot.isEmpty {
                Text(plot).font(.system(size: 14)).foregroundColor(MN.text2).lineLimit(3).padding(.top, 8)
            }
            HStack(spacing: 12) {
                if let r = resume {
                    let p = r.0, ep = r.1
                    Button(action: { play(ep, start: p.position) }) {
                        Text(p.position > 0 ? "Devam et · S\(ep.season) B\(ep.number) · \(formatTime(p.position))"
                                            : "Sıradaki · S\(ep.season) B\(ep.number)")
                    }
                    .buttonStyle(PillButtonStyle(prominent: keyFocus && area == .buttons && buttonIndex == 0))
                }
                Button(action: { model.toggleFavorite(entry) }) {
                    Text(isFavorite ? "Favorilerden çıkar" : "Favorilere ekle")
                }
                .buttonStyle(PillButtonStyle(prominent: keyFocus && area == .buttons && buttonIndex == buttonCount - 1))
            }
            .padding(.top, 18)

            if let error = error {
                Text(error).font(.system(size: 15)).foregroundColor(MN.text2).padding(.top, 40)
            } else if info == nil {
                ProgressView().padding(.top, 40)
            } else {
                seasonRow.padding(.top, 22)
                episodeList.padding(.top, 10)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var seasonRow: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 10) {
                ForEach(Array(seasons.enumerated()), id: \.offset) { i, s in
                    let focused = keyFocus && area == .seasons && seasonIndex == i
                    Text("Sezon \(s)")
                        .font(.system(size: 14))
                        .foregroundColor(focused ? .white : (seasonIndex == i ? MN.blue : MN.text2))
                        .padding(.horizontal, 18).frame(height: 36)
                        .background(Capsule().fill(focused ? MN.blue : MN.card))
                        .overlay(Capsule().stroke(seasonIndex == i && !focused ? MN.blue : .clear, lineWidth: 2))
                        .contentShape(Capsule())
                        .onTapGesture { seasonIndex = i; episodeIndex = 0; area = .seasons; keyFocus = true }
                }
            }
            .padding(2)
        }
    }

    private var episodeList: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 6) {
                    ForEach(Array(episodes.enumerated()), id: \.element.id) { i, ep in
                        episodeRow(ep, focused: keyFocus && area == .episodes && episodeIndex == i)
                            .id(ep.id)
                            .onTapGesture {
                                area = .episodes
                                episodeIndex = i
                                keyFocus = true
                                choose(ep)
                            }
                    }
                }
                .padding(.vertical, 4)
                .padding(.bottom, 30)
            }
            .onChange(of: episodeIndex) {
                if episodes.indices.contains(episodeIndex) { proxy.scrollTo(episodes[episodeIndex].id) }
            }
        }
    }

    private func episodeRow(_ ep: Episode, focused: Bool) -> some View {
        _ = model.revision
        let watched = model.store.isWatched(ep.id)
        let pos = model.store.episodePosition(ep.id)
        var meta: [String] = []
        if ep.durationSec > 0 { meta.append("\((ep.durationSec + 59) / 60) dk") }
        if watched { meta.append("İzlendi") } else if pos > 0 { meta.append("Kaldığın yer \(formatTime(pos))") }
        return HStack(spacing: 14) {
            Text("B\(ep.number)").font(.system(size: 15, weight: .bold)).foregroundColor(MN.blue).frame(width: 46, alignment: .leading)
            VStack(alignment: .leading, spacing: 3) {
                Text(ep.title).font(.system(size: 15)).foregroundColor(.white).lineLimit(1)
                if !meta.isEmpty {
                    Text(meta.joined(separator: "  ·  ")).font(.system(size: 12)).foregroundColor(MN.text2)
                }
                if !watched && pos > 0 && ep.durationSec > 0 {
                    ProgressLine(fraction: pos / Double(ep.durationSec)).padding(.top, 3)
                }
            }
            Spacer(minLength: 0)
            Image(systemName: "play.fill").foregroundColor(focused ? .white : MN.muted)
        }
        .padding(.horizontal, 16).padding(.vertical, 10)
        .frame(minHeight: 62)
        .background(RoundedRectangle(cornerRadius: 10).fill(focused ? MN.cardFocus : MN.card))
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(focused ? MN.blue : .clear, lineWidth: 2.5))
        .contentShape(RoundedRectangle(cornerRadius: 10))
    }

    private func load() async {
        do {
            let i = try await model.seriesInfo(entry.id)
            info = i
            if i.seasons.isEmpty {
                error = "Bu dizide bölüm bulunamadı"
                area = .buttons
                return
            }
            if let p = model.store.progress("SERIES:\(entry.id)"), let si = i.seasons.firstIndex(of: p.season) {
                seasonIndex = si
                if let ei = (i.episodesBySeason[p.season] ?? []).firstIndex(where: { $0.id == p.episodeId }) { episodeIndex = ei }
                area = .buttons
                buttonIndex = 0
            }
        } catch {
            self.error = "Dizi bilgisi alınamadı: \(error.localizedDescription)"
            area = .buttons
        }
    }

    private func choose(_ ep: Episode) {
        let saved = model.store.episodePosition(ep.id)
        if saved > 30 && !model.store.isWatched(ep.id) {
            pendingPlay = (ep, saved)
            askResume = true
        } else {
            play(ep, start: 0)
        }
    }

    private func play(_ ep: Episode, start: Double) {
        guard let info = info else { return }
        let all = info.allEpisodes
        guard let index = all.firstIndex(where: { $0.id == ep.id }) else { return }
        model.playSeries(id: entry.id, name: name, cover: cover, episodes: all, index: index, start: start)
    }

    private func handleKey(_ press: KeyPress) -> KeyPress.Result {
        guard case .series(let top)? = model.route.last, top.key == entry.key, !askResume else { return .ignored }
        switch press.key {
        case .escape:
            model.back()
        case .upArrow:
            switch area {
            case .episodes:
                if episodeIndex > 0 { episodeIndex -= 1 } else if !seasons.isEmpty { area = .seasons } else { area = .buttons }
            case .seasons:
                area = .buttons
                buttonIndex = 0
            case .buttons:
                break
            }
        case .downArrow:
            switch area {
            case .buttons:
                if !seasons.isEmpty { area = .seasons } else if !episodes.isEmpty { area = .episodes }
            case .seasons:
                if !episodes.isEmpty { area = .episodes; episodeIndex = 0 }
            case .episodes:
                if episodeIndex + 1 < episodes.count { episodeIndex += 1 }
            }
        case .leftArrow:
            if area == .seasons && seasonIndex > 0 { seasonIndex -= 1; episodeIndex = 0 }
            if area == .buttons && buttonIndex > 0 { buttonIndex -= 1 }
        case .rightArrow:
            if area == .seasons && seasonIndex + 1 < seasons.count { seasonIndex += 1; episodeIndex = 0 }
            if area == .buttons && buttonIndex + 1 < buttonCount { buttonIndex += 1 }
        case .return, .space:
            switch area {
            case .buttons:
                if buttonIndex == buttonCount - 1 { model.toggleFavorite(entry) }
                else if let r = resume { play(r.1, start: r.0.position) }
            case .seasons:
                if !episodes.isEmpty { area = .episodes; episodeIndex = 0 }
            case .episodes:
                if episodes.indices.contains(episodeIndex) { choose(episodes[episodeIndex]) }
            }
        default:
            return .ignored
        }
        return .handled
    }
}
