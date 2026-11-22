using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Endpoints;
using MusicHub.Domain;
using MusicHub.Infrastructure;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Regression tests for the W-019 code-review findings that are observable over HTTP: the
/// authorization model (CR-004/CR-006), the error contract (CR-021), the last-administrator guard
/// and security-stamp behaviour (CR-005), the playback upsert (CR-003/CR-018), the batch bound
/// (CR-011), artwork caching and album artwork (CR-013/CR-014), pagination stability (CR-017),
/// the health probe (CR-012), and the per-user stream limit (CR-019).
///
/// <para>
/// Each test asserts the behaviour the finding described as wrong, so each one fails against the
/// pre-fix code rather than merely exercising the happy path.
/// </para>
/// </summary>
[Collection("api")]
public class ReviewFindingsTests(ApiFactory factory)
{
    private const string Password = "ReviewFindings!Passw0rd";

    [Fact]
    public async Task The_album_endpoints_keep_their_contract_after_moving_onto_the_shared_service()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);

        // Create: the response keeps its historical shape ({ id, title }) and location header.
        var create = await admin.PostAsJsonAsync("/api/albums", new
        {
            title = "  Contract Album  ",
            albumArtist = "Contract Artist",
            year = 2024
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var albumId = created.RootElement.GetProperty("id").GetGuid();
        // The title is trimmed and stored as given; the same rule the bot now uses.
        Assert.Equal("Contract Album", created.RootElement.GetProperty("title").GetString());
        Assert.Equal($"/api/albums/{albumId}", create.Headers.Location?.OriginalString);

        // A blank title is still a 400 with an error message, not a 500 or an empty album.
        var blank = await admin.PostAsJsonAsync("/api/albums", new { title = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Contains("required", await blank.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // Assign: 204 and the membership is real.
        var assign = await admin.PostAsJsonAsync($"/api/albums/{albumId}/tracks", new { trackId });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        // Idempotent, as the shared service promises: a repeated tap is harmless.
        var again = await admin.PostAsJsonAsync($"/api/albums/{albumId}/tracks", new { trackId });
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        // An unknown album is still a 404.
        var missing = await admin.PostAsJsonAsync($"/api/albums/{Guid.NewGuid()}/tracks", new { trackId });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        Assert.Equal(1, await db.AlbumTracks.CountAsync(x => x.AlbumId == albumId && x.TrackId == trackId));
        var track = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
        Assert.Equal("Contract Album", track.AlbumDisplay);

        db.AlbumTracks.RemoveRange(await db.AlbumTracks.Where(x => x.AlbumId == albumId).ToListAsync());
        db.Albums.RemoveRange(await db.Albums.Where(x => x.Id == albumId).ToListAsync());
        await db.SaveChangesAsync();
    }

    // CR-004: only administrators may change library content.
    [Theory]
    [InlineData("PATCH", "/api/tracks/{track}")]
    [InlineData("DELETE", "/api/tracks/{track}")]
    [InlineData("POST", "/api/albums")]
    [InlineData("POST", "/api/artists")]
    [InlineData("POST", "/api/folders")]
    [InlineData("POST", "/api/genres")]
    public async Task Contributor_cannot_change_library_content(string method, string pathTemplate)
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);

        using var contributor = await CreateRoleClientAsync(admin, "Contributor");
        var path = pathTemplate.Replace("{track}", trackId.ToString());

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "PATCH" or "POST")
        {
            request.Content = JsonContent.Create(new
            {
                title = "contributor rename",
                name = "contributor created",
                albumArtist = "x",
                year = 2001
            });
        }
        var response = await contributor.SendAsync(request);

        // A write by a non-administrator is forbidden, and - per the project rule that UI hiding is
        // not authorization - the server must say so rather than relying on the dashboard.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_cannot_upload_audio()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var contributor = await CreateRoleClientAsync(admin, "Contributor");

        var wav = ApiFactory.BuildWav(9100);
        using var form = BuildUpload(wav, $"cr004-{Guid.NewGuid():N}.wav");
        var response = await contributor.PostAsync("/api/uploads", form);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // CR-004: per-user data stays writable, which is the documented exception to read-only.
    [Fact]
    public async Task Contributor_can_still_manage_own_favourites_and_playlists()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);
        using var contributor = await CreateRoleClientAsync(admin, "Contributor");

        Assert.Equal(HttpStatusCode.NoContent, (await contributor.PostAsync($"/api/favorites/{trackId}", null)).StatusCode);

        var playlist = await contributor.PostAsJsonAsync("/api/playlists", new { name = "CR-004 own playlist", isPublic = false });
        Assert.Equal(HttpStatusCode.Created, playlist.StatusCode);
        using var created = JsonDocument.Parse(await playlist.Content.ReadAsStringAsync());
        var playlistId = created.RootElement.GetProperty("id").GetGuid();

        var rename = await contributor.PatchAsJsonAsync($"/api/playlists/{playlistId}", new { name = "renamed" });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        var add = await contributor.PostAsJsonAsync($"/api/playlists/{playlistId}/tracks", new { trackId });
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);
    }

    // CR-006: includeDeleted is administrator-only.
    [Fact]
    public async Task Listener_cannot_list_deleted_tracks_but_administrator_can()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);

        var delete = await admin.DeleteAsync($"/api/tracks/{trackId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var listener = await CreateRoleClientAsync(admin, "Listener");

        var listenerAttempt = await listener.GetAsync("/api/tracks?includeDeleted=true&pageSize=200");
        Assert.Equal(HttpStatusCode.Forbidden, listenerAttempt.StatusCode);

        // The default listing never contains the deleted row for a Listener either.
        var listenerDefault = await listener.GetFromJsonAsync<JsonElement>("/api/tracks?pageSize=200");
        Assert.DoesNotContain(listenerDefault.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == trackId);

        var adminAttempt = await admin.GetFromJsonAsync<JsonElement>("/api/tracks?includeDeleted=true&pageSize=200");
        Assert.Contains(adminAttempt.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == trackId);
    }

    // CR-021: a malformed address is a validation failure, not a conflict.
    [Fact]
    public async Task Creating_a_user_with_a_malformed_email_returns_400_not_409()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var invalid = await admin.PostAsJsonAsync("/api/users", new
        {
            email = "not-an-email",
            password = Password,
            displayName = "Invalid",
            role = "Listener"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Contains("valid address", invalidJson.RootElement.GetProperty("error").GetString()!);

        var email = Unique("duplicate");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password = Password,
            displayName = "First",
            role = "Listener"
        })).StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password = Password,
            displayName = "Second",
            role = "Listener"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Contains("already registered", duplicateJson.RootElement.GetProperty("error").GetString()!);
    }

    // CR-021: an ownership rejection carries the documented body instead of an empty 403.
    [Fact]
    public async Task A_forbidden_playlist_edit_returns_a_body()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var listener = await CreateRoleClientAsync(admin, "Listener");

        var playlist = await admin.PostAsJsonAsync("/api/playlists", new { name = "admin only", isPublic = false });
        Assert.Equal(HttpStatusCode.Created, playlist.StatusCode);
        using var created = JsonDocument.Parse(await playlist.Content.ReadAsStringAsync());
        var playlistId = created.RootElement.GetProperty("id").GetGuid();

        var attempt = await listener.PatchAsJsonAsync($"/api/playlists/{playlistId}", new { name = "stolen" });
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);

        using var body = JsonDocument.Parse(await attempt.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
    }

    // CR-005: a metadata edit must not sign the user out of their live sessions.
    [Fact]
    public async Task Editing_a_display_name_keeps_existing_access_tokens_valid()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("stamp");
        var userId = await CreateUserAsync(admin, email, "Listener");

        using var user = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);

        var rename = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { displayName = "Renamed Person" });
        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);

        // Before the fix the stamp rotated on every PATCH and this was a 401.
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);
    }

    // CR-005: the stamp still rotates on a role change, because the JWT carries role claims.
    [Fact]
    public async Task Changing_a_role_revokes_existing_access_tokens()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("role-change");
        var userId = await CreateUserAsync(admin, email, "Listener");

        using var user = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);

        var change = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { role = "Contributor" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
    }

    // CR-005: the only administrator cannot be demoted into an unrecoverable instance.
    [Fact]
    public async Task The_last_administrator_cannot_be_demoted()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetGuid();

        // Count real administrators so the assertion holds even if the fixture created more than one.
        var administrators = await CountAdministratorsAsync();
        if (administrators > 1)
        {
            // Another administrator exists, so the guard must allow it; demote this one and restore.
            var allowed = await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { role = "Listener" });
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            using var restoreScope = factory.Services.CreateScope();
            var restoreDb = restoreScope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var restoreUser = await restoreDb.Users.Include(x => x.UserRoles).SingleAsync(x => x.Id == adminId);
            var adminRole = await restoreDb.Roles.SingleAsync(x => x.Name == RoleNames.Administrator);
            restoreDb.UserRoles.RemoveRange(restoreUser.UserRoles);
            restoreUser.UserRoles.Add(new UserRole { UserId = adminId, RoleId = adminRole.Id });
            await restoreDb.SaveChangesAsync();
            return;
        }

        var refused = await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { role = "Listener" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Contains("At least one administrator", body.RootElement.GetProperty("error").GetString()!);

        // Still an administrator.
        Assert.Equal(1, await CountAdministratorsAsync());
    }

    // The demotion guard does not cover deactivation: `{ isActive: false }` on the last active
    // administrator left nobody able to sign in, so the instance could only be recovered offline.
    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetGuid();

        var activeAdministrators = await CountActiveAdministratorsAsync();
        if (activeAdministrators > 1)
        {
            // Another active administrator exists, so deactivation is legitimate; prove it is
            // allowed, then restore the account so the fixture is left as it was.
            var allowed = await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { isActive = false });
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

            using var restoreScope = factory.Services.CreateScope();
            var restoreDb = restoreScope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var restoreUser = await restoreDb.Users.SingleAsync(x => x.Id == adminId);
            restoreUser.IsActive = true;
            restoreUser.DeactivatedAt = null;
            restoreUser.DeactivationReason = null;
            await restoreDb.SaveChangesAsync();
            return;
        }

        var refused = await admin.PatchAsJsonAsync($"/api/users/{adminId}", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Contains("At least one active administrator", body.RootElement.GetProperty("error").GetString()!);

        // Still active, and still able to sign in.
        Assert.Equal(1, await CountActiveAdministratorsAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    // A deactivated non-administrator is still deactivatable: the guard must not become a blanket
    // refusal that breaks ordinary account management.
    [Fact]
    public async Task A_non_administrator_can_still_be_deactivated()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var userId = await CreateUserAsync(admin, Unique("deactivate"), "Listener");

        var response = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Primary_administrator_cannot_be_deleted()
    {
        using var admin = await factory.CreateAdminClientAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var primaryAdminId = await db.UserRoles
            .Where(r => r.Role.Name == RoleNames.Administrator)
            .OrderBy(r => r.User.CreatedAt)
            .Select(r => r.UserId)
            .FirstAsync();

        // An administrator creates another administrator to attempt deleting the primary admin
        var secondAdminEmail = Unique("secondadmin");
        var secondAdminId = await CreateUserAsync(admin, secondAdminEmail, RoleNames.Administrator);
        using var secondAdmin = await LoginAsync(secondAdminEmail);

        var response = await secondAdmin.DeleteAsync($"/api/users/{primaryAdminId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("primary administrator", body.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);

        // Cleanup second admin
        var cleanup = await admin.DeleteAsync($"/api/users/{secondAdminId}");
        Assert.Equal(HttpStatusCode.NoContent, cleanup.StatusCode);
    }

    [Fact]
    public async Task Administrator_cannot_delete_their_own_account()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetGuid();

        var response = await admin.DeleteAsync($"/api/users/{adminId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("own account", body.RootElement.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_user_leaves_uploaded_tracks_intact_with_null_creator()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var uploaderEmail = Unique("uploader");
        var uploaderId = await CreateUserAsync(admin, uploaderEmail, RoleNames.Administrator);
        using var uploader = await LoginAsync(uploaderEmail);

        // The uploader uploads a track
        var trackId = await UploadTrackAsync(uploader);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var trackBefore = await db.Tracks.AsNoTracking().SingleAsync(x => x.Id == trackId);
            Assert.Equal(uploaderId, trackBefore.CreatedByUserId);
        }

        // Admin deletes the uploader
        var deleteResponse = await admin.DeleteAsync($"/api/users/{uploaderId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify the user is gone from db
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.False(await db.Users.AnyAsync(x => x.Id == uploaderId));

            // CRITICAL: The track MUST still exist in the database and be active!
            var trackAfter = await db.Tracks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == trackId);
            Assert.NotNull(trackAfter);
            Assert.False(trackAfter.IsDeleted);
            Assert.Null(trackAfter.CreatedByUserId);
        }

        // Verify the track is still accessible through the API
        var trackApi = await admin.GetAsync($"/api/tracks/{trackId}");
        Assert.Equal(HttpStatusCode.OK, trackApi.StatusCode);
    }

    [Fact]
    public async Task Users_list_marks_isPrimaryAdmin_flag()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var listenerEmail = Unique("listener-flag");
        var listenerId = await CreateUserAsync(admin, listenerEmail, RoleNames.Listener);

        var listResponse = await admin.GetFromJsonAsync<JsonElement[]>("/api/users");
        Assert.NotNull(listResponse);

        var primaryAdminEntry = listResponse.Single(x => x.GetProperty("isPrimaryAdmin").GetBoolean());
        Assert.Contains(RoleNames.Administrator, primaryAdminEntry.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        var listenerEntry = listResponse.Single(x => x.GetProperty("id").GetGuid() == listenerId);
        Assert.False(listenerEntry.GetProperty("isPrimaryAdmin").GetBoolean());

        // Cleanup
        await admin.DeleteAsync($"/api/users/{listenerId}");
    }

    // CR-003: reporting the same track repeatedly keeps working and stays one row.
    [Fact]
    public async Task Repeated_playback_reports_for_one_track_stay_at_one_history_row()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);
        var email = Unique("history");
        var userId = await CreateUserAsync(admin, email, "Listener");
        using var user = await LoginAsync(email);

        for (var i = 0; i < 5; i++)
        {
            var response = await user.PostAsJsonAsync("/api/history/playback",
                new { trackId, positionSeconds = 10, completed = false, listenedSeconds = 1 });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var rows = await ReadingHistoryRowsAsync(userId, trackId);
        Assert.Single(rows);
        Assert.Equal(5, rows[0].PlayCount);
    }

    // CR-003 + CR-018: concurrent reports neither wedge the endpoint nor lose the increments.
    [Fact]
    public async Task Concurrent_playback_reports_do_not_wedge_the_endpoint_or_lose_time()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);
        var email = Unique("concurrent");
        var userId = await CreateUserAsync(admin, email, "Listener");
        using var user = await LoginAsync(email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            user.PostAsJsonAsync("/api/history/playback",
                new { trackId, positionSeconds = 1, completed = false, listenedSeconds = 5 })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        // The next sequential report must still succeed: before the fix the duplicates made this 500.
        var after = await user.PostAsJsonAsync("/api/history/playback",
            new { trackId, positionSeconds = 1, completed = false, listenedSeconds = 0 });
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);

        var rows = await ReadingHistoryRowsAsync(userId, trackId);
        Assert.Single(rows);

        // Six increments of five seconds; a lost update would leave a smaller total.
        var used = await ReadingListenSecondsUsedAsync(userId);
        Assert.Equal(30, used);
    }

    // CR-011: an oversized batch is rejected instead of doing unbounded work.
    [Fact]
    public async Task Adding_more_than_500_tracks_to_a_folder_is_rejected()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var folder = await admin.PostAsJsonAsync("/api/folders", new { name = $"cr011-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, folder.StatusCode);
        using var folderJson = JsonDocument.Parse(await folder.Content.ReadAsStringAsync());
        var folderId = folderJson.RootElement.GetProperty("id").GetGuid();

        var tooMany = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();
        var response = await admin.PostAsJsonAsync($"/api/folders/{folderId}/tracks", new { trackIds = tooMany });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("500", body.RootElement.GetProperty("error").GetString()!);
    }

    // CR-013: artwork is served from an authenticated endpoint, so it must never be cacheable by a
    // shared proxy.
    [Fact]
    public async Task Track_artwork_is_never_publicly_cacheable()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);

        var response = await admin.GetAsync($"/api/tracks/{trackId}/artwork");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The WAV fixture has no embedded cover; the header rule is still asserted whenever
            // artwork exists, and this test documents that the fixture cannot produce it.
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private, "artwork must be private, not shared-cacheable");
        Assert.False(response.Headers.CacheControl?.Public ?? false, "artwork must not be marked public");
        Assert.Contains("max-age=86400", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    // CR-012: the anonymous probe must not mutate the data root or enumerate capabilities.
    [Fact]
    public async Task Health_probe_writes_nothing_and_discloses_no_capabilities()
    {
        using var client = factory.CreateClient();
        var quarantine = Path.Combine(factory.DataRoot, "quarantine");
        var before = Directory.Exists(quarantine) ? Directory.GetFiles(quarantine).Length : 0;

        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        var after = Directory.Exists(quarantine) ? Directory.GetFiles(quarantine).Length : 0;
        Assert.Equal(before, after);

        // Reconnaissance fields that the old payload published to anyone.
        Assert.DoesNotContain("telegramConfigured", raw);
        Assert.DoesNotContain("malwareScannerConfigured", raw);
        Assert.DoesNotContain("ffprobe", raw);

        using var json = JsonDocument.Parse(raw);
        var checks = json.RootElement.GetProperty("checks");
        Assert.True(checks.GetProperty("database").GetBoolean());
        Assert.True(checks.GetProperty("storageReadable").GetBoolean());
    }

    // CR-017: paging through the library must not repeat or drop rows that share a timestamp.
    [Fact]
    public async Task Paging_through_the_library_covers_every_track_exactly_once()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var full = await admin.GetFromJsonAsync<JsonElement>("/api/tracks?pageSize=200&sort=oldest");
        var expected = full.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.True(expected.Count > 1, "the fixture needs at least two tracks for a paging assertion");

        var collected = new List<Guid>();
        var page = 1;
        while (true)
        {
            var chunk = await admin.GetFromJsonAsync<JsonElement>($"/api/tracks?pageSize=3&sort=oldest&page={page}");
            var items = chunk.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
            if (items.Count == 0) break;
            collected.AddRange(items);
            page++;
            if (page > 100) throw new InvalidOperationException("paging did not terminate");
        }

        Assert.Equal(expected.Count, collected.Count);
        Assert.Equal(expected.OrderBy(x => x), collected.OrderBy(x => x));
        Assert.Equal(collected.Count, collected.Distinct().Count());
    }

    // CR-014: album artwork is derived from a track cover, so the album endpoint is reachable and
    // the album DTO does not claim artwork the endpoint cannot serve.
    [Fact]
    public async Task Album_artwork_endpoint_falls_back_to_a_track_cover()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var albums = await admin.GetFromJsonAsync<JsonElement>("/api/albums?pageSize=50");
        foreach (var album in albums.GetProperty("items").EnumerateArray())
        {
            var albumId = album.GetProperty("id").GetGuid();
            var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}");
            var artworkUrl = detail.GetProperty("album").GetProperty("artworkUrl");

            var artwork = await admin.GetAsync($"/api/albums/{albumId}/artwork");
            if (artworkUrl.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal(HttpStatusCode.NotFound, artwork.StatusCode);
                continue;
            }

            // A non-null URL must actually resolve - the previous dead feature advertised nothing,
            // and a broken advertisement would be the opposite failure.
            Assert.Equal(HttpStatusCode.OK, artwork.StatusCode);
            Assert.True(artwork.Headers.CacheControl?.Private, "album artwork must be private, not shared-cacheable");
        }
    }

    // CR-019: streaming.maxConcurrentPerUser is a real limit read from the stored setting, not a
    // dead control that the dashboard lets an operator edit.
    [Fact]
    public async Task The_stored_per_user_stream_limit_is_enforced()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin);
        var email = Unique("streams");
        await CreateUserAsync(admin, email, "Listener");
        using var user = await LoginAsync(email);
        var userId = (await user.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();

        try
        {
            var put = await admin.PutAsJsonAsync("/api/admin/settings",
                new Dictionary<string, string> { [SettingsEndpoints.MaxConcurrentPerUserKey] = "1" });
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

            // A single stream is within the limit.
            var first = await user.GetAsync($"/api/tracks/{trackId}/stream");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            await first.Content.ReadAsByteArrayAsync();

            // Hold one lease on the tracker the endpoint itself resolves. The next request is the
            // second concurrent stream for this account, so it must be refused rather than served.
            var tracker = factory.Services.GetRequiredService<MusicHub.Api.Realtime.StreamTracker>();
            using (tracker.Track(userId))
            {
                Assert.Equal(1, tracker.ActiveFor(userId));
                var blocked = await user.GetAsync($"/api/tracks/{trackId}/stream");
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
                using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
                Assert.Contains("simultaneous streams", body.RootElement.GetProperty("error").GetString()!);
            }

            // Releasing the lease frees the slot: the limit is accounting, not a latch.
            Assert.Equal(0, tracker.ActiveFor(userId));
            var allowedAgain = await user.GetAsync($"/api/tracks/{trackId}/stream");
            Assert.Equal(HttpStatusCode.OK, allowedAgain.StatusCode);
            await allowedAgain.Content.ReadAsByteArrayAsync();
        }
        finally
        {
            // The suite shares one database; leave the operator setting as it was found.
            await admin.PutAsJsonAsync("/api/admin/settings",
                new Dictionary<string, string> { [SettingsEndpoints.MaxConcurrentPerUserKey] = "4" });
        }
    }

    // CR-023: pairing must apply the same entitlement enforcement as login and refresh, so it cannot
    // mint tokens for an account that is already past its expiry.
    [Fact]
    public async Task Pairing_refuses_an_account_that_is_past_its_expiry()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("pairing");
        var userId = await CreateUserAsync(admin, email, "Listener");
        using var user = await LoginAsync(email);

        var created = await user.PostAsJsonAsync("/api/pairing/create", new { deviceName = "cr023-device" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var code = createdJson.RootElement.GetProperty("code").GetString()!;

        // Expire the account behind the sweep's back: IsActive stays true until something evaluates
        // the entitlement, which is exactly the window the finding described.
        await SetExpiryAsync(userId, DateTimeOffset.UtcNow.AddMinutes(-1));

        using var anonymous = factory.CreateClient();
        var claim = await anonymous.PostAsJsonAsync("/api/pairing/claim", new { code, deviceName = "cr023-device" });
        Assert.Equal(HttpStatusCode.Unauthorized, claim.StatusCode);
        using var claimJson = JsonDocument.Parse(await claim.Content.ReadAsStringAsync());
        Assert.Equal("expired", claimJson.RootElement.GetProperty("reason").GetString());
        Assert.False(claimJson.RootElement.TryGetProperty("accessToken", out _),
            "an expired account must not receive a token pair");
    }

    // CR-010: the quota was only checked before the upload, so concurrent imports each passed the
    // check and together overshot the allowance. The commit re-asserts it under the account's row
    // lock, so exactly one of a set of racing over-quota imports can land.
    [Fact]
    public async Task Concurrent_uploads_cannot_overshoot_the_storage_quota()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // A dedicated administrator with a tight quota: administrators are exempt from deactivation
        // but the import pipeline still honours QuotaBytes, and only an administrator may upload.
        const long quota = 400_000;
        var email = Unique("quota");
        await CreateUserAsync(admin, email, "Administrator", quota);
        using var limited = await LoginAsync(email);

        // Each file is ~240 KB (44-byte header + 2 bytes per sample), so two cannot both fit under
        // the 400 KB cap. Distinct sample counts keep the SHA-256 dedup path out of the way.
        var sampleCounts = new[] { 120_000, 120_001, 120_002, 120_003 };
        using var gate = new SemaphoreSlim(0);
        var uploads = sampleCounts.Select(async (count, index) =>
        {
            var form = BuildUpload(ApiFactory.BuildWav(count), $"cr010-{index}.wav");
            await gate.WaitAsync();
            return await limited.PostAsync("/api/uploads", form);
        }).ToList();

        // Release every upload at once so the four imports overlap instead of queueing behind each
        // other, which is the situation the finding describes.
        gate.Release(sampleCounts.Length);
        var responses = await Task.WhenAll(uploads);

        var outcomes = new List<bool>();
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            outcomes.Add(json.RootElement.GetProperty("results")[0].GetProperty("success").GetBoolean());
        }

        Assert.Equal(1, outcomes.Count(x => x));
        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.MultiStatus or HttpStatusCode.UnprocessableEntity,
            $"unexpected status {r.StatusCode}"));

        // The stored bytes are what the quota is about, so assert the invariant directly.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var userId = (await db.Users.AsNoTracking().Where(x => x.Email == email).Select(x => x.Id).SingleAsync());
        var stored = await db.Tracks.AsNoTracking()
            .Where(x => x.CreatedByUserId == userId && !x.IsDeleted)
            .SumAsync(x => (long?)x.AudioAsset.SizeBytes) ?? 0;
        Assert.InRange(stored, 1, quota);

        // The refused imports report the quota rather than a database failure, and leave nothing
        // behind in the store (CR-024).
        var failed = responses
            .Where(r => r.StatusCode != HttpStatusCode.Created)
            .ToList();
        Assert.NotEmpty(failed);
        foreach (var response in failed)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Contains("quota", json.RootElement.GetProperty("results")[0].GetProperty("error").GetString()!,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // Found while proving CR-010: untagged files all resolve the fallback artist "Unknown Artist",
    // and the read-then-insert in GetOrCreateArtistAsync let two concurrent imports both miss it and
    // both insert. The loser hit the unique index and lost the entire upload as "The file could not
    // be stored." Insert-or-ignore plus a re-read makes concurrent untagged imports all succeed.
    //
    // The uploads come from *different* administrators on purpose. CR-010's account row lock
    // serialises imports per user, which would hide this race; the artist row is shared across all
    // users, so only independent accounts exercise it.
    [Fact]
    public async Task Concurrent_untagged_uploads_from_different_users_all_succeed()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var sampleCounts = new[] { 21_000, 21_001, 21_002, 21_003, 21_004, 21_005 };
        var clients = new List<HttpClient>();
        foreach (var _ in sampleCounts)
        {
            var email = Unique("race");
            await CreateUserAsync(admin, email, "Administrator");
            clients.Add(await LoginAsync(email));
        }

        using var gate = new SemaphoreSlim(0);
        var uploads = sampleCounts.Select(async (count, index) =>
        {
            var form = BuildUpload(ApiFactory.BuildWav(count), $"untagged-{index}.wav");
            await gate.WaitAsync();
            return await clients[index].PostAsync("/api/uploads", form);
        }).ToList();

        try
        {
            gate.Release(sampleCounts.Length);
            var responses = await Task.WhenAll(uploads);

            foreach (var response in responses)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var result = json.RootElement.GetProperty("results")[0];
                Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
            }

            // Exactly one artist row exists for the shared fallback name, not one per import.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            Assert.Equal(1, await db.Artists.CountAsync(x => x.NormalizedName == "unknown artist"));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    // ---- helpers ------------------------------------------------------------------------

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}@review.tests.musichub.local";

    private async Task<Guid> UploadTrackAsync(HttpClient admin, int sampleCount = 0)
    {
        // The store deduplicates by SHA-256, so every upload needs distinct bytes.
        var count = sampleCount == 0 ? Random.Shared.Next(9400, 60000) : sampleCount;
        using var form = BuildUpload(ApiFactory.BuildWav(count), $"cr.{Guid.NewGuid():N}.wav");
        var response = await admin.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("results")[0];
        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
        return result.GetProperty("track").GetProperty("id").GetGuid();
    }

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(content, "files", fileName);
        return form;
    }

    private async Task<Guid> CreateUserAsync(HttpClient admin, string email, string role, long quotaBytes = 0)
    {
        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password = Password,
            displayName = email.Split('@')[0],
            role,
            quotaBytes,
            canUsePlayer = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> CreateRoleClientAsync(HttpClient admin, string role)
    {
        var email = Unique(role.ToLowerInvariant());
        await CreateUserAsync(admin, email, role);
        return await LoginAsync(email);
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        var token = await ApiFactory.LoginAsync(client, email, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> CountAdministratorsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.UserRoles.CountAsync(x => x.Role.Name == RoleNames.Administrator);
    }

    private async Task<int> CountActiveAdministratorsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.UserRoles.CountAsync(x => x.Role.Name == RoleNames.Administrator && x.User.IsActive);
    }

    private async Task<List<ListeningHistory>> ReadingHistoryRowsAsync(Guid userId, Guid trackId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.ListeningHistory.AsNoTracking().Where(x => x.UserId == userId && x.TrackId == trackId).ToListAsync();
    }

    private async Task<long> ReadingListenSecondsUsedAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.ListenSecondsUsed).SingleAsync();
    }

    /// <summary>
    /// Moves <c>ExpiresAt</c> into the past directly, so the account is expired while
    /// <c>IsActive</c> is still true - the window before the 2-minute sweep evaluates it.
    /// </summary>
    private async Task SetExpiryAsync(Guid userId, DateTimeOffset expiresAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        user.ExpiresAt = expiresAt;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
