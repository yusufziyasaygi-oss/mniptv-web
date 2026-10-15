import SwiftUI

final class LoginForm: ObservableObject {
    @Published var server = ""
    @Published var username = ""
    @Published var password = ""
    @Published var error: String?
    @Published var busy = false
}

struct LoginView: View {
    @EnvironmentObject var model: AppModel
    @ObservedObject var form: LoginForm
    var onCancel: (() -> Void)?

    private enum Field { case server, user, pass }
    @FocusState private var focus: Field?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 12) {
                Circle().fill(MN.blue).frame(width: 16, height: 16)
                Text("MN IPTV").font(.system(size: 26, weight: .bold)).foregroundColor(.white)
            }
            .padding(.bottom, 28)

            label("Sunucu adresi")
            field(TextField("http://sunucu.com:8080", text: $form.server), .server)
            label("Kullanıcı adı").padding(.top, 16)
            field(TextField("", text: $form.username), .user)
            label("Şifre").padding(.top, 16)
            field(SecureField("", text: $form.password), .pass)

            if let error = form.error {
                Text(error).font(.system(size: 14)).foregroundColor(MN.red).padding(.top, 14)
            }

            HStack(spacing: 14) {
                Button(action: connect) { Text("Bağlan").frame(minWidth: 110) }
                    .buttonStyle(PillButtonStyle(prominent: true))
                    .keyboardShortcut(.defaultAction)
                    .disabled(form.busy)
                if let onCancel = onCancel {
                    Button("Vazgeç", action: onCancel).buttonStyle(PillButtonStyle())
                        .keyboardShortcut(.cancelAction)
                }
                if form.busy { ProgressView().controlSize(.small) }
            }
            .padding(.top, 24)
        }
        .frame(width: 440)
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(MN.black)
        .onAppear {
            if let acc = model.store.account, form.server.isEmpty {
                form.server = acc.server
                form.username = acc.username
                form.password = acc.password
            }
            focus = form.server.isEmpty ? .server : nil
        }
        .onSubmit {
            // Enter connects as soon as every field is filled; otherwise jump to the first empty one.
            if form.server.trimmingCharacters(in: .whitespaces).isEmpty { focus = .server }
            else if form.username.trimmingCharacters(in: .whitespaces).isEmpty { focus = .user }
            else if form.password.isEmpty { focus = .pass }
            else { connect() }
        }
    }

    private func label(_ text: String) -> some View {
        Text(text).font(.system(size: 13)).foregroundColor(MN.text2).padding(.bottom, 6)
    }

    private func field<V: View>(_ content: V, _ f: Field) -> some View {
        content
            .textFieldStyle(.plain)
            .font(.system(size: 16))
            .foregroundColor(.white)
            .padding(.horizontal, 14)
            .frame(height: 44)
            .background(RoundedRectangle(cornerRadius: 9).fill(MN.card))
            .overlay(RoundedRectangle(cornerRadius: 9).stroke(focus == f ? MN.blue : MN.divider, lineWidth: 2))
            .focused($focus, equals: f)
    }

    func connect() {
        guard !form.busy else { return }
        form.busy = true
        form.error = nil
        Task { @MainActor in
            let error = await model.login(server: form.server, username: form.username, password: form.password)
            form.busy = false
            form.error = error
            if error == nil { onCancel?() }
        }
    }
}
