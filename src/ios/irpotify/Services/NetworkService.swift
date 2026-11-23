import Foundation

public enum NetworkError: LocalizedError {
    case invalidServerUrl
    case unauthorized
    case serverError(String)
    case decodingError(String)
    case networkFailure(String)

    public var errorDescription: String? {
        switch self {
        case .invalidServerUrl: return "Invalid server address."
        case .unauthorized: return "Session expired. Please log in again."
        case .serverError(let msg): return msg
        case .decodingError(let msg): return "Data error: \(msg)"
        case .networkFailure(let msg): return "Network error: \(msg)"
        }
    }
}

public final class NetworkService {
    public static let shared = NetworkService()

    private let session: URLSession

    private init() {
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 300
        self.session = URLSession(configuration: config)
    }

    private var baseUrl: String {
        SessionManager.shared.serverUrl.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
    }

    // MARK: - Core Request Dispatcher

    private func request<T: Decodable>(
        endpoint: String,
        method: String = "GET",
        queryItems: [URLQueryItem]? = nil,
        body: Data? = nil,
        customBaseUrl: String? = nil,
        requiresAuth: Bool = true
    ) async throws -> T {
        let root = customBaseUrl ?? baseUrl
        guard let base = URL(string: root) else {
            throw NetworkError.invalidServerUrl
        }

        var components = URLComponents(url: base.appendingPathComponent(endpoint), resolvingAgainstBaseURL: false)
        if let queryItems = queryItems, !queryItems.isEmpty {
            components?.queryItems = queryItems
        }

        guard let requestUrl = components?.url else {
            throw NetworkError.invalidServerUrl
        }

        var req = URLRequest(url: requestUrl)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Accept")

        if let body = body {
            req.httpBody = body
            req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }

        if requiresAuth, let token = SessionManager.shared.accessToken {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }

        let (data, response): (Data, URLResponse)
        do {
            (data, response) = try await session.data(for: req)
        } catch {
            throw NetworkError.networkFailure(error.localizedDescription)
        }

        guard let httpResponse = response as? HTTPURLResponse else {
            throw NetworkError.networkFailure("Invalid response.")
        }

        // Handle Token Refresh on 401
        if httpResponse.statusCode == 401 && requiresAuth {
            if let refreshed = try? await refreshToken() {
                SessionManager.shared.updateAccessToken(refreshed)
                // Retry request with new token
                var retryReq = req
                retryReq.setValue("Bearer \(refreshed)", forHTTPHeaderField: "Authorization")
                let (retryData, retryResponse) = try await session.data(for: retryReq)
                if let retryHttp = retryResponse as? HTTPURLResponse, retryHttp.statusCode >= 200 && retryHttp.statusCode < 300 {
                    return try JSONDecoder().decode(T.self, from: retryData)
                }
            }
            SessionManager.shared.logout()
            throw NetworkError.unauthorized
        }

        guard httpResponse.statusCode >= 200 && httpResponse.statusCode < 300 else {
            let errorMsg = String(data: data, encoding: .utf8) ?? "HTTP \(httpResponse.statusCode)"
            throw NetworkError.serverError(errorMsg)
        }

        do {
            let decoder = JSONDecoder()
            return try decoder.decode(T.self, from: data)
        } catch {
            throw NetworkError.decodingError(error.localizedDescription)
        }
    }

    private func refreshToken() async throws -> String {
        guard let refresh = SessionManager.shared.refreshToken else {
            throw NetworkError.unauthorized
        }
        let body = try JSONEncoder().encode(RefreshRequest(refreshToken: refresh))
        let res: TokenResponse = try await request(
            endpoint: "api/auth/refresh",
            method: "POST",
            body: body,
            requiresAuth: false
        )
        return res.accessToken
    }

    // MARK: - Auth API

    public func login(server: String, email: String, pass: String) async throws -> TokenResponse {
        let body = try JSONEncoder().encode(LoginRequest(email: email, password: pass))
        let res: TokenResponse = try await request(
            endpoint: "api/auth/login",
            method: "POST",
            body: body,
            customBaseUrl: server,
            requiresAuth: false
        )
        SessionManager.shared.setSession(tokenResponse: res, server: server)
        return res
    }

    public func claimPairing(server: String, code: String) async throws -> TokenResponse {
        let body = try JSONEncoder().encode(PairingClaimRequest(code: code))
        let res: TokenResponse = try await request(
            endpoint: "api/pairing/claim",
            method: "POST",
            body: body,
            customBaseUrl: server,
            requiresAuth: false
        )
        SessionManager.shared.setSession(tokenResponse: res, server: server)
        return res
    }

    // MARK: - Library & Catalog API

    public func fetchHome() async throws -> HomeResponse {
        return try await request(endpoint: "api/home")
    }

    public func fetchTracks(
        query: String? = nil,
        page: Int = 1,
        pageSize: Int = 50,
        sort: String? = "title_asc",
        folderId: String? = nil,
        albumId: String? = nil,
        artistId: String? = nil,
        favorites: Bool? = nil
    ) async throws -> PagedResult<TrackDto> {
        var queryItems: [URLQueryItem] = [
            URLQueryItem(name: "page", value: "\(page)"),
            URLQueryItem(name: "pageSize", value: "\(pageSize)")
        ]
        if let query = query, !query.isEmpty { queryItems.append(URLQueryItem(name: "q", value: query)) }
        if let sort = sort { queryItems.append(URLQueryItem(name: "sort", value: sort)) }
        if let folderId = folderId { queryItems.append(URLQueryItem(name: "folderId", value: folderId)) }
        if let albumId = albumId { queryItems.append(URLQueryItem(name: "albumId", value: albumId)) }
        if let artistId = artistId { queryItems.append(URLQueryItem(name: "artistId", value: artistId)) }
        if let favorites = favorites { queryItems.append(URLQueryItem(name: "favorites", value: "\(favorites)")) }

        return try await request(endpoint: "api/tracks", queryItems: queryItems)
    }

    public func fetchAlbums(query: String? = nil, page: Int = 1, pageSize: Int = 100) async throws -> PagedResult<AlbumDto> {
        var queryItems: [URLQueryItem] = [
            URLQueryItem(name: "page", value: "\(page)"),
            URLQueryItem(name: "pageSize", value: "\(pageSize)"),
            URLQueryItem(name: "sort", value: "title_asc")
        ]
        if let query = query, !query.isEmpty { queryItems.append(URLQueryItem(name: "q", value: query)) }
        return try await request(endpoint: "api/albums", queryItems: queryItems)
    }

    public func fetchAlbumDetail(albumId: String) async throws -> AlbumDetailResponse {
        return try await request(endpoint: "api/albums/\(albumId)")
    }

    public func fetchArtists(query: String? = nil, page: Int = 1, pageSize: Int = 100) async throws -> PagedResult<ArtistDto> {
        var queryItems: [URLQueryItem] = [
            URLQueryItem(name: "page", value: "\(page)"),
            URLQueryItem(name: "pageSize", value: "\(pageSize)"),
            URLQueryItem(name: "sort", value: "name_asc")
        ]
        if let query = query, !query.isEmpty { queryItems.append(URLQueryItem(name: "q", value: query)) }
        return try await request(endpoint: "api/artists", queryItems: queryItems)
    }

    public func fetchArtistDetail(artistId: String) async throws -> ArtistDetailResponse {
        return try await request(endpoint: "api/artists/\(artistId)")
    }

    public func fetchFolders() async throws -> [FolderNodeDto] {
        return try await request(endpoint: "api/folders")
    }

    public func fetchPlaylists() async throws -> [PlaylistDto] {
        return try await request(endpoint: "api/playlists")
    }

    public func fetchPlaylistDetail(playlistId: String) async throws -> PlaylistDetailResponse {
        return try await request(endpoint: "api/playlists/\(playlistId)")
    }

    public func toggleFavorite(trackId: String, isFavorite: Bool) async throws {
        let method = isFavorite ? "POST" : "DELETE"
        struct EmptyResponse: Decodable {}
        let _: EmptyResponse? = try? await request(endpoint: "api/favorites/\(trackId)", method: method)
    }

    public func search(query: String) async throws -> SearchResponse {
        let queryItems = [URLQueryItem(name: "q", value: query)]
        return try await request(endpoint: "api/search", queryItems: queryItems)
    }

    public func getAudioStreamUrl(trackId: String) -> URL? {
        let token = SessionManager.shared.accessToken ?? ""
        let root = baseUrl
        return URL(string: "\(root)/api/tracks/\(trackId)/stream?access_token=\(token)")
    }

    public func getArtworkUrl(path: String?) -> URL? {
        guard let path = path, !path.isEmpty else { return nil }
        if path.hasPrefix("http://") || path.hasPrefix("https://") {
            return URL(string: path)
        }
        let root = baseUrl
        let cleanPath = path.hasPrefix("/") ? String(path.dropFirst()) : path
        return URL(string: "\(root)/\(cleanPath)")
    }

    public func reportPlayback(trackId: String, position: Double, completed: Bool) async {
        let body = try? JSONEncoder().encode(PlaybackReportRequest(
            trackId: trackId,
            positionSeconds: position,
            completed: completed,
            deviceId: "iOS Device"
        ))
        struct Empty: Decodable {}
        let _: Empty? = try? await request(endpoint: "api/history/playback", method: "POST", body: body)
    }
}
