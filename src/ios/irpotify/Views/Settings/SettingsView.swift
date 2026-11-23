import SwiftUI

public struct SettingsView: View {
    @ObservedObject var session = SessionManager.shared
    @State private var showLogoutAlert: Bool = false

    public var body: some View {
        NavigationView {
            ZStack {
                AppTheme.background.ignoresSafeArea()

                List {
                    // Profile Section
                    Section(header: Text("Account").foregroundColor(AppTheme.textSecondary)) {
                        HStack(spacing: 14) {
                            ZStack {
                                Circle().fill(AppTheme.primary)
                                    .frame(width: 48, height: 48)
                                Text(session.currentUser?.displayName.prefix(1).uppercased() ?? "U")
                                    .font(.system(size: 20, weight: .bold))
                                    .foregroundColor(Color.black)
                            }

                            VStack(alignment: .leading, spacing: 4) {
                                Text(session.currentUser?.displayName ?? "User")
                                    .font(.system(size: 16, weight: .bold))
                                    .foregroundColor(AppTheme.textPrimary)

                                Text(session.currentUser?.email ?? "")
                                    .font(.system(size: 13))
                                    .foregroundColor(AppTheme.textSecondary)

                                if let role = session.currentUser?.role {
                                    Text(role)
                                        .font(.system(size: 11, weight: .semibold))
                                        .foregroundColor(AppTheme.primary)
                                }
                            }
                        }
                        .padding(.vertical, 4)
                        .listRowBackground(AppTheme.card)
                    }

                    // Server Section
                    Section(header: Text("Connected Server").foregroundColor(AppTheme.textSecondary)) {
                        HStack {
                            Image(systemName: "server.rack")
                                .foregroundColor(AppTheme.primary)
                            Text(session.serverUrl)
                                .font(.system(size: 14))
                                .foregroundColor(AppTheme.textPrimary)
                        }
                        .listRowBackground(AppTheme.card)
                    }

                    // About App Section
                    Section(header: Text("About").foregroundColor(AppTheme.textSecondary)) {
                        HStack {
                            Text("irpotify for iOS")
                                .foregroundColor(AppTheme.textPrimary)
                            Spacer()
                            Text("v1.0.0")
                                .foregroundColor(AppTheme.textSecondary)
                        }
                        .listRowBackground(AppTheme.card)

                        HStack {
                            Text("Audio Engine")
                                .foregroundColor(AppTheme.textPrimary)
                            Spacer()
                            Text("AVPlayer / CoreAudio")
                                .foregroundColor(AppTheme.textSecondary)
                        }
                        .listRowBackground(AppTheme.card)
                    }

                    // Logout Section
                    Section {
                        Button(action: {
                            showLogoutAlert = true
                        }) {
                            HStack {
                                Spacer()
                                Text("Log Out")
                                    .font(.system(size: 16, weight: .semibold))
                                    .foregroundColor(Color.red)
                                Spacer()
                            }
                        }
                        .listRowBackground(AppTheme.card)
                    }
                }
                .listStyle(InsetGroupedListStyle())
                .scrollContentBackground(.hidden)
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .alert(isPresented: $showLogoutAlert) {
                Alert(
                    title: Text("Log Out"),
                    message: Text("Are you sure you want to log out of irpotify?"),
                    primaryButton: .destructive(Text("Log Out")) {
                        AudioPlayerService.shared.togglePlayPause()
                        session.logout()
                    },
                    secondaryButton: .cancel()
                )
            }
        }
    }
}
