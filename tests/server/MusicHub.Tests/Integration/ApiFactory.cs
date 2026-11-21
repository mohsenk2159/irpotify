using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace MusicHub.Tests.Integration;

/// <summary>
/// Boots the real API against an isolated PostgreSQL database and temporary data root.
/// The development database is never touched.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@tests.musichub.local";
    public const string AdminPassword = "TestAdminPassw0rd!123";
    public const string ListenerEmail = "listener@tests.musichub.local";
    public const string ListenerPassword = "ListenerPassw0rd!123";
    public const string BootstrapToken = "test-bootstrap-token-0123456789abcdef";

    private const string TestDatabase = "musichub_test";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialized;

    /// <summary>
    /// Connection to the server that hosts the throwaway test database. Overridable so the suite can
    /// run against a CI service container instead of a developer's local cluster:
    /// <c>MUSICHUB_TEST_ADMIN_CONNECTION</c> wins, then the CI default on 5432, then the local
    /// development cluster on 5433.
    /// </summary>
    private static string AdminConnection =>
        Environment.GetEnvironmentVariable("MUSICHUB_TEST_ADMIN_CONNECTION")
        ?? (IsCi
            ? "Host=127.0.0.1;Port=5432;Database=postgres;Username=musichub;Password=musichub;Pooling=false"
            : "Host=127.0.0.1;Port=5433;Database=postgres;Username=musichub;Password=musichub;Pooling=false");

    private static bool IsCi =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));

    public string DataRoot { get; }

    public ApiFactory()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "musichub-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataRoot);
        ResetDatabase();

        var repoRoot = FindRepoRoot();
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("MUSICHUB_CONNECTION_STRING",
            $"Host={HostOf(AdminConnection)};Port={PortOf(AdminConnection)};Database={TestDatabase};Username=musichub;Password=musichub;Pooling=false");
        Environment.SetEnvironmentVariable("MUSICHUB_JWT_SECRET",
            "integration-test-jwt-secret-0123456789-abcdefghijklmnopqrstuvwxyz");
        Environment.SetEnvironmentVariable("MUSICHUB_DATA_ROOT", DataRoot);
        Environment.SetEnvironmentVariable("MUSICHUB_BOOTSTRAP_TOKEN", BootstrapToken);
        Environment.SetEnvironmentVariable("MUSICHUB_REQUIRE_MALWARE_SCAN", "false");
        // The whole suite shares one client IP; the production sign-in limiter would throttle it.
        Environment.SetEnvironmentVariable("RateLimiting__LoginPermitLimit", "500");
        Environment.SetEnvironmentVariable("RateLimiting__PasswordPermitLimit", "500");

        // ffprobe is a Windows binary in a developer checkout (tools\ffmpeg\...\ffprobe.exe). On CI
        // the runner installs a native ffprobe on PATH, and the API already falls back to the bare
        // command name when the variable is unset - so only point it at the checkout when that file
        // really exists, otherwise leave it alone and let the PATH lookup do the work.
        var ffprobe = Path.Combine(repoRoot, "tools", "ffmpeg", "ffmpeg-9.0.2-essentials_build", "bin", "ffprobe.exe");
        var ffmpeg = Path.Combine(repoRoot, "tools", "ffmpeg", "ffmpeg-9.0.2-essentials_build", "bin", "ffmpeg.exe");
        if (File.Exists(ffprobe))
        {
            Environment.SetEnvironmentVariable("MUSICHUB_FFPROBE_PATH", ffprobe);
            Environment.SetEnvironmentVariable("MUSICHUB_FFMPEG_PATH", ffmpeg);
        }
    }

    private static string HostOf(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Host ?? "127.0.0.1";

    private static int PortOf(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Port;

    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        await EnsureInitializedAsync(client);
        var token = await LoginAsync(client, AdminEmail, AdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password, deviceName = "integration-tests" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    public static async Task EnsureInitializedAsync(HttpClient client)
    {
        await Gate.WaitAsync();
        try
        {
            if (_initialized) return;
            var status = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
            if (!status.GetProperty("initialized").GetBoolean())
            {
                var response = await client.PostAsJsonAsync("/api/setup/initialize", new
                {
                    bootstrapToken = BootstrapToken,
                    email = AdminEmail,
                    password = AdminPassword,
                    displayName = "Integration Administrator"
                });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }
            _initialized = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static byte[] BuildWav(int sampleCount = 8000)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataLength = sampleCount * channels * bitsPerSample / 8;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * short.MaxValue * 0.25));
        }
        writer.Flush();
        return buffer.ToArray();
    }

    private static void ResetDatabase()
    {
        using var connection = new NpgsqlConnection(AdminConnection);
        connection.Open();
        Execute(connection,
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TestDatabase}' AND pid <> pg_backend_pid()");
        Execute(connection, $"DROP DATABASE IF EXISTS {TestDatabase}");
        Execute(connection, $"CREATE DATABASE {TestDatabase}");
    }

    private static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MusicHub.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try { Directory.Delete(DataRoot, recursive: true); } catch { }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
