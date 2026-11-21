using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MusicHub.Tests.Integration;

/// <summary>
/// The library location is reported and validated, never silently changed. These tests pin the
/// read-only contract and, in particular, the checks that stop an administrator from pointing the
/// library at a folder that would damage the host.
/// </summary>
/// <remarks>
/// The two roots are deliberately distinct and the tests assert that: the data root holds the keys
/// and the database credential and is fixed, while the library root is the one an administrator may
/// move. Confusing them is the mistake this contract exists to prevent.
/// </remarks>
[Collection("api")]
public class StorageLocationTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_can_see_the_library_and_the_data_root_separately()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var response = await admin.GetAsync("/api/admin/storage/location");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        var dataRoot = root.GetProperty("dataRoot").GetString();
        Assert.False(string.IsNullOrWhiteSpace(dataRoot));
        Assert.True(Path.IsPathFullyQualified(dataRoot!));
        var resolvedFrom = root.GetProperty("dataRootFrom").GetString();
        Assert.True(resolvedFrom is "environment" or "configuration" or "default", resolvedFrom);

        var library = root.GetProperty("library");
        var libraryPath = library.GetProperty("path").GetString();
        Assert.False(string.IsNullOrWhiteSpace(libraryPath));
        Assert.True(Path.IsPathFullyQualified(libraryPath!));
        var librarySource = library.GetProperty("source").GetString();
        Assert.True(librarySource is "default" or "relocated", librarySource);

        // With no pointer written, the library is the data root: existing installs keep working.
        Assert.Equal(dataRoot, libraryPath);
        Assert.Equal("default", librarySource);

        var areas = library.GetProperty("areas").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).ToList();
        foreach (var expected in new[] { "music", "artwork", "quarantine" })
        {
            Assert.Contains(expected, areas);
        }

        var music = library.GetProperty("areas").EnumerateArray()
            .First(x => x.GetProperty("name").GetString() == "music");
        Assert.Equal(Path.Combine(libraryPath!, "music"), music.GetProperty("path").GetString());

        Assert.Equal(JsonValueKind.Number, root.GetProperty("assets").GetProperty("count").ValueKind);
        Assert.False(root.GetProperty("relocation").GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Storage_endpoints_are_denied_to_non_administrators()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/storage/location")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/admin/storage/validate", new { path = @"C:\temp" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/admin/storage/relocate",
                new { path = @"C:\temp", confirmation = "relocate" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/storage/relocation")).StatusCode);

        using var admin = await factory.CreateAdminClientAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            email = ApiFactory.ListenerEmail,
            password = ApiFactory.ListenerPassword,
            displayName = "Storage Listener",
            role = "Listener"
        });
        Assert.True(create.IsSuccessStatusCode || create.StatusCode == HttpStatusCode.Conflict);

        using var listener = factory.CreateClient();
        var token = await ApiFactory.LoginAsync(listener, ApiFactory.ListenerEmail, ApiFactory.ListenerPassword);
        listener.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/admin/storage/location")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await listener.PostAsJsonAsync("/api/admin/storage/validate", new { path = @"C:\temp" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await listener.PostAsJsonAsync("/api/admin/storage/relocate",
                new { path = @"C:\temp", confirmation = "relocate" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await listener.GetAsync("/api/admin/storage/relocation")).StatusCode);
    }

    [Fact]
    public async Task A_usable_folder_validates_and_leaves_nothing_behind()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var candidate = Path.Combine(Path.GetTempPath(), "musichub-storage-candidate-" + Guid.NewGuid().ToString("N"));

        try
        {
            var report = await ValidateAsync(admin, candidate);
            Assert.True(report.GetProperty("valid").GetBoolean(), Describe(report));
            Assert.All(report.GetProperty("checks").EnumerateArray(),
                x => Assert.True(x.GetProperty("ok").GetBoolean(), Describe(report)));
            Assert.Equal(candidate, report.GetProperty("resolvedPath").GetString());
            Assert.NotEqual(JsonValueKind.Null, report.GetProperty("disk").ValueKind);
            Assert.Equal(JsonValueKind.Number, report.GetProperty("disk").GetProperty("freeBytes").ValueKind);

            // The probe must not create the folder it was asked about.
            Assert.False(Directory.Exists(candidate));
        }
        finally
        {
            if (Directory.Exists(candidate)) Directory.Delete(candidate, true);
        }
    }

    [Fact]
    public async Task Dangerous_or_useless_targets_are_rejected_with_a_reason()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var relative = await ValidateAsync(admin, @"relative\library");
        Assert.False(relative.GetProperty("valid").GetBoolean());
        Assert.False(Check(relative, "path.absolute"));

        var traversal = await ValidateAsync(admin, Path.Combine(Path.GetTempPath(), "..", "library"));
        Assert.False(traversal.GetProperty("valid").GetBoolean());
        Assert.False(Check(traversal, "path.no.traversal"));

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = await ValidateAsync(admin, Path.Combine(windows, "MusicHubLibrary"));
        Assert.False(system.GetProperty("valid").GetBoolean());
        Assert.False(Check(system, "path.not.system"));

        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var root = await ValidateAsync(admin, driveRoot);
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.False(Check(root, "path.not.drive.root"));

        var applicationDirectory = Path.Combine(AppContext.BaseDirectory, "library");
        var inside = await ValidateAsync(admin, applicationDirectory);
        Assert.False(inside.GetProperty("valid").GetBoolean());
        Assert.False(Check(inside, "path.outside.application"));

        // A rejected folder is never created: the check that failed exists to prevent exactly that.
        Assert.False(Directory.Exists(Path.Combine(windows, "MusicHubLibrary")));
        Assert.False(Directory.Exists(applicationDirectory));
    }

    [Fact]
    public async Task The_data_root_and_the_keys_folder_are_refused_as_a_library()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var location = await admin.GetAsync("/api/admin/storage/location");
        using var locationJson = JsonDocument.Parse(await location.Content.ReadAsStringAsync());
        var dataRoot = locationJson.RootElement.GetProperty("dataRoot").GetString()!;

        var dataRootReport = await ValidateAsync(admin, dataRoot);
        Assert.False(dataRootReport.GetProperty("valid").GetBoolean());
        Assert.False(Check(dataRootReport, "path.not.data.root"));

        var keysReport = await ValidateAsync(admin, Path.Combine(dataRoot, "keys"));
        Assert.False(keysReport.GetProperty("valid").GetBoolean());
        Assert.False(Check(keysReport, "path.not.keys"));
    }

    [Fact]
    public async Task The_current_library_is_reported_as_already_in_use()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var location = await admin.GetAsync("/api/admin/storage/location");
        using var locationJson = JsonDocument.Parse(await location.Content.ReadAsStringAsync());
        var current = locationJson.RootElement.GetProperty("library").GetProperty("path").GetString()!;

        var report = await ValidateAsync(admin, current);
        Assert.False(report.GetProperty("valid").GetBoolean());
        Assert.False(Check(report, "path.not.current"));
    }

    [Fact]
    public async Task A_folder_that_already_holds_files_is_refused()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var candidate = Path.Combine(Path.GetTempPath(), "musichub-storage-full-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        await File.WriteAllTextAsync(Path.Combine(candidate, "someone-elses-file.mp3"), "not ours");

        try
        {
            var report = await ValidateAsync(admin, candidate);
            Assert.False(report.GetProperty("valid").GetBoolean());
            Assert.False(Check(report, "path.empty"));
            // The operator's file is untouched.
            Assert.True(File.Exists(Path.Combine(candidate, "someone-elses-file.mp3")));
        }
        finally
        {
            Directory.Delete(candidate, true);
        }
    }

    [Fact]
    public async Task A_move_without_the_confirmation_word_is_refused()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var candidate = Path.Combine(Path.GetTempPath(), "musichub-storage-noconfirm-" + Guid.NewGuid().ToString("N"));

        var response = await admin.PostAsJsonAsync("/api/admin/storage/relocate", new { path = candidate });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Nothing was queued and no folder was created.
        var relocation = await admin.GetAsync("/api/admin/storage/relocation");
        using var json = JsonDocument.Parse(await relocation.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("active").GetBoolean());
        Assert.False(Directory.Exists(candidate));
    }

    [Fact]
    public async Task A_move_to_a_dangerous_folder_is_refused_before_anything_is_queued()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var response = await admin.PostAsJsonAsync("/api/admin/storage/relocate",
            new { path = Path.Combine(windows, "MusicHubLibrary"), confirmation = "relocate" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = json.RootElement.GetProperty("checks").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "path.not.system");
        Assert.False(checks.GetProperty("ok").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(windows, "MusicHubLibrary")));
    }

    private static async Task<JsonElement> ValidateAsync(HttpClient client, string path)
    {
        var response = await client.PostAsJsonAsync("/api/admin/storage/validate", new { path });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static bool Check(JsonElement report, string id) =>
        report.GetProperty("checks").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == id)
            .GetProperty("ok").GetBoolean();

    private static string Describe(JsonElement report) =>
        string.Join("; ", report.GetProperty("checks").EnumerateArray()
            .Select(x => $"{x.GetProperty("id").GetString()}={x.GetProperty("ok").GetBoolean()}"));
}
