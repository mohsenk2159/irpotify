using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MusicHub.Player.Models;

namespace MusicHub.Player.Services;

public sealed class ApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>
/// Typed HTTP client for the MusicHub API. Access tokens are refreshed transparently once
/// per request when the server answers 401; a failed refresh raises <see cref="SessionExpired"/>.
/// </summary>
public sealed class PlayerApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public string BaseUrl { get; private set; } = "http://127.0.0.1:5080";
    public string? AccessToken { get; private set; }
    public string? RefreshTokenValue { get; private set; }
    public DateTimeOffset AccessTokenExpiresAt { get; private set; }
    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }
    public UserProfile? CurrentUser { get; private set; }

    public event Action? SessionExpired;

    public void Configure(string serverUrl)
    {
        var raw = (serverUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Server address is required.");
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "http://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Enter a valid server address, for example http://192.168.1.20:5080.");
        }
        BaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public void ApplySession(SavedSession session)
    {
        BaseUrl = session.ServerUrl.TrimEnd('/');
        AccessToken = session.AccessToken;
        RefreshTokenValue = session.RefreshToken;
        AccessTokenExpiresAt = session.AccessTokenExpiresAt;
        RefreshTokenExpiresAt = session.RefreshTokenExpiresAt;
        CurrentUser = session.User;
    }

    public void ApplyAuth(AuthResult result)
    {
        AccessToken = result.AccessToken;
        RefreshTokenValue = result.RefreshToken;
        AccessTokenExpiresAt = result.AccessTokenExpiresAt;
        RefreshTokenExpiresAt = result.RefreshTokenExpiresAt;
        CurrentUser = result.User;
    }

    public SavedSession BuildSession(string? email) => new()
    {
        ServerUrl = BaseUrl,
        AccessToken = AccessToken ?? string.Empty,
        AccessTokenExpiresAt = AccessTokenExpiresAt,
        RefreshToken = RefreshTokenValue ?? string.Empty,
        RefreshTokenExpiresAt = RefreshTokenExpiresAt,
        User = CurrentUser,
        Email = email ?? CurrentUser?.Email
    };

    public void ClearAuth()
    {
        AccessToken = null;
        RefreshTokenValue = null;
        CurrentUser = null;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string deviceName, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/auth/login"))
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                deviceName,
                clientType = "player"
            }, options: JsonOptions)
        };
        var result = await SendAsync<AuthResult>(request, allowRefresh: false, ct);
        ApplyAuth(result);
        return result;
    }

    public async Task<UserProfile> GetMeAsync(CancellationToken ct = default)
    {
        var profile = await GetAsync<UserProfile>("/api/auth/me", ct);
        CurrentUser = profile;
        return profile;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(RefreshTokenValue))
        {
            ClearAuth();
            return;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/auth/logout"))
            {
                Content = JsonContent.Create(new { refreshToken = RefreshTokenValue }, options: JsonOptions)
            };
            using var response = await _http.SendAsync(request, ct);
        }
        catch
        {
            // Logout is best effort; local state is cleared regardless.
        }
        finally
        {
            ClearAuth();
        }
    }

    public Task<PagedResult<TrackItem>> GetTracksAsync(string? query = null, bool? favorites = null, string? sort = null, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(query)) qs.Add("q=" + Uri.EscapeDataString(query.Trim()));
        if (favorites is not null) qs.Add("favorites=" + favorites.Value.ToString().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(sort)) qs.Add("sort=" + Uri.EscapeDataString(sort));
        return GetAsync<PagedResult<TrackItem>>("/api/tracks?" + string.Join("&", qs), ct);
    }

    public Task<PagedResult<AlbumItem>> GetAlbumsAsync(string? query = null, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(query)) qs.Add("q=" + Uri.EscapeDataString(query.Trim()));
        return GetAsync<PagedResult<AlbumItem>>("/api/albums?" + string.Join("&", qs), ct);
    }

    public Task<AlbumDetail> GetAlbumAsync(Guid id, CancellationToken ct = default)
        => GetAsync<AlbumDetail>($"/api/albums/{id}", ct);

    public Task<PagedResult<ArtistItem>> GetArtistsAsync(string? query = null, int page = 1, int pageSize = 200, CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(query)) qs.Add("q=" + Uri.EscapeDataString(query.Trim()));
        return GetAsync<PagedResult<ArtistItem>>("/api/artists?" + string.Join("&", qs), ct);
    }

    public Task<ArtistDetail> GetArtistAsync(Guid id, CancellationToken ct = default)
        => GetAsync<ArtistDetail>($"/api/artists/{id}", ct);

    public Task<List<PlaylistItem>> GetPlaylistsAsync(CancellationToken ct = default)
        => GetAsync<List<PlaylistItem>>("/api/playlists", ct);

    public Task<PlaylistDetail> GetPlaylistAsync(Guid id, CancellationToken ct = default)
        => GetAsync<PlaylistDetail>($"/api/playlists/{id}", ct);

    public async Task<PlaylistItem> CreatePlaylistAsync(string name, string? description, bool isPublic, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/playlists"))
        {
            Content = JsonContent.Create(new { name, description, isPublic }, options: JsonOptions)
        };
        return await SendAsync<PlaylistItem>(request, true, ct);
    }

    public async Task DeletePlaylistAsync(Guid id, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url($"/api/playlists/{id}"));
        await SendNoContentAsync(request, ct);
    }

    public async Task AddTrackToPlaylistAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url($"/api/playlists/{playlistId}/tracks"))
        {
            Content = JsonContent.Create(new { trackId }, options: JsonOptions)
        };
        await SendNoContentAsync(request, ct);
    }

    public async Task RemoveTrackFromPlaylistAsync(Guid playlistId, Guid trackId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url($"/api/playlists/{playlistId}/tracks/{trackId}"));
        await SendNoContentAsync(request, ct);
    }

    public async Task<bool> ToggleFavoriteAsync(Guid trackId, bool favorite, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(favorite ? HttpMethod.Post : HttpMethod.Delete, Url($"/api/favorites/{trackId}"));
        await SendNoContentAsync(request, ct);
        return favorite;
    }

    public Task<HomeResult> GetHomeAsync(CancellationToken ct = default)
        => GetAsync<HomeResult>("/api/home", ct);

    public Task<SearchResult> SearchAsync(string query, CancellationToken ct = default)
        => GetAsync<SearchResult>("/api/search?q=" + Uri.EscapeDataString(query.Trim()), ct);

    public Task<List<HistoryEntry>> GetHistoryAsync(int limit = 50, CancellationToken ct = default)
        => GetAsync<List<HistoryEntry>>($"/api/history?limit={limit}", ct);

    public async Task RecordPlaybackAsync(Guid trackId, double positionSeconds, bool completed, double listenedSeconds = 0, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/history/playback"))
        {
            Content = JsonContent.Create(new { trackId, positionSeconds, completed, listenedSeconds }, options: JsonOptions)
        };
        await SendNoContentAsync(request, ct);
    }

    // ---- Admin: library storage location ------------------------------------------------

    public Task<StorageLocationInfo> GetStorageLocationAsync(CancellationToken ct = default)
        => GetAsync<StorageLocationInfo>("/api/admin/storage/location", ct);

    public async Task<StorageValidationResult> ValidateStoragePathAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/admin/storage/validate"))
        {
            Content = JsonContent.Create(new { path }, options: JsonOptions)
        };
        return await SendAsync<StorageValidationResult>(request, true, ct);
    }

    public async Task<StorageRelocationStart> StartStorageRelocationAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/admin/storage/relocate"))
        {
            Content = JsonContent.Create(new { path, confirmation = "relocate" }, options: JsonOptions)
        };
        return await SendAsync<StorageRelocationStart>(request, true, ct);
    }

    public Task<StorageRelocationInfo> GetStorageRelocationAsync(CancellationToken ct = default)
        => GetAsync<StorageRelocationInfo>("/api/admin/storage/relocation", ct);

    public async Task<string> GetStreamUrlAsync(Guid trackId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url($"/api/tracks/{trackId}/stream-token"));
        var token = await SendAsync<StreamTokenResult>(request, true, ct);
        // Build against our configured base URL: the server's absolute URL may reflect an
        // internal proxy host that the desktop client cannot reach.
        return Url(token.StreamUrl);
    }

    public async Task<byte[]?> GetArtworkAsync(string? relativeUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url(relativeUrl));
            if (!string.IsNullOrWhiteSpace(AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            }
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    private string Url(string path)
    {
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        return BaseUrl + (path.StartsWith('/') ? path : "/" + path);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        return await SendAsync<T>(request, true, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, bool allowRefresh, CancellationToken ct)
    {
        var response = await SendRawAsync(request, allowRefresh, ct);
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ApiException("The server returned an empty response.", (int)response.StatusCode);
            }
            try
            {
                var value = JsonSerializer.Deserialize<T>(text, JsonOptions);
                if (value is null) throw new ApiException("The server returned an unexpected response.", (int)response.StatusCode);
                return value;
            }
            catch (JsonException)
            {
                throw new ApiException("The server returned a response that this player cannot read. Check the server version.", (int)response.StatusCode);
            }
        }
    }

    private async Task SendNoContentAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendRawAsync(request, true, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw BuildError(response.StatusCode, text);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, bool allowRefresh, CancellationToken ct)
    {
        // Buffer JSON bodies once so a 401 retry can resend identical bytes.
        if (request.Content is not null and not ByteArrayContent)
        {
            var body = await request.Content.ReadAsByteArrayAsync(ct);
            var buffered = new ByteArrayContent(body);
            foreach (var header in request.Content.Headers)
            {
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = buffered;
        }

        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        }

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                response.Dispose();
                throw BuildError(response.StatusCode, text);
            }
            return response;
        }

        // A 401 on a request that must not refresh (for example the login call itself)
        // carries the server's real explanation, so surface that instead of "session expired".
        if (!allowRefresh || string.IsNullOrWhiteSpace(RefreshTokenValue))
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw BuildError(HttpStatusCode.Unauthorized, text);
        }

        response.Dispose();
        await RefreshTokenAsync(ct);

        using var retry = CloneRequest(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        var second = await _http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!second.IsSuccessStatusCode)
        {
            var text = await second.Content.ReadAsStringAsync(ct);
            var status = second.StatusCode;
            second.Dispose();
            throw BuildError(status, text);
        }
        return second;
    }

    private async Task RefreshTokenAsync(CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(RefreshTokenValue)) throw new ApiException("Your session has expired. Sign in again.", 401);
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/auth/refresh"))
            {
                Content = JsonContent.Create(new { refreshToken = RefreshTokenValue }, options: JsonOptions)
            };
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                ClearAuth();
                SessionExpired?.Invoke();
                throw new ApiException("Your session has expired. Sign in again.", 401);
            }
            var result = JsonSerializer.Deserialize<AuthResult>(text, JsonOptions) ?? throw new ApiException("Refresh failed.", 401);
            ApplyAuth(result);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        if (source.Content is not null)
        {
            var body = source.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(body);
            foreach (var header in source.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private static ApiException BuildError(HttpStatusCode status, string text)
    {
        string? message = null;
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(text, JsonOptions);
            message = error?.Error ?? error?.Detail ?? error?.Title;
        }
        catch
        {
            // Not JSON.
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            message = status switch
            {
                HttpStatusCode.Forbidden => "This account is not allowed to do that.",
                HttpStatusCode.Unauthorized => "Invalid email or password.",
                HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
                HttpStatusCode.ServiceUnavailable => "The server is unavailable or starting up.",
                _ => $"The server returned HTTP {(int)status}."
            };
        }
        return new ApiException(message, (int)status);
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshGate.Dispose();
    }
}
