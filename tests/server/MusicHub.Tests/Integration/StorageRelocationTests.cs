using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Domain;
using MusicHub.Infrastructure;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Storage;
using Npgsql;

namespace MusicHub.Tests.Integration;

/// <summary>
/// A private database for the relocation tests.
/// </summary>
/// <remarks>
/// The move verifies every audio asset the database knows about, so it must not run against the
/// shared integration database: rows written by other tests would describe files this move never
/// copied and the verification would fail for the wrong reason. This database is created from the
/// real migrations and emptied between tests.
/// </remarks>
public sealed class RelocationDatabase : IAsyncLifetime
{
    private const string DatabaseName = "musichub_test_relocation";

    // The same server the rest of the suite uses, so a CI service container on 5432 works here too.
    private static string AdminConnection => ApiFactory.AdminConnection;

    public string ConnectionString { get; } = BuildConnectionString();

    private static string BuildConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnection)
        {
            Database = DatabaseName,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using (var connection = new NpgsqlConnection(AdminConnection))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{DatabaseName}' AND pid <> pg_backend_pid()");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {DatabaseName}");
            await ExecuteAsync(connection, $"CREATE DATABASE {DatabaseName}");
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public MusicHubDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MusicHubDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Removes everything a previous test wrote, in foreign-key order.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.AudioAssets.ExecuteDeleteAsync();
        await db.FileHashes.ExecuteDeleteAsync();
        await db.AppSettings.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Exercises a real library move: real folders, real files, the real database, the real pointer.
/// </summary>
/// <remarks>
/// The properties under test decide whether a mistake costs disk space or the library: files are
/// copied and verified before the pointer moves, the old copy is never deleted, and a move that
/// cannot prove the copy succeeded does not change the location at all.
/// </remarks>
public class StorageRelocationTests(RelocationDatabase database) : IClassFixture<RelocationDatabase>
{
    [Fact]
    public async Task A_real_move_copies_verifies_and_switches_the_location()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var targetRoot = area.Folder("target");

        var (sha, bytes) = area.WriteAudio(sourceRoot, "music/ab/cd", "track-one");
        var storagePath = $"music/ab/cd/{sha}.mp3";

        await using (var db = database.CreateContext())
        {
            await SeedAssetAsync(db, sha, bytes.Length, storagePath);

            var service = await NewServiceAsync(db, dataRoot, sourceRoot);
            var result = await service.RunAsync(Guid.NewGuid(), targetRoot, CancellationToken.None);

            Assert.Equal(Path.GetFullPath(sourceRoot), result.Source);
            Assert.Equal(Path.GetFullPath(targetRoot), result.Target);
            Assert.Equal(1, result.FilesCopied);
            Assert.Equal(bytes.Length, result.BytesCopied);
        }

        // The bytes arrived intact, the pointer names the new folder, and the old copy still exists.
        var copied = Path.Combine(targetRoot, "music", "ab", "cd", sha + ".mp3");
        Assert.True(File.Exists(copied));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(copied));
        Assert.True(File.Exists(Path.Combine(sourceRoot, "music", "ab", "cd", sha + ".mp3")),
            "The old library copy must be kept so a bad move costs disk space, not the library.");

        var store = NewStore(dataRoot);
        Assert.Equal(Path.GetFullPath(targetRoot), store.LibraryRoot);
        Assert.Equal("relocated", store.Source);
        Assert.Null(store.PointerError);
    }

    [Fact]
    public async Task A_move_that_cannot_verify_the_copy_does_not_change_the_location()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var targetRoot = area.Folder("target");

        // The database claims a file that is not on disk, so the copy cannot be verified.
        var phantom = new string('c', 64);
        var storagePath = $"music/cc/dd/{phantom}.mp3";

        await using var db = database.CreateContext();
        await SeedAssetAsync(db, phantom, 4096, storagePath);

        var service = await NewServiceAsync(db, dataRoot, sourceRoot);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.RunAsync(Guid.NewGuid(), targetRoot, CancellationToken.None));
        Assert.Contains(storagePath, error.Message, StringComparison.OrdinalIgnoreCase);

        // The pointer was never written, so the library still resolves to the old folder.
        Assert.Equal(Path.GetFullPath(sourceRoot), NewStore(dataRoot).LibraryRoot);
        Assert.NotEqual(Path.GetFullPath(targetRoot), NewStore(dataRoot).LibraryRoot);

        var state = await db.AppSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == LibraryRelocationState.SettingKey);
        Assert.NotNull(state);
        Assert.Contains(LibraryRelocationState.PhaseFailed, state!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_target_that_already_holds_other_files_is_refused()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var targetRoot = area.Folder("target");
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "someones-album.flac"), "not ours");

        var (sha, bytes) = area.WriteAudio(sourceRoot, "music/ab/cd", "track-two");

        await using var db = database.CreateContext();
        await SeedAssetAsync(db, sha, bytes.Length, $"music/ab/cd/{sha}.mp3");

        var service = await NewServiceAsync(db, dataRoot, sourceRoot);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.RunAsync(Guid.NewGuid(), targetRoot, CancellationToken.None));

        // The operator's file was not overwritten and the pointer did not move.
        Assert.Equal("not ours", await File.ReadAllTextAsync(Path.Combine(targetRoot, "someones-album.flac")));
        Assert.Equal(Path.GetFullPath(sourceRoot), NewStore(dataRoot).LibraryRoot);
    }

    [Fact]
    public async Task A_move_to_a_protected_folder_is_refused_without_touching_it()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var (sha, bytes) = area.WriteAudio(sourceRoot, "music/ab/cd", "track-three");

        await using var db = database.CreateContext();
        await SeedAssetAsync(db, sha, bytes.Length, $"music/ab/cd/{sha}.mp3");

        var systemRoot = DangerousSystemRoot();
        var protectedTarget = Path.Combine(systemRoot, "MusicHubRelocationProbe");

        var service = await NewServiceAsync(db, dataRoot, sourceRoot);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.RunAsync(Guid.NewGuid(), protectedTarget, CancellationToken.None));
        Assert.Contains("path.not.system", error.Message, StringComparison.Ordinal);

        Assert.False(Directory.Exists(protectedTarget));
        Assert.Equal(Path.GetFullPath(sourceRoot), NewStore(dataRoot).LibraryRoot);
    }

    [Fact]
    public async Task A_failed_move_can_be_rerun_against_the_same_target_and_repairs_the_partial_copy()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var targetRoot = area.Folder("target");

        var (sha, bytes) = area.WriteAudio(sourceRoot, "music/ab/cd", "track-four");
        var storagePath = $"music/ab/cd/{sha}.mp3";
        var copiedPath = Path.Combine(targetRoot, "music", "ab", "cd", sha + ".mp3");

        await using var db = database.CreateContext();
        await SeedAssetAsync(db, sha, bytes.Length, storagePath);

        // A phantom asset makes the first run fail during verification, after the real file was
        // already copied. The pointer must still name the old library.
        var phantom = new string('d', 64);
        await SeedAssetAsync(db, phantom, 128, $"music/dd/ee/{phantom}.mp3");

        var service = await NewServiceAsync(db, dataRoot, sourceRoot);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.RunAsync(Guid.NewGuid(), targetRoot, CancellationToken.None));
        Assert.Equal(Path.GetFullPath(sourceRoot), NewStore(dataRoot).LibraryRoot);

        // The partial copy is left behind, and one of its files is damaged in the meantime.
        Assert.True(File.Exists(copiedPath));
        await File.WriteAllBytesAsync(copiedPath, "truncated"u8.ToArray());

        // With the phantom row gone, the same target is accepted and the damaged file is replaced
        // from the source rather than trusted.
        db.AudioAssets.RemoveRange(db.AudioAssets.Where(x => x.StoragePath == $"music/dd/ee/{phantom}.mp3"));
        await db.SaveChangesAsync();

        var second = await service.RunAsync(Guid.NewGuid(), targetRoot, CancellationToken.None);
        Assert.Equal(Path.GetFullPath(targetRoot), second.Target);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(copiedPath));
        Assert.Equal(Path.GetFullPath(targetRoot), NewStore(dataRoot).LibraryRoot);
    }

    [Fact]
    public async Task Two_concurrent_requests_cannot_both_queue_a_move()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");
        var firstTarget = area.Folder("target-one");
        var secondTarget = area.Folder("target-two");

        await using var db = database.CreateContext();
        var store = NewStore(dataRoot);
        await store.WriteAsync(sourceRoot, CancellationToken.None);

        // Two operators, two tabs, one tap each. Both requests would pass a check-then-insert if the
        // rule were not enforced by the database, and two simultaneous copies of a whole library is
        // exactly what "one move at a time" is supposed to prevent.
        async Task<bool> QueueAsync(string target)
        {
            await using var own = database.CreateContext();
            var admin = new StorageAdminService(own, store);
            var result = await admin.TryQueueRelocationAsync(target, CancellationToken.None);
            return result.Queued;
        }

        var results = await Task.WhenAll(QueueAsync(firstTarget), QueueAsync(secondTarget));

        Assert.Single(results, queued => queued);
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.BackgroundJobs.CountAsync(
            x => x.Type == LibraryRelocationService.JobType && x.Status == JobStatus.Pending));
    }

    [Fact]
    public async Task A_move_to_the_current_library_is_refused()
    {
        await database.ResetAsync();
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");

        await using var db = database.CreateContext();
        var service = await NewServiceAsync(db, dataRoot, sourceRoot);

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.RunAsync(Guid.NewGuid(), sourceRoot, CancellationToken.None));
        Assert.Contains("path.not.current", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_store_defaults_to_the_data_root_and_picks_up_a_written_pointer()
    {
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var relocated = area.Folder("elsewhere");

        var store = NewStore(dataRoot);
        Assert.Equal(Path.GetFullPath(dataRoot), store.LibraryRoot);
        Assert.Equal("default", store.Source);
        Assert.Null(store.PointerError);

        await store.WriteAsync(relocated, CancellationToken.None);

        // No Invalidate call: the change is picked up from the file, which is what lets the API
        // follow a move performed by the worker without a restart.
        Assert.Equal(Path.GetFullPath(relocated), store.LibraryRoot);
        Assert.Equal("relocated", store.Source);

        // A second, independently constructed store sees the same thing.
        Assert.Equal(Path.GetFullPath(relocated), NewStore(dataRoot).LibraryRoot);
    }

    [Fact]
    public async Task Writing_the_data_root_as_the_library_removes_the_pointer()
    {
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var relocated = area.Folder("elsewhere");

        var store = NewStore(dataRoot);
        await store.WriteAsync(relocated, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(dataRoot, LibraryLocationStore.PointerFileName)));

        await store.WriteAsync(dataRoot, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(dataRoot, LibraryLocationStore.PointerFileName)));
        Assert.Equal(Path.GetFullPath(dataRoot), store.LibraryRoot);
        Assert.Equal("default", store.Source);
    }

    [Fact]
    public async Task A_corrupt_pointer_is_reported_instead_of_silently_ignored()
    {
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        await File.WriteAllTextAsync(
            Path.Combine(dataRoot, LibraryLocationStore.PointerFileName), "{ this is not json");

        var store = NewStore(dataRoot);
        Assert.Equal(Path.GetFullPath(dataRoot), store.LibraryRoot);
        Assert.Equal("default", store.Source);
        Assert.NotNull(store.PointerError);
    }

    [Fact]
    public void The_validator_never_touches_a_folder_that_failed_a_safety_check()
    {
        using var area = new TempArea();
        var dataRoot = area.Folder("data");
        var sourceRoot = area.Folder("source");

        var systemRoot = DangerousSystemRoot();
        var candidate = Path.Combine(systemRoot, "MusicHubValidatorProbe");

        var checks = StorageLocationValidator.Validate(candidate, sourceRoot, dataRoot, requireEmpty: true);

        Assert.Contains(checks, x => x.Id == "path.not.system" && !x.Ok);
        Assert.DoesNotContain(checks, x => x.Id == "path.creatable");
        Assert.DoesNotContain(checks, x => x.Id == "path.writable");
        Assert.False(Directory.Exists(candidate));
    }

    private static string DangerousSystemRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windows)) return windows;
        }

        foreach (var root in new[] { "/etc", "/usr", "/var" })
        {
            if (Directory.Exists(root)) return root;
        }

        return "/etc";
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static LibraryLocationStore NewStore(string dataRoot) =>
        new(Options.Create(new StorageOptions { RootPath = dataRoot }),
            NullLogger<LibraryLocationStore>.Instance);

    /// <summary>
    /// Builds a service whose library is <paramref name="sourceRoot"/>, exactly as production does
    /// it: the pointer is what tells the service where the library is.
    /// </summary>
    private static async Task<LibraryRelocationService> NewServiceAsync(
        MusicHubDbContext db, string dataRoot, string sourceRoot)
    {
        var store = NewStore(dataRoot);
        await store.WriteAsync(sourceRoot, CancellationToken.None);
        return new LibraryRelocationService(
            db, store, Options.Create(new StorageOptions { RootPath = dataRoot }),
            NullLogger<LibraryRelocationService>.Instance);
    }

    /// <summary>
    /// Inserts a hash and an asset row whose content-addressed path is derived from the hash, which
    /// is the same shape the importer writes.
    /// </summary>
    private static async Task SeedAssetAsync(MusicHubDbContext db, string sha, long size, string storagePath)
    {
        var hash = new FileHash { Sha256 = sha, SizeBytes = size };
        db.FileHashes.Add(hash);
        db.AudioAssets.Add(new AudioAsset
        {
            FileHashId = hash.Id,
            OriginalFileName = "fixture.mp3",
            Extension = ".mp3",
            MimeType = "audio/mpeg",
            StoragePath = storagePath,
            SizeBytes = size
        });
        await db.SaveChangesAsync();
    }

    private sealed class TempArea : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "musichub-relocation-" + Guid.NewGuid().ToString("N")[..12]);

        public TempArea() => Directory.CreateDirectory(_root);

        public string Folder(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Writes a small real file whose name is its own SHA-256, as the store does.</summary>
        public (string Sha, byte[] Bytes) WriteAudio(string root, string area, string label)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("musichub-fixture-" + label + "-" + Guid.NewGuid().ToString("N"));
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var directory = Path.Combine(root, area.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, sha + ".mp3"), bytes);
            return (sha, bytes);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp folder must not fail an otherwise valid test run.
            }
        }
    }
}
