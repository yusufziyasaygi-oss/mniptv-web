import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
struct MNIPTVApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @StateObject private var model: AppModel

    init() {
        if SmokeRunner.directory != nil { Store.shared.resetAll() }
        _model = StateObject(wrappedValue: AppModel())
    }

    var body: some Scene {
        Window("MN IPTV", id: "main") {
            RootView()
                .environmentObject(model)
                .frame(minWidth: 1100, minHeight: 680)
                .preferredColorScheme(.dark)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1440, height: 880)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Gezin") {
                ForEach(Section.allCases, id: \.rawValue) { s in
                    Button(s.title) { model.goToSection(s) }
                        .keyboardShortcut(KeyEquivalent(Character(String(s.rawValue + 1))), modifiers: .command)
                }
                Divider()
                Button("Ara") { model.goToSection(.search) }.keyboardShortcut("f", modifiers: .command)
                Button("Listeyi yenile") { model.reloadSection() }.keyboardShortcut("r", modifiers: .command)
                Button("Geri") { model.back() }.keyboardShortcut("[", modifiers: .command)
            }
            CommandMenu("Oynatıcı") {
                Button((model.usesMNPlayer ? "" : "✓ ") + "MN IPTV") { model.setPlaybackApp(mnPlayer: false) }
                Button((model.usesMNPlayer ? "✓ " : "") + "MN Player") { model.setPlaybackApp(mnPlayer: true) }
            }
        }
    }
}

struct RootView: View {
    @EnvironmentObject var model: AppModel

    @StateObject private var loginForm = LoginForm()
    @State private var accountDialog = false
    @State private var changingAccount = false

    var body: some View {
        ZStack {
            // The video surface never leaves the window; player screens are transparent overlays on top of it.
            if model.account != nil, let engine = PlaybackEngine.shared {
                MPVSurfaceView(surface: engine.surface)
                    .allowsHitTesting(false)
            }
            if model.account == nil {
                LoginView(form: loginForm)
            } else {
                BrowseView(onAccount: { accountDialog = true })
                    .opacity(model.route.isEmpty ? 1 : 0)
                    .allowsHitTesting(model.route.isEmpty)
                if let top = model.route.last {
                    switch top {
                    case .series(let e):
                        SeriesView(entry: e).id("S:" + e.key)
                    case .player(let r):
                        PlayerScreen(request: r, model: model).id(r.token)
                    }
                }
                if changingAccount {
                    LoginView(form: loginForm, onCancel: { changingAccount = false })
                }
            }
            if let toast = model.toast {
                VStack {
                    Spacer()
                    ToastView(text: toast).padding(.bottom, 30)
                }
                .allowsHitTesting(false)
            }
        }
        .background(MN.black)
        .onAppear { model.startIfNeeded() }
        .task {
            if let dir = SmokeRunner.directory {
                await SmokeRunner(model: model, form: loginForm, directory: dir).run()
            }
        }
        .confirmationDialog("Hesap", isPresented: $accountDialog, titleVisibility: .visible) {
            Button("Listeyi yenile") { model.reloadSection() }
            Button(model.usesMNPlayer ? "Oynatıcıyı MN IPTV yap" : "Oynatıcıyı MN Player yap") {
                model.setPlaybackApp(mnPlayer: !model.usesMNPlayer)
            }
            Button("Hesabı değiştir") {
                loginForm.error = nil
                changingAccount = true
            }
            Button("Çıkış yap", role: .destructive) { model.signOut() }
            Button("Vazgeç", role: .cancel) {}
        } message: {
            Text("\(model.account?.username ?? "")\n\(model.account?.server ?? "")\nOynatıcı: \(model.usesMNPlayer ? "MN Player" : "MN IPTV")")
        }
    }
}
