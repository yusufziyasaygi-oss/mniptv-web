import SwiftUI

struct BrowseView: View {
    @EnvironmentObject var model: AppModel
    var onAccount: () -> Void

    enum Area { case sidebar, categories, search, grid }

    @State private var area: Area = .sidebar
    @State private var sideIndex = 1
    @State private var catIndex = 0
    @State private var gridIndex = 0
    @State private var liveCols = 2
    @State private var posterCols = 4
    @State private var categoryWork: Task<Void, Never>?
    @State private var searchWork: Task<Void, Never>?
    @FocusState private var keyFocus: Bool
    @FocusState private var searchFocus: Bool

    private let accountIndex = Section.allCases.count

    private var liveTiles: [Tile] { model.tiles.filter { $0.isLive } }
    private var posterTiles: [Tile] { model.tiles.filter { !$0.isLive } }
    /// Display order: channels first, then posters (matters for mixed favourites/search lists).
    private var ordered: [Tile] { liveTiles + posterTiles }

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            if model.section.kind != nil { categoryColumn }
            content
        }
        .background(MN.black)
        .focusable()
        .focused($keyFocus)
        .focusEffectDisabled()
        .onKeyPress(phases: .down) { press in handleKey(press) }
        .onAppear {
            sideIndex = model.section.rawValue
            reclaimFocus()
        }
        .onChange(of: model.section) {
            sideIndex = model.section.rawValue
            catIndex = 0
            gridIndex = 0
            if model.section == .search {
                area = .search
                searchFocus = true
            } else if area == .search {
                area = .sidebar
            }
        }
        .onChange(of: model.selectedCategory) { gridIndex = 0 }
        .onChange(of: model.tiles) {
            if gridIndex >= model.tiles.count { gridIndex = max(0, model.tiles.count - 1) }
            if model.tiles.isEmpty && area == .grid { area = model.section.kind != nil ? .categories : .sidebar }
        }
        .onChange(of: model.route.isEmpty) {
            if model.route.isEmpty { reclaimFocus() }
        }
    }

    // MARK: sidebar

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 10) {
                Circle().fill(MN.blue).frame(width: 13, height: 13)
                Text("MN IPTV").font(.system(size: 19, weight: .bold)).foregroundColor(.white)
            }
            .padding(.leading, 14)
            .padding(.top, 34)
            .padding(.bottom, 22)

            ForEach(Section.allCases, id: \.rawValue) { s in
                SideRow(title: s.title, symbol: s.symbol, selected: model.section == s,
                        focused: keyFocus && area == .sidebar && sideIndex == s.rawValue)
                    .onTapGesture {
                        area = .sidebar
                        sideIndex = s.rawValue
                        activateSidebar()
                    }
            }
            Spacer()
            SideRow(title: "Hesap", symbol: "person.crop.circle", selected: false,
                    focused: keyFocus && area == .sidebar && sideIndex == accountIndex)
                .onTapGesture { onAccount() }
                .padding(.bottom, 14)
        }
        .padding(.horizontal, 12)
        .frame(width: 210)
        .background(MN.surface)
    }

    // MARK: categories

    private var categoryColumn: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(model.section.title)
                .font(.system(size: 13)).foregroundColor(MN.muted)
                .padding(.leading, 14).padding(.top, 34).padding(.bottom, 14)
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 3) {
                        ForEach(Array(model.categories.enumerated()), id: \.offset) { i, cat in
                            SideRow(title: cat.name, selected: model.selectedCategory == i,
                                    focused: keyFocus && area == .categories && catIndex == i)
                                .id(i)
                                .onTapGesture {
                                    area = .categories
                                    catIndex = i
                                    model.selectCategory(i)
                                    keyFocus = true
                                }
                        }
                    }
                    .padding(.horizontal, 10)
                    .padding(.bottom, 20)
                }
                .onChange(of: catIndex) { proxy.scrollTo(catIndex) }
            }
        }
        .frame(width: 250)
    }

    // MARK: content

    private var content: some View {
        VStack(alignment: .leading, spacing: 0) {
            if model.section == .search {
                TextField("Kanal, film veya dizi ara", text: $model.query)
                    .textFieldStyle(.plain)
                    .font(.system(size: 16))
                    .foregroundColor(.white)
                    .padding(.horizontal, 14)
                    .frame(height: 44)
                    .background(RoundedRectangle(cornerRadius: 9).fill(MN.card))
                    .overlay(RoundedRectangle(cornerRadius: 9).stroke(searchFocus ? MN.blue : MN.divider, lineWidth: 2))
                    .focused($searchFocus)
                    .onChange(of: model.query) { scheduleSearch() }
                    .onSubmit { searchWork?.cancel(); model.runSearch(); leaveSearch() }
                    .onKeyPress(.downArrow) { leaveSearch(); return .handled }
                    .onKeyPress(.escape) { searchFocus = false; area = .sidebar; keyFocus = true; return .handled }
                    .padding(.top, 30)
                    .padding(.bottom, 10)
            }
            Text(model.contentTitle)
                .font(.system(size: 18, weight: .bold)).foregroundColor(.white).lineLimit(1)
                .padding(.top, model.section == .search ? 4 : 30)
                .padding(.bottom, 12)
                .padding(.leading, 4)
            GeometryReader { geo in
                ZStack {
                    grid(width: geo.size.width)
                    if model.loading {
                        VStack(spacing: 14) {
                            ProgressView().controlSize(.large)
                            if model.slowLoading {
                                Text("Sunucu yavaş yanıt veriyor, bekleniyor…")
                                    .font(.system(size: 14)).foregroundColor(MN.text2)
                            }
                        }
                    } else if let empty = model.emptyMessage {
                        Text(empty).font(.system(size: 15)).foregroundColor(MN.text2)
                            .multilineTextAlignment(.center).frame(maxWidth: 520)
                    }
                }
                .onAppear { updateColumns(geo.size.width) }
                .onChange(of: geo.size.width) { updateColumns(geo.size.width) }
            }
        }
        .padding(.horizontal, 22)
        .frame(maxWidth: .infinity)
    }

    private func updateColumns(_ width: CGFloat) {
        liveCols = max(1, Int((width + 12) / (250 + 12)))
        posterCols = max(2, Int((width + 12) / (150 + 12)))
    }

    private func grid(width: CGFloat) -> some View {
        let lives = liveTiles
        let posters = posterTiles
        return ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    if !lives.isEmpty {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12), count: liveCols), spacing: 12) {
                            ForEach(Array(lives.enumerated()), id: \.element.key) { i, tile in
                                tileView(tile, index: i)
                            }
                        }
                    }
                    if !posters.isEmpty {
                        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12), count: posterCols), spacing: 14) {
                            ForEach(Array(posters.enumerated()), id: \.element.key) { i, tile in
                                tileView(tile, index: lives.count + i)
                            }
                        }
                    }
                }
                .padding(.vertical, 6)
                .padding(.horizontal, 4)
                .padding(.bottom, 30)
            }
            .onChange(of: gridIndex) {
                let list = ordered
                if list.indices.contains(gridIndex) { proxy.scrollTo(list[gridIndex].key) }
            }
            .onChange(of: model.contentTitle) {
                if let first = ordered.first { proxy.scrollTo(first.key, anchor: .top) }
            }
        }
    }

    @ViewBuilder
    private func tileView(_ tile: Tile, index: Int) -> some View {
        let focused = keyFocus && area == .grid && gridIndex == index
        Group {
            switch tile {
            case .media(let e, let fav, let showKind):
                if e.kind == .live {
                    LiveTile(entry: e, favorite: fav, focused: focused)
                } else {
                    PosterTile(image: e.icon, title: e.name, subtitle: nil,
                               badge: showKind ? (e.kind == .movie ? "Film" : "Dizi") : nil,
                               favorite: fav, progress: nil, focused: focused)
                }
            case .resume(let p):
                PosterTile(image: p.image, title: p.title,
                           subtitle: p.kind == .series
                               ? (p.position <= 0 ? "Sıradaki: S\(p.season) B\(p.episode)" : "S\(p.season) B\(p.episode) · \(formatTime(p.position))")
                               : formatTime(p.position),
                           badge: p.kind == .series ? "Dizi" : "Film", favorite: false,
                           progress: p.duration > 0 && p.position > 0 ? p.fraction : nil, focused: focused)
            }
        }
        .id(tile.key)
        .onTapGesture {
            area = .grid
            gridIndex = index
            keyFocus = true
            model.open(tile)
        }
        .contextMenu { contextMenu(for: tile) }
    }

    @ViewBuilder
    private func contextMenu(for tile: Tile) -> some View {
        switch tile {
        case .media(let e, let fav, _):
            Button(e.kind == .movie ? "Oynat" : "Aç") { model.open(tile) }
            if e.kind == .movie, model.store.progress(e.key) != nil {
                Button("Baştan başla") { model.playMovie(e, start: 0) }
            }
            Button(fav ? "Favorilerden çıkar" : "Favorilere ekle") { model.toggleFavorite(e) }
        case .resume(let p):
            Button("Devam et") { model.open(tile) }
            Button("İzlemeye Devam'dan kaldır") { model.removeProgress(p) }
        }
    }

    // MARK: actions

    private func reclaimFocus() {
        keyFocus = true
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 300_000_000)
            if model.route.isEmpty && !searchFocus { keyFocus = true }
        }
    }

    private func activateSidebar() {
        keyFocus = true
        if sideIndex == accountIndex { onAccount(); return }
        guard let s = Section(rawValue: sideIndex) else { return }
        model.select(s, force: s == model.section && s.kind != nil && model.categories.isEmpty)
        if s == .search {
            area = .search
            searchFocus = true
        }
    }

    private func enterFromSidebar() {
        guard let s = Section(rawValue: sideIndex) else { onAccount(); return }
        if s != model.section { model.select(s) }
        if s == .search {
            area = .search
            searchFocus = true
        } else if s.kind != nil {
            area = .categories
            catIndex = max(0, model.selectedCategory)
        } else if !model.tiles.isEmpty {
            area = .grid
            gridIndex = 0
        }
    }

    private func leaveSearch() {
        searchFocus = false
        keyFocus = true
        if !model.tiles.isEmpty {
            area = .grid
            gridIndex = 0
        }
    }

    private func scheduleSearch() {
        searchWork?.cancel()
        searchWork = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 500_000_000)
            if !Task.isCancelled { model.runSearch() }
        }
    }

    private func scheduleCategory(_ i: Int) {
        categoryWork?.cancel()
        categoryWork = Task { @MainActor in
            try? await Task.sleep(nanoseconds: 250_000_000)
            if !Task.isCancelled { model.selectCategory(i) }
        }
    }

    // MARK: keyboard

    private func handleKey(_ press: KeyPress) -> KeyPress.Result {
        guard model.route.isEmpty else { return .ignored }
        let count = ordered.count
        switch press.key {
        case .upArrow, .downArrow:
            let up = press.key == .upArrow
            switch area {
            case .sidebar:
                sideIndex = min(accountIndex, max(0, sideIndex + (up ? -1 : 1)))
                if sideIndex < accountIndex, let s = Section(rawValue: sideIndex), s != .search { model.select(s) }
            case .categories:
                let n = model.categories.count
                guard n > 0 else { return .handled }
                catIndex = min(n - 1, max(0, catIndex + (up ? -1 : 1)))
                scheduleCategory(catIndex)
            case .grid:
                moveGridVertically(up: up)
            case .search:
                return .ignored
            }
            return .handled
        case .leftArrow:
            switch area {
            case .grid:
                if isFirstColumn(gridIndex) {
                    if model.section.kind != nil { area = .categories; catIndex = max(0, model.selectedCategory) }
                    else { area = .sidebar }
                } else {
                    gridIndex -= 1
                }
            case .categories:
                area = .sidebar
            default:
                break
            }
            return .handled
        case .rightArrow:
            switch area {
            case .sidebar:
                if model.section.kind != nil && !model.categories.isEmpty {
                    area = .categories
                    catIndex = max(0, model.selectedCategory)
                } else if model.section == .search {
                    area = .search
                    searchFocus = true
                } else if count > 0 {
                    area = .grid
                    gridIndex = 0
                }
            case .categories:
                categoryWork?.cancel()
                if catIndex != model.selectedCategory { model.selectCategory(catIndex) }
                else if count > 0 { area = .grid; gridIndex = 0 }
            case .grid:
                if !isLastColumn(gridIndex) && gridIndex + 1 < count { gridIndex += 1 }
            case .search:
                return .ignored
            }
            return .handled
        case .return, .space:
            switch area {
            case .sidebar:
                if sideIndex == accountIndex { onAccount() } else { enterFromSidebar() }
            case .categories:
                categoryWork?.cancel()
                if catIndex != model.selectedCategory { model.selectCategory(catIndex) }
                if count > 0 { area = .grid; gridIndex = 0 }
            case .grid:
                if ordered.indices.contains(gridIndex) { model.open(ordered[gridIndex]) }
            case .search:
                return .ignored
            }
            return .handled
        case .escape:
            switch area {
            case .grid:
                if model.section.kind != nil { area = .categories } else if model.section == .search { area = .search; searchFocus = true } else { area = .sidebar }
            case .categories:
                area = .sidebar
            default:
                break
            }
            return .handled
        default:
            if press.characters.lowercased() == "f", area == .grid, ordered.indices.contains(gridIndex),
               case .media(let e, _, _) = ordered[gridIndex] {
                model.toggleFavorite(e)
                return .handled
            }
            return .ignored
        }
    }

    // Grid geometry: channels block (liveCols wide) followed by posters block (posterCols wide).

    private func isFirstColumn(_ i: Int) -> Bool {
        let lc = liveTiles.count
        return i < lc ? i % liveCols == 0 : (i - lc) % posterCols == 0
    }

    private func isLastColumn(_ i: Int) -> Bool {
        let lc = liveTiles.count
        return i < lc ? i % liveCols == liveCols - 1 : (i - lc) % posterCols == posterCols - 1
    }

    private func moveGridVertically(up: Bool) {
        let lc = liveTiles.count, pc = posterTiles.count
        let i = gridIndex
        if i < lc {
            let col = i % liveCols
            if up {
                if i - liveCols >= 0 { gridIndex = i - liveCols }
                else if model.section == .search { area = .search; searchFocus = true }
            } else if i + liveCols < lc {
                gridIndex = i + liveCols
            } else if (i / liveCols) < (lc - 1) / liveCols {
                gridIndex = lc - 1
            } else if pc > 0 {
                gridIndex = lc + min(col, pc - 1)
            }
        } else {
            let j = i - lc
            let col = j % posterCols
            if up {
                if j - posterCols >= 0 { gridIndex = i - posterCols }
                else if lc > 0 {
                    let lastRowStart = ((lc - 1) / liveCols) * liveCols
                    gridIndex = min(lc - 1, lastRowStart + min(col, liveCols - 1))
                } else if model.section == .search { area = .search; searchFocus = true }
            } else if j + posterCols < pc {
                gridIndex = i + posterCols
            } else if (j / posterCols) < (pc - 1) / posterCols {
                gridIndex = lc + pc - 1
            }
        }
    }
}
