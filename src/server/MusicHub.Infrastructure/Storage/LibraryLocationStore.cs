using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Infrastructure.Storage;

/// <summary>
/// Resolves the folder that holds the music library and remembers where an administrator moved it.
/// </summary>
/// <remarks>
/// <para>
/// There are two roots and they are not interchangeable. The <b>data root</b>
/// (<see cref="StorageOptions.RootPath"/>) is resolved before the host can reach PostgreSQL,
/// because the database credential and the data-protection keyring that unprotects it live under
/// it. It therefore cannot move at runtime, and nothing an administrator does in the product may
/// change it.
/// </para>
/// <para>
/// The <b>library root</b> holds only <c>music/</c>, <c>artwork/</c> and <c>quarantine/</c>. It is
/// recorded in <c>&lt;data root&gt;/library-location.json</c>, which is a file the data root always
/// contains, so the pointer is findable without a database connection and survives the move it
/// describes. When the file is absent the library root is the data root, which is exactly what
/// every installation written before this feature expects.
/// </para>
/// <para>
/// The pointer is re-read whenever its timestamp or length changes, so the API and the Worker pick
/// a new location up without a restart once the relocation worker flips it.
/// </para>
/// </remarks>
public sealed class LibraryLocationStore
{
    public const string PointerFileName = "library-location.json";
    private const int PointerVersion = 1;

    private readonly string _dataRoot;
    private readonly ILogger<LibraryLocationStore> _logger;
    private readonly object _gate = new();

    private string? _cachedLibraryRoot;
    private string _cachedSource = "default";
    private string? _cachedError;
    private DateTime _cachedStamp;
    private long _cachedLength = -1;

    public LibraryLocationStore(IOptions<StorageOptions> options, ILogger<LibraryLocationStore> logger)
    {
        _logger = logger;
        _dataRoot = Path.GetFullPath(options.Value.RootPath, AppContext.BaseDirectory);
        PointerPath = Path.Combine(_dataRoot, PointerFileName);
    }

    /// <summary>The immutable root that holds the keys, the credential and the pointer itself.</summary>
    public string DataRoot => _dataRoot;

    public string PointerPath { get; }

    /// <summary>
    /// The folder the library currently lives in. Falls back to the data root when no pointer has
    /// been written, or when the pointer cannot be read (which is reported through
    /// <see cref="PointerError"/> instead of being hidden).
    /// </summary>
    public string LibraryRoot => Read().Root;

    /// <summary><c>relocated</c> when a pointer is in effect, otherwise <c>default</c>.</summary>
    public string Source => Read().Source;

    /// <summary>Why the pointer could not be used, or null when it is absent or valid.</summary>
    public string? PointerError => Read().Error;

    /// <summary>Forces the next read to hit the disk. Used by tests and by callers that just wrote.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cachedLibraryRoot = null;
            _cachedStamp = default;
            _cachedLength = -1;
        }
    }

    /// <summary>
    /// Points the library at <paramref name="libraryRoot"/>. The file is written beside the real one
    /// and then replaces it in a single step, so a crash mid-write can never leave a truncated
    /// pointer that would silently send the library back to the data root.
    /// </summary>
    public async Task WriteAsync(string libraryRoot, CancellationToken ct)
    {
        var full = Path.GetFullPath(libraryRoot);
        if (string.Equals(full, _dataRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Nothing to remember: the data root is the documented default.
            if (File.Exists(PointerPath)) File.Delete(PointerPath);
            Invalidate();
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            version = PointerVersion,
            libraryRoot = full,
            updatedAt = DateTimeOffset.UtcNow
        });

        var temporary = PointerPath + ".tmp";
        await File.WriteAllTextAsync(temporary, payload, ct);
        File.Move(temporary, PointerPath, overwrite: true);
        Invalidate();
        _logger.LogInformation("The library location pointer now names {Root}.", full);
    }

    private (string Root, string Source, string? Error) Read()
    {
        FileInfo info;
        try
        {
            info = new FileInfo(PointerPath);
            if (!info.Exists)
            {
                lock (_gate)
                {
                    _cachedLibraryRoot = _dataRoot;
                    _cachedSource = "default";
                    _cachedError = null;
                    _cachedStamp = default;
                    _cachedLength = -1;
                }
                return (_dataRoot, "default", null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (_dataRoot, "default", "The library location pointer could not be inspected: " + ex.Message);
        }

        lock (_gate)
        {
            if (_cachedLibraryRoot is not null
                && _cachedStamp == info.LastWriteTimeUtc
                && _cachedLength == info.Length)
            {
                return (_cachedLibraryRoot, _cachedSource, _cachedError);
            }
        }

        string root;
        string source;
        string? error;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(PointerPath));
            var value = document.RootElement.TryGetProperty("libraryRoot", out var element)
                        && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            {
                root = _dataRoot;
                source = "default";
                error = "The library location pointer does not name an absolute path; the data root is in use.";
            }
            else
            {
                root = Path.GetFullPath(value);
                source = "relocated";
                error = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Never pretend the library is where it used to be: the operator has to see this.
            root = _dataRoot;
            source = "default";
            error = "The library location pointer could not be read: " + ex.Message;
            _logger.LogWarning(ex, "The library location pointer at {Path} is unreadable.", PointerPath);
        }

        lock (_gate)
        {
            _cachedLibraryRoot = root;
            _cachedSource = source;
            _cachedError = error;
            _cachedStamp = info.LastWriteTimeUtc;
            _cachedLength = info.Length;
        }

        return (root, source, error);
    }
}
