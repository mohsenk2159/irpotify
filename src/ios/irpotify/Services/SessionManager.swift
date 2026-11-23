import Foundation
import Combine
import Security

public final class SessionManager: ObservableObject {
    public static let shared = SessionManager()

    @Published public var serverUrl: String {
        didSet { UserDefaults.standard.set(serverUrl, forKey: "irpotify_server_url") }
    }
    @Published public var isAuthenticated: Bool = false
    @Published public var currentUser: UserDto? = nil

    private let accessTokenKey = "irpotify_access_token"
    private let refreshTokenKey = "irpotify_refresh_token"
    private let userKey = "irpotify_current_user"

    private init() {
        self.serverUrl = UserDefaults.standard.string(forKey: "irpotify_server_url") ?? ""
        loadSession()
    }

    public var accessToken: String? {
        get { readKeychain(key: accessTokenKey) }
        set {
            if let val = newValue {
                saveKeychain(key: accessTokenKey, value: val)
            } else {
                deleteKeychain(key: accessTokenKey)
            }
        }
    }

    public var refreshToken: String? {
        get { readKeychain(key: refreshTokenKey) }
        set {
            if let val = newValue {
                saveKeychain(key: refreshTokenKey, value: val)
            } else {
                deleteKeychain(key: refreshTokenKey)
            }
        }
    }

    public func setSession(tokenResponse: TokenResponse, server: String) {
        self.serverUrl = server.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        self.accessToken = tokenResponse.accessToken
        self.refreshToken = tokenResponse.refreshToken
        self.currentUser = tokenResponse.user

        if let userData = try? JSONEncoder().encode(tokenResponse.user) {
            UserDefaults.standard.set(userData, forKey: userKey)
        }

        DispatchQueue.main.async {
            self.isAuthenticated = true
        }
    }

    public func updateAccessToken(_ newAccessToken: String) {
        self.accessToken = newAccessToken
    }

    public func logout() {
        self.accessToken = nil
        self.refreshToken = nil
        self.currentUser = nil
        UserDefaults.standard.removeObject(forKey: userKey)
        DispatchQueue.main.async {
            self.isAuthenticated = false
        }
    }

    private func loadSession() {
        if let token = accessToken, !token.isEmpty {
            if let data = UserDefaults.standard.data(forKey: userKey),
               let user = try? JSONDecoder().decode(UserDto.self, from: data) {
                self.currentUser = user
            }
            self.isAuthenticated = true
        } else {
            self.isAuthenticated = false
        }
    }

    // MARK: - Keychain Helpers

    private func saveKeychain(key: String, value: String) {
        let data = Data(value.utf8)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        SecItemDelete(query as CFDictionary)
        SecItemAdd(query as CFDictionary, nil)
    }

    private func readKeychain(key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var item: CFTypeRef?
        if SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess,
           let data = item as? Data,
           let string = String(data: data, encoding: .utf8) {
            return string
        }
        return nil
    }

    private func deleteKeychain(key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: key
        ]
        SecItemDelete(query as CFDictionary)
    }

    // MARK: - QR Code Pairing Parser

    public struct PairingPayload {
        public let serverUrl: String
        public let code: String
        public let lanServerUrl: String?
    }

    public static func parsePairingCode(_ qrString: String) -> PairingPayload? {
        guard let url = URL(string: qrString.trimmingCharacters(in: .whitespacesAndNewlines)),
              let components = URLComponents(url: url, resolvingAgainstBaseURL: false) else {
            return nil
        }

        let queryItems = components.queryItems ?? []
        var server: String? = nil
        var code: String? = nil
        var lan: String? = nil

        for item in queryItems {
            switch item.name.lowercased() {
            case "server": server = item.value
            case "code": code = item.value
            case "lan": lan = item.value
            default: break
            }
        }

        guard let finalCode = code, !finalCode.isEmpty else { return nil }
        let chosenServer = lan ?? server ?? (components.host != nil ? "http://\(components.host!):5080" : nil)
        guard let finalServer = chosenServer else { return nil }

        return PairingPayload(serverUrl: finalServer, code: finalCode, lanServerUrl: lan)
    }
}
