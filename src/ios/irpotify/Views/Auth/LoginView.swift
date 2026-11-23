import SwiftUI

public struct LoginView: View {
    @State private var serverUrl: String = ""
    @State private var email: String = ""
    @State private var password: String = ""
    @State private var isLoading: Bool = false
    @State private var errorMessage: String? = nil
    @State private var showQRScanner: Bool = false

    public init() {
        _serverUrl = State(initialValue: SessionManager.shared.serverUrl)
    }

    public var body: some View {
        ZStack {
            AppTheme.background.ignoresSafeArea()

            ScrollView {
                VStack(spacing: 28) {
                    Spacer(minLength: 40)

                    // Logo & App Name
                    VStack(spacing: 12) {
                        Image(systemName: "waveform.circle.fill")
                            .font(.system(size: 72))
                            .foregroundColor(AppTheme.primary)

                        Text("irpotify")
                            .font(.system(size: 32, weight: .bold))
                            .foregroundColor(AppTheme.textPrimary)

                        Text("Your Personal Music Cloud")
                            .font(.system(size: 14))
                            .foregroundColor(AppTheme.textSecondary)
                    }

                    // QR Code Fast Connect Button
                    Button(action: {
                        showQRScanner = true
                    }) {
                        HStack(spacing: 10) {
                            Image(systemName: "qrcode.viewfinder")
                                .font(.system(size: 20, weight: .semibold))
                            Text("Fast Pair with QR Code")
                                .font(.system(size: 16, weight: .bold))
                        }
                        .foregroundColor(Color.black)
                        .frame(maxWidth: .infinity)
                        .frame(height: 50)
                        .background(AppTheme.primary)
                        .cornerRadius(25)
                    }
                    .padding(.horizontal, 24)

                    HStack {
                        Rectangle().fill(AppTheme.divider).frame(height: 1)
                        Text("OR SIGN IN MANUALLY")
                            .font(.system(size: 11, weight: .bold))
                            .foregroundColor(AppTheme.textSecondary)
                        Rectangle().fill(AppTheme.divider).frame(height: 1)
                    }
                    .padding(.horizontal, 24)

                    // Manual Form
                    VStack(spacing: 16) {
                        // Server URL
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Server URL")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(AppTheme.textSecondary)

                            TextField("http://192.168.1.100:5080", text: $serverUrl)
                                .keyboardType(.URL)
                                .autocapitalization(.none)
                                .disableAutocorrection(true)
                                .padding()
                                .background(AppTheme.card)
                                .cornerRadius(8)
                                .foregroundColor(AppTheme.textPrimary)
                        }

                        // Email
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Email")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(AppTheme.textSecondary)

                            TextField("user@example.com", text: $email)
                                .keyboardType(.emailAddress)
                                .autocapitalization(.none)
                                .disableAutocorrection(true)
                                .padding()
                                .background(AppTheme.card)
                                .cornerRadius(8)
                                .foregroundColor(AppTheme.textPrimary)
                        }

                        // Password
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Password")
                                .font(.system(size: 12, weight: .semibold))
                                .foregroundColor(AppTheme.textSecondary)

                            SecureField("••••••••", text: $password)
                                .padding()
                                .background(AppTheme.card)
                                .cornerRadius(8)
                                .foregroundColor(AppTheme.textPrimary)
                        }

                        if let err = errorMessage {
                            Text(err)
                                .font(.system(size: 13))
                                .foregroundColor(Color.red)
                                .multilineTextAlignment(.center)
                                .padding(.horizontal)
                        }

                        // Login Button
                        Button(action: {
                            performLogin()
                        }) {
                            ZStack {
                                if isLoading {
                                    ProgressView()
                                        .progressViewStyle(CircularProgressViewStyle(tint: Color.black))
                                } else {
                                    Text("Log In")
                                        .font(.system(size: 16, weight: .bold))
                                        .foregroundColor(Color.black)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .frame(height: 50)
                            .background(AppTheme.primary)
                            .cornerRadius(25)
                        }
                        .disabled(isLoading || serverUrl.isEmpty || email.isEmpty || password.isEmpty)
                        .opacity((serverUrl.isEmpty || email.isEmpty || password.isEmpty) ? 0.6 : 1.0)
                    }
                    .padding(.horizontal, 24)

                    Spacer(minLength: 40)
                }
            }
        }
        .sheet(isPresented: $showQRScanner) {
            QRScannerView { scannedString in
                handleScannedQR(scannedString)
            }
        }
    }

    private func performLogin() {
        guard !serverUrl.isEmpty, !email.isEmpty, !password.isEmpty else { return }
        isLoading = true
        errorMessage = nil

        let cleanServer = serverUrl.trimmingCharacters(in: CharacterSet(charactersIn: "/"))

        Task {
            do {
                _ = try await NetworkService.shared.login(server: cleanServer, email: email, pass: password)
                await MainActor.run {
                    isLoading = false
                }
            } catch {
                await MainActor.run {
                    isLoading = false
                    errorMessage = error.localizedDescription
                }
            }
        }
    }

    private func handleScannedQR(_ qr: String) {
        guard let payload = SessionManager.parsePairingCode(qr) else {
            errorMessage = "Invalid QR code format."
            return
        }

        isLoading = true
        errorMessage = nil
        self.serverUrl = payload.serverUrl

        Task {
            do {
                _ = try await NetworkService.shared.claimPairing(server: payload.serverUrl, code: payload.code)
                await MainActor.run {
                    isLoading = false
                }
            } catch {
                await MainActor.run {
                    isLoading = false
                    errorMessage = "Pairing failed: \(error.localizedDescription)"
                }
            }
        }
    }
}
