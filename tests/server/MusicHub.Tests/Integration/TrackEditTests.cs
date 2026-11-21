using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Regression test for CR-001: <c>PATCH /api/tracks/{id}</c> returned HTTP 500 while still committing
/// the edit.
///
/// <para>
/// The handler loaded the track without the <c>AudioAsset</c>/<c>FileHash</c> navigations and then
/// built the response through <c>DtoMapper.Track</c>, which reads
/// <c>AudioAsset.SizeBytes</c> and <c>AudioAsset.FileHash.Sha256</c>. Lazy loading is not enabled, so
/// the mapping threw a <c>NullReferenceException</c> after <c>SaveChangesAsync</c> had already run:
/// the row was updated and the caller was told the request failed. The suite had no PATCH test at
/// all, which is why it survived.
/// </para>
///
/// <para>
/// The assertions therefore check both halves of the bug: the response must be a success carrying a
/// fully populated DTO, and the edit must be visible afterwards.
/// </para>
/// </summary>
[Collection("api")]
public class TrackEditTests(ApiFactory factory)
{
    // The content-addressed store deduplicates by SHA-256, so each test needs distinct audio bytes.
    // BuildWav is deterministic for a given sample count, and the existing suite varies it for the
    // same reason. This counter keeps the sample count unique across the whole run.
    private static int _sampleCount = 8300;

    private static async Task<(Guid Id, string Sha256)> UploadTrackAsync(HttpClient admin)
    {
        var sampleCount = Interlocked.Add(ref _sampleCount, 97);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(ApiFactory.BuildWav(sampleCount));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(file, "files", $"cr001-{Guid.NewGuid():N}.wav");

        var response = await admin.PostAsync("/api/uploads", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("results")[0];
        Assert.True(result.GetProperty("success").GetBoolean());
        var track = result.GetProperty("track");
        return (track.GetProperty("id").GetGuid(), track.GetProperty("sha256").GetString()!);
    }

    [Fact]
    public async Task Editing_a_track_returns_a_complete_dto_and_persists_the_change()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (trackId, sha256) = await UploadTrackAsync(admin);

        var patch = await admin.PatchAsJsonAsync($"/api/tracks/{trackId}", new
        {
            title = "CR-001 renamed",
            year = 1999,
            trackNumber = 7
        });

        // Before the fix this was a 500 whose body was the generic problem+json document.
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("application/json", patch.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        var updated = body.RootElement;
        Assert.Equal(trackId, updated.GetProperty("id").GetGuid());
        Assert.Equal("CR-001 renamed", updated.GetProperty("title").GetString());
        Assert.Equal(1999, updated.GetProperty("year").GetInt32());
        Assert.Equal(7, updated.GetProperty("trackNumber").GetInt32());

        // The two properties that used to throw: they come from AudioAsset/FileHash, which the handler
        // did not include.
        Assert.True(updated.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.Equal(sha256, updated.GetProperty("sha256").GetString());
        Assert.False(string.IsNullOrWhiteSpace(updated.GetProperty("artist").GetString()));

        // And the write really happened, rather than the endpoint failing after committing.
        var reread = await admin.GetFromJsonAsync<JsonElement>($"/api/tracks/{trackId}");
        var stored = reread.GetProperty("track");
        Assert.Equal("CR-001 renamed", stored.GetProperty("title").GetString());
        Assert.Equal(1999, stored.GetProperty("year").GetInt32());
        Assert.Equal(sha256, stored.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task Editing_a_missing_track_returns_404_rather_than_500()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var patch = await admin.PatchAsJsonAsync($"/api/tracks/{Guid.NewGuid()}", new { title = "ghost" });

        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);
    }

    [Fact]
    public async Task A_listener_cannot_edit_a_track()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (trackId, _) = await UploadTrackAsync(admin);

        // A fresh listener account, created through the real endpoint.
        var email = $"cr001.listener.{Guid.NewGuid():N}@tests.musichub.local";
        const string password = "CrTestListenerPassw0rd!42";
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            email,
            password,
            displayName = "CR-001 Listener",
            role = "Listener"
        });
        Assert.True(
            create.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"unexpected user creation status {create.StatusCode}");

        using var listener = factory.CreateClient();
        var token = await ApiFactory.LoginAsync(listener, email, password);
        listener.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var patch = await listener.PatchAsJsonAsync($"/api/tracks/{trackId}", new { title = "hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }
}
