using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicHub.Tests.Integration;

[Collection("api")]
public class MediaPipelineTests(ApiFactory factory)
{
    [Fact]
    public async Task Upload_dedup_metadata_and_range_streaming_work_end_to_end()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var wav = ApiFactory.BuildWav();

        var upload = await UploadAsync(admin, wav, "integration-tone.wav");
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        using var uploadJson = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var result = uploadJson.RootElement.GetProperty("results")[0];
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.False(result.GetProperty("duplicate").GetBoolean());
        var track = result.GetProperty("track");
        var trackId = track.GetProperty("id").GetGuid();
        var sha256 = track.GetProperty("sha256").GetString()!;
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), sha256);
        Assert.False(string.IsNullOrWhiteSpace(track.GetProperty("title").GetString()));
        Assert.True(track.GetProperty("durationSeconds").GetDouble() > 0);

        var raw = await upload.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storage", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quarantine", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("music/", raw, StringComparison.OrdinalIgnoreCase);

        var duplicate = await UploadAsync(admin, wav, "integration-tone-copy.wav");
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        var duplicateResult = duplicateJson.RootElement.GetProperty("results")[0];
        Assert.True(duplicateResult.GetProperty("duplicate").GetBoolean());
        Assert.Equal(trackId, duplicateResult.GetProperty("track").GetProperty("id").GetGuid());

        var garbage = await UploadAsync(admin, Enumerable.Range(1, 64).Select(x => (byte)x).ToArray(), "not-audio.mp3");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, garbage.StatusCode);

        using var anonymous = factory.CreateClient();
        var anonymousUpload = await UploadAsync(anonymous, wav, "anonymous.wav");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousUpload.StatusCode);

        var tokenResponse = await admin.PostAsync($"/api/tracks/{trackId}/stream-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var streamUrl = tokenJson.RootElement.GetProperty("streamUrl").GetString()!;

        var full = await admin.GetAsync(streamUrl);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var fullBytes = await full.Content.ReadAsByteArrayAsync();
        Assert.Equal(wav.Length, fullBytes.Length);
        Assert.Equal($"\"{sha256}\"", full.Headers.ETag!.Tag);
        Assert.Contains("bytes", full.Headers.AcceptRanges);

        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, streamUrl);
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 99);
        var range = await admin.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal(100, (await range.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal($"bytes 0-99/{wav.Length}", range.Content.Headers.ContentRange!.ToString());

        var unsatisfiableRequest = new HttpRequestMessage(HttpMethod.Get, streamUrl);
        unsatisfiableRequest.Headers.Range = new RangeHeaderValue(wav.Length, wav.Length + 10);
        var unsatisfiable = await admin.SendAsync(unsatisfiableRequest);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, unsatisfiable.StatusCode);

        var anonymousStream = await anonymous.GetAsync($"/api/tracks/{trackId}/stream");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStream.StatusCode);

        var invalidToken = await anonymous.GetAsync($"/api/tracks/{trackId}/stream?token=invalid.invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
    }

    [Fact]
    public async Task Untagged_upload_takes_the_original_file_name_as_its_title()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // BuildWav writes a bare RIFF/WAVE header with no INFO chunk, so the file carries no
        // title tag. The track title must then come from the name the uploader supplied, not
        // from the GUID-named file used while the upload sits in quarantine (B-062).
        var fileName = $"untagged-{Guid.NewGuid():N}.wav";
        var upload = await UploadAsync(admin, ApiFactory.BuildWav(4000), fileName);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        using var json = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("results")[0];
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.False(result.GetProperty("duplicate").GetBoolean());

        var title = result.GetProperty("track").GetProperty("title").GetString();
        Assert.Equal(Path.GetFileNameWithoutExtension(fileName), title);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(content, "files", fileName);
        return await client.PostAsync("/api/uploads", form);
    }
}
