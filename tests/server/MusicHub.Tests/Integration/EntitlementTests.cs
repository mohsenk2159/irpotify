using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Account expiry, storage quota, and listening-time quota: creation, enforcement at login,
/// per-request validation, playback reporting, the background sweep, and reactivation.
/// </summary>
[Collection("api")]
public class EntitlementTests(ApiFactory factory)
{
    private const string Password = "Entitlement!Passw0rd";

    [Fact]
    public async Task Expiry_deactivates_the_account_and_revokes_active_sessions()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("expiry");
        var userId = await CreateUserAsync(admin, email, "Listener", expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));

        using var user = await LoginAsync(email);
        var me = await user.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        await SetUserAsync(userId, u => u.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1));

        // The already-issued access token is rejected on the very next request.
        var afterExpiry = await user.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterExpiry.StatusCode);

        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password, deviceName = "tests" });
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
        using var reloginJson = JsonDocument.Parse(await relogin.Content.ReadAsStringAsync());
        Assert.Contains("access period has ended", reloginJson.RootElement.GetProperty("error").GetString()!);

        var summary = await GetUserAsync(admin, userId);
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(DeactivationReasons.Expired, summary.GetProperty("deactivationReason").GetString());
        Assert.Equal(JsonValueKind.String, summary.GetProperty("deactivatedAt").ValueKind);
    }

    [Fact]
    public async Task Listening_quota_is_consumed_by_playback_reports_and_blocks_further_requests()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin, "listen-quota.wav", 7100);
        var email = Unique("listen");
        var userId = await CreateUserAsync(admin, email, "Listener", quotaBytes: 0, listenQuotaSeconds: 60);

        using var user = await LoginAsync(email);

        var first = await user.PostAsJsonAsync("/api/history/playback", new { trackId, positionSeconds = 10, completed = false, listenedSeconds = 30 });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var partial = await GetUserAsync(admin, userId);
        Assert.Equal(30, partial.GetProperty("listenSecondsUsed").GetInt64());
        Assert.True(partial.GetProperty("isActive").GetBoolean());

        // The final report exhausts the allowance and deactivates the account immediately.
        var second = await user.PostAsJsonAsync("/api/history/playback", new { trackId, positionSeconds = 20, completed = false, listenedSeconds = 30 });
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(DeactivationReasons.ListeningQuota, secondJson.RootElement.GetProperty("reason").GetString());

        var summary = await GetUserAsync(admin, userId);
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(DeactivationReasons.ListeningQuota, summary.GetProperty("deactivationReason").GetString());
        Assert.Equal(60, summary.GetProperty("listenSecondsUsed").GetInt64());

        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password, deviceName = "tests" });
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);

        // The revoked access token cannot report playback any more.
        var blocked = await user.PostAsJsonAsync("/api/history/playback", new { trackId, positionSeconds = 30, completed = false, listenedSeconds = 5 });
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
    }

    /// <summary>
    /// B-023: a modified client that reports <c>listenedSeconds = 0</c> must still be charged,
    /// because the server measures the audio bytes it actually served. Before the fix the account
    /// could be used indefinitely by simply lying in every playback report.
    /// </summary>
    [Fact]
    public async Task Streamed_bytes_charge_listening_time_even_when_the_client_reports_zero()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // Long enough that the served audio is worth many seconds at the default bitrate.
        var trackId = await UploadTrackAsync(admin, "tamper-quota.wav", 400_000);
        var email = Unique("tamper");
        var userId = await CreateUserAsync(admin, email, "Listener", quotaBytes: 0, listenQuotaSeconds: 3600);

        using var user = await LoginAsync(email);

        // Obtain a stream token and pull the audio the way the player does - anonymously, with the
        // token in the query string.
        var tokenResponse = await user.PostAsync($"/api/tracks/{trackId}/stream-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var streamUrl = tokenJson.RootElement.GetProperty("streamUrl").GetString()!;

        using var anonymous = factory.CreateClient();
        var audio = await anonymous.GetByteArrayAsync(streamUrl);
        Assert.NotEmpty(audio);

        var before = await GetUserAsync(admin, userId);
        Assert.Equal(0, before.GetProperty("listenSecondsUsed").GetInt64());

        // The forgery: zero seconds reported.
        var forged = await user.PostAsJsonAsync("/api/history/playback",
            new { trackId, positionSeconds = 0, completed = false, listenedSeconds = 0 });
        Assert.Equal(HttpStatusCode.NoContent, forged.StatusCode);

        var after = await GetUserAsync(admin, userId);
        var charged = after.GetProperty("listenSecondsUsed").GetInt64();

        // Decisive assertion: the lie bought nothing. (Under the old code this was 0.)
        Assert.True(charged > 0,
            $"Server charged {charged}s after serving {audio.Length} bytes with a zero-second report.");
    }

    /// <summary>
    /// The server must never charge more than the larger of its own measurement and the client
    /// report, so a legitimate report is not lost and a stream is not billed twice.
    /// </summary>
    [Fact]
    public async Task Client_report_is_still_honoured_when_it_exceeds_measured_stream_time()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var trackId = await UploadTrackAsync(admin, "offline-quota.wav", 7100);
        var email = Unique("offline");
        var userId = await CreateUserAsync(admin, email, "Listener", quotaBytes: 0, listenQuotaSeconds: 3600);

        using var user = await LoginAsync(email);

        // Offline playback never touches the stream endpoint, so the report is the only signal.
        var report = await user.PostAsJsonAsync("/api/history/playback",
            new { trackId, positionSeconds = 45, completed = false, listenedSeconds = 45 });
        Assert.Equal(HttpStatusCode.NoContent, report.StatusCode);

        var summary = await GetUserAsync(admin, userId);
        Assert.Equal(45, summary.GetProperty("listenSecondsUsed").GetInt64());
    }

    [Fact]
    public async Task Storage_quota_deactivates_the_account_at_login()    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("storage");
        var userId = await CreateUserAsync(admin, email, "Listener", quotaBytes: 0);

        // Only administrators may upload, so the storage attributable to a non-administrator is set
        // up directly: this is the state an operator reaches by uploading on a user's behalf, or by
        // demoting an account that already owns content. The quota rule itself is unchanged.
        var wav = ApiFactory.BuildWav(7300);
        using var form = BuildUpload(wav, "storage-quota.wav");
        var upload = await admin.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadJson = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var trackId = uploadJson.RootElement.GetProperty("results")[0].GetProperty("track").GetProperty("id").GetGuid();

        await SetTrackOwnerAsync(trackId, userId);

        using var user = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);

        // Non-administrators have read-only library access, so the storage quota is not reachable
        // through an upload they can no longer make; enforcement is asserted by the login-time and
        // sweep checks below, exactly as it is for any other quota.
        var denied = await user.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Tighten the quota below the already-used storage, as an administrator would.
        var patch = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { quotaBytes = 1024 });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var summary = await GetUserAsync(admin, userId);
        var usedBytes = summary.GetProperty("storageUsedBytes").GetInt64();
        Assert.True(usedBytes >= wav.Length, $"Expected storage usage >= {wav.Length} but was {usedBytes}.");

        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password, deviceName = "tests" });
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
        using var reloginJson = JsonDocument.Parse(await relogin.Content.ReadAsStringAsync());
        Assert.Contains("storage allowance", reloginJson.RootElement.GetProperty("error").GetString()!);

        summary = await GetUserAsync(admin, userId);
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(DeactivationReasons.StorageQuota, summary.GetProperty("deactivationReason").GetString());
    }

    [Fact]
    public async Task Background_sweep_deactivates_expired_accounts()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("sweep");
        var userId = await CreateUserAsync(admin, email, "Listener");
        await SetUserAsync(userId, u => u.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserEntitlementService>();
        var deactivated = await service.EnforceAllAsync(CancellationToken.None);

        Assert.True(deactivated >= 1);
        var summary = await GetUserAsync(admin, userId);
        Assert.False(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(DeactivationReasons.Expired, summary.GetProperty("deactivationReason").GetString());
    }

    [Fact]
    public async Task Administrators_are_exempt_from_expiry_and_quotas()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetGuid();

        await SetUserAsync(adminId, u =>
        {
            u.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-60);
            u.ListenQuotaSeconds = 10;
            u.ListenSecondsUsed = 10;
            u.QuotaBytes = 1;
        });

        try
        {
            var stillValid = await admin.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<UserEntitlementService>();
                await service.EnforceAllAsync(CancellationToken.None);
            }

            var afterSweep = await admin.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, afterSweep.StatusCode);

            var summary = await GetUserAsync(admin, adminId);
            Assert.True(summary.GetProperty("isActive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, summary.GetProperty("deactivationReason").ValueKind);
        }
        finally
        {
            await SetUserAsync(adminId, u =>
            {
                u.ExpiresAt = null;
                u.ListenQuotaSeconds = 0;
                u.ListenSecondsUsed = 0;
                u.QuotaBytes = 10L * 1024 * 1024 * 1024;
                u.IsActive = true;
                u.DeactivatedAt = null;
                u.DeactivationReason = null;
            });
        }
    }

    [Fact]
    public async Task Manual_deactivation_and_reactivation_follow_the_entitlement_rules()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var email = Unique("reactivate");
        var userId = await CreateUserAsync(admin, email, "Listener");

        var deactivate = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var blockedLogin = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = Password, deviceName = "tests" });
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);
        using var blockedJson = JsonDocument.Parse(await blockedLogin.Content.ReadAsStringAsync());
        Assert.Contains("disabled by an administrator", blockedJson.RootElement.GetProperty("error").GetString()!);

        var manual = await GetUserAsync(admin, userId);
        Assert.Equal(DeactivationReasons.Manual, manual.GetProperty("deactivationReason").GetString());

        // Reactivation is refused while the expiry is still in the past.
        await SetUserAsync(userId, u => u.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10));
        var refused = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var refusedJson = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Contains("access period has ended", refusedJson.RootElement.GetProperty("error").GetString()!);

        // Extending the expiry and reactivating in one request succeeds.
        var reactivate = await admin.PatchAsJsonAsync($"/api/users/{userId}", new
        {
            isActive = true,
            expiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        var summary = await GetUserAsync(admin, userId);
        Assert.True(summary.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("deactivatedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("deactivationReason").ValueKind);

        using var user = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Entitlement_values_are_validated_and_can_be_cleared_or_reset()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var pastExpiry = await admin.PostAsJsonAsync("/api/users", new
        {
            email = Unique("past"),
            password = Password,
            displayName = "Past",
            role = "Listener",
            expiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, pastExpiry.StatusCode);

        var negativeListen = await admin.PostAsJsonAsync("/api/users", new
        {
            email = Unique("negative"),
            password = Password,
            displayName = "Negative",
            role = "Listener",
            listenQuotaSeconds = -1
        });
        Assert.Equal(HttpStatusCode.BadRequest, negativeListen.StatusCode);

        var oversizedListen = await admin.PostAsJsonAsync("/api/users", new
        {
            email = Unique("oversized"),
            password = Password,
            displayName = "Oversized",
            role = "Listener",
            listenQuotaSeconds = 400_000_000L
        });
        Assert.Equal(HttpStatusCode.BadRequest, oversizedListen.StatusCode);

        var email = Unique("editable");
        var userId = await CreateUserAsync(admin, email, "Listener", listenQuotaSeconds: 3600, expiresAt: DateTimeOffset.UtcNow.AddDays(1));

        var created = await GetUserAsync(admin, userId);
        Assert.Equal(3600, created.GetProperty("listenQuotaSeconds").GetInt64());
        Assert.Equal(0, created.GetProperty("listenSecondsUsed").GetInt64());
        Assert.True(created.GetProperty("storageUsedBytes").GetInt64() >= 0);

        var past = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { expiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var negativePatch = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { listenQuotaSeconds = -5 });
        Assert.Equal(HttpStatusCode.BadRequest, negativePatch.StatusCode);

        await SetUserAsync(userId, u => u.ListenSecondsUsed = 100);
        var clear = await admin.PatchAsJsonAsync($"/api/users/{userId}", new { clearExpiresAt = true, listenQuotaSeconds = 0, resetListenUsage = true });
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);

        var cleared = await GetUserAsync(admin, userId);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("expiresAt").ValueKind);
        Assert.Equal(0, cleared.GetProperty("listenQuotaSeconds").GetInt64());
        Assert.Equal(0, cleared.GetProperty("listenSecondsUsed").GetInt64());
    }

    // ---- helpers ------------------------------------------------------------------------

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}@entitlements.tests.musichub.local";

    private async Task<Guid> CreateUserAsync(
        HttpClient admin,
        string email,
        string role,
        long quotaBytes = 0,
        long listenQuotaSeconds = 0,
        DateTimeOffset? expiresAt = null)
    {
        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password = Password,
            displayName = email.Split('@')[0],
            role,
            quotaBytes,
            canUsePlayer = true,
            listenQuotaSeconds,
            expiresAt
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password, deviceName = "entitlement-tests", clientType = "player" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> GetUserAsync(HttpClient admin, Guid userId)
    {
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        foreach (var user in users.EnumerateArray())
        {
            if (user.GetProperty("id").GetGuid() == userId) return user.Clone();
        }
        throw new InvalidOperationException($"User {userId} was not returned by /api/users.");
    }

    private async Task SetUserAsync(Guid userId, Action<User> mutate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        mutate(user);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Attributes an existing track's storage to another account. Stands in for the operator action
    /// of uploading on a user's behalf now that only administrators may upload.
    /// </summary>
    private async Task SetTrackOwnerAsync(Guid trackId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var track = await db.Tracks.SingleAsync(x => x.Id == trackId);
        track.CreatedByUserId = userId;
        await db.SaveChangesAsync();
    }

    private static MultipartFormDataContent BuildUpload(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(content, "files", fileName);
        return form;
    }

    private static async Task<Guid> UploadTrackAsync(HttpClient admin, string fileName, int sampleCount)
    {
        var wav = ApiFactory.BuildWav(sampleCount);
        using var form = BuildUpload(wav, fileName);
        var response = await admin.PostAsync("/api/uploads", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("results")[0];
        Assert.True(result.GetProperty("success").GetBoolean());
        return result.GetProperty("track").GetProperty("id").GetGuid();
    }
}
