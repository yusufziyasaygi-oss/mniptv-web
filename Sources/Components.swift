import AppKit
import SwiftUI

enum MN {
    static let black = Color(red: 0, green: 0, blue: 0)
    static let surface = Color(red: 0.06, green: 0.067, blue: 0.082)
    static let card = Color(red: 0.086, green: 0.098, blue: 0.125)
    static let cardFocus = Color(red: 0.11, green: 0.15, blue: 0.22)
    static let blue = Color(red: 0.231, green: 0.545, blue: 1.0)
    static let blueDark = Color(red: 0.141, green: 0.416, blue: 0.839)
    static let text = Color.white
    static let text2 = Color(red: 0.616, green: 0.643, blue: 0.69)
    static let muted = Color(red: 0.353, green: 0.376, blue: 0.431)
    static let divider = Color(red: 0.125, green: 0.141, blue: 0.176)
    static let red = Color(red: 1.0, green: 0.231, blue: 0.188)
}

/// Small in-memory image cache so posters do not reload while scrolling.
@MainActor
final class ImageCache {
    static let shared = ImageCache()
    private let cache = NSCache<NSString, NSImage>()
    private var running: [String: Task<NSImage?, Never>] = [:]
    private let session: URLSession = {
        let c = URLSessionConfiguration.default
        c.timeoutIntervalForRequest = 15
        c.httpAdditionalHeaders = ["User-Agent": userAgent]
        c.urlCache = URLCache(memoryCapacity: 32 << 20, diskCapacity: 256 << 20)
        return URLSession(configuration: c)
    }()

    init() { cache.countLimit = 600 }

    func cached(_ url: String) -> NSImage? { cache.object(forKey: url as NSString) }

    func load(_ url: String) async -> NSImage? {
        if let img = cache.object(forKey: url as NSString) { return img }
        if let task = running[url] { return await task.value }
        let task = Task<NSImage?, Never> { [session] in
            guard let u = URL(string: url), let result = try? await session.data(from: u) else { return nil }
            return NSImage(data: result.0)
        }
        running[url] = task
        let img = await task.value
        running[url] = nil
        if let img = img { cache.setObject(img, forKey: url as NSString) }
        return img
    }
}

struct RemoteImage: View {
    let url: String?
    var fill = true
    @State private var image: NSImage?

    var body: some View {
        ZStack {
            if let image = image {
                if fill {
                    Image(nsImage: image).resizable().scaledToFill()
                } else {
                    Image(nsImage: image).resizable().scaledToFit()
                }
            }
        }
        .task(id: url) {
            image = nil
            guard let url = url, !url.isEmpty else { return }
            if let c = ImageCache.shared.cached(url) { image = c; return }
            image = await ImageCache.shared.load(url)
        }
    }
}

struct PillButtonStyle: ButtonStyle {
    var prominent = false
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 14, weight: .semibold))
            .foregroundColor(.white)
            .padding(.horizontal, 20)
            .frame(height: 38)
            .background(
                Capsule().fill(prominent ? MN.blue : Color(red: 0.145, green: 0.165, blue: 0.212))
                    .opacity(configuration.isPressed ? 0.75 : 1)
            )
            .contentShape(Capsule())
    }
}

struct ProgressLine: View {
    let fraction: Double
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.white.opacity(0.25))
                Capsule().fill(MN.blue).frame(width: g.size.width * max(0, min(1, fraction)))
            }
        }
        .frame(height: 4)
    }
}

struct Badge: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.system(size: 11, weight: .semibold))
            .foregroundColor(.white)
            .padding(.horizontal, 6).padding(.vertical, 2)
            .background(RoundedRectangle(cornerRadius: 6).fill(Color.black.opacity(0.75)))
    }
}

/// Wide tile for live channels.
struct LiveTile: View {
    let entry: MediaEntry
    let favorite: Bool
    let focused: Bool
    @State private var hover = false

    var body: some View {
        HStack(spacing: 12) {
            RemoteImage(url: entry.icon, fill: false)
                .frame(width: 52, height: 52)
            VStack(alignment: .leading, spacing: 2) {
                if entry.number > 0 {
                    Text("\(entry.number)").font(.system(size: 11)).foregroundColor(MN.muted)
                }
                Text(entry.name).font(.system(size: 14)).foregroundColor(.white).lineLimit(2)
            }
            Spacer(minLength: 0)
            if favorite { Image(systemName: "star.fill").font(.system(size: 12)).foregroundColor(MN.blue) }
        }
        .padding(.horizontal, 12)
        .frame(height: 72)
        .background(RoundedRectangle(cornerRadius: 12).fill(focused || hover ? MN.cardFocus : MN.card))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(focused ? MN.blue : (hover ? MN.blue.opacity(0.45) : .clear), lineWidth: 2.5))
        .contentShape(RoundedRectangle(cornerRadius: 12))
        .onHover { hover = $0 }
    }
}

/// Poster tile for movies, series and continue-watching cards.
struct PosterTile: View {
    let image: String?
    let title: String
    let subtitle: String?
    let badge: String?
    let favorite: Bool
    let progress: Double?
    let focused: Bool
    @State private var hover = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ZStack(alignment: .topLeading) {
                Rectangle().fill(Color(red: 0.1, green: 0.118, blue: 0.153))
                RemoteImage(url: image)
                if let badge = badge { Badge(text: badge).padding(5) }
                if favorite {
                    Image(systemName: "star.fill").font(.system(size: 11)).foregroundColor(MN.blue)
                        .padding(5).background(Circle().fill(Color.black.opacity(0.75)))
                        .padding(5).frame(maxWidth: .infinity, alignment: .topTrailing)
                }
                if let p = progress {
                    VStack { Spacer(); ProgressLine(fraction: p) }
                }
            }
            .frame(height: 190)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            Text(title).font(.system(size: 13)).foregroundColor(.white).lineLimit(2, reservesSpace: true)
            if let subtitle = subtitle {
                Text(subtitle).font(.system(size: 11)).foregroundColor(MN.text2).lineLimit(1)
            }
        }
        .padding(6)
        .background(RoundedRectangle(cornerRadius: 12).fill(focused || hover ? MN.cardFocus : MN.card))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(focused ? MN.blue : (hover ? MN.blue.opacity(0.45) : .clear), lineWidth: 2.5))
        .scaleEffect(focused ? 1.03 : 1)
        .animation(.easeOut(duration: 0.12), value: focused)
        .contentShape(RoundedRectangle(cornerRadius: 12))
        .onHover { hover = $0 }
    }
}

struct SideRow: View {
    let title: String
    var symbol: String?
    let selected: Bool
    let focused: Bool
    @State private var hover = false

    var body: some View {
        HStack(spacing: 10) {
            if let symbol = symbol {
                Image(systemName: symbol).frame(width: 18)
            }
            Text(title).lineLimit(1)
            Spacer(minLength: 0)
        }
        .font(.system(size: 15))
        .foregroundColor(focused ? .white : (selected ? MN.blue : MN.text2))
        .padding(.horizontal, 14)
        .frame(height: 40)
        .background(
            RoundedRectangle(cornerRadius: 9)
                .fill(focused ? MN.blue : (selected ? MN.card : (hover ? MN.card.opacity(0.6) : .clear)))
        )
        .contentShape(Rectangle())
        .onHover { hover = $0 }
    }
}

struct ToastView: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.system(size: 14, weight: .medium))
            .foregroundColor(.white)
            .padding(.horizontal, 18).padding(.vertical, 10)
            .background(Capsule().fill(Color(red: 0.15, green: 0.17, blue: 0.21)))
            .shadow(radius: 8)
    }
}
