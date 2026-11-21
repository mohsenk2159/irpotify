using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Infrastructure.Media;

public sealed record StoredUploadResult(string Sha256, long SizeBytes, string QuarantinePath);

/// <summary>Outcome of committing a quarantined file into the content-addressed store.</summary>
/// <param name="RelativePath">Store-relative path of the committed file.</param>
/// <param name="CreatedFile">
/// True only when this call moved the file into the store. False means the bytes were already
/// present, so a rollback must not delete them.
/// </param>
public sealed record CommitResult(string RelativePath, bool CreatedFile);

public sealed class ContentAddressedStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"
    };

    private readonly StorageOptions _options;
    private readonly LibraryLocationStore _locations;

    public ContentAddressedStorage(IOptions<StorageOptions> options, LibraryLocationStore locations)
    {
        _options = options.Value;
        _locations = locations;
    }

    /// <summary>
    /// The folder that currently holds the library. It is read from the location store on every
    /// access instead of being captured once, so a relocation performed by the worker takes effect
    /// in this process without a restart. When the pointer has not moved this is the data root,
    /// which is what every existing installation uses.
    /// </summary>
    public string RootPath => Path.GetFullPath(_locations.LibraryRoot, AppContext.BaseDirectory);
    public string MusicRoot => Path.Combine(RootPath, "music");
    public string QuarantineRoot => Path.Combine(RootPath, "quarantine");
    public string ArtworkRoot => Path.Combine(RootPath, "artwork");
    public long MaxUploadBytes => _options.MaxUploadBytes;

    public bool IsAllowedExtension(string extension) => AllowedExtensions.Contains(extension);

    public static string SanitizeExtension(string? fileNameOrExtension)
    {
        var extension = fileNameOrExtension ?? string.Empty;
        if (extension.Contains('.') && extension.LastIndexOf('.') > 0)
        {
            extension = Path.GetExtension(extension);
        }
        if (!extension.StartsWith('.')) extension = "." + extension;
        extension = extension.ToLowerInvariant();
        return AllowedExtensions.Contains(extension) ? extension : string.Empty;
    }

    public static string SafeOriginalFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "upload";
        var name = Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name.Length > 240 ? name[^240..] : name;
    }

    public async Task<StoredUploadResult> ReceiveToQuarantineAsync(Stream input, string extension, long maximumBytes, CancellationToken cancellationToken)
    {
        if (!AllowedExtensions.Contains(extension))
        {
            throw new UnsupportedMediaException("Unsupported audio extension.");
        }

        var id = Guid.NewGuid().ToString("N");
        var quarantineRoot = QuarantineRoot;
        Directory.CreateDirectory(quarantineRoot);
        var path = Path.Combine(quarantineRoot, id + ".part");
        long total = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[1024 * 128];
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes || total > _options.MaxUploadBytes)
                {
                    await output.DisposeAsync();
                    TryDelete(path);
                    throw new UploadTooLargeException($"Upload exceeds the configured limit of {Math.Min(maximumBytes, _options.MaxUploadBytes)} bytes.");
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        if (total == 0)
        {
            TryDelete(path);
            throw new InvalidAudioException("The uploaded file is empty.");
        }

        return new StoredUploadResult(Convert.ToHexStringLower(hash.GetHashAndReset()), total, path);
    }

    public string RelativeMusicPath(string sha256, string extension)
    {
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid SHA-256.", nameof(sha256));
        extension = SanitizeExtension(extension);
        if (extension.Length == 0) throw new UnsupportedMediaException("Unsupported audio extension.");
        return Path.Combine("music", sha256[..2], sha256[2..4], sha256 + extension).Replace('\\', '/');
    }

    public string ResolveMusicPath(string relativePath)
    {
        var root = RootPath;
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Storage path escaped the configured root.");
        }
        return full;
    }

    public string ResolveArtworkPath(string relativePath)
    {
        var artworkRoot = ArtworkRoot;
        var rootWithSeparator = artworkRoot.EndsWith(Path.DirectorySeparatorChar) ? artworkRoot : artworkRoot + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(artworkRoot, Path.GetFileName(relativePath)));
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Artwork path escaped the configured root.");
        }
        return full;
    }

    /// <summary>
    /// Moves a quarantined file into the content-addressed store and verifies its hash.
    /// Returns the relative path plus whether this call actually created the stored file, so a
    /// caller that later rolls back can delete only a file it is responsible for and never one
    /// that already existed (CR-024).
    /// </summary>
    public async Task<CommitResult> CommitAsync(string quarantinePath, string sha256, string extension, string originalFileName, CancellationToken cancellationToken)
    {
        var relative = RelativeMusicPath(sha256, extension);
        var destination = ResolveMusicPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var created = false;
        if (!File.Exists(destination))
        {
            try
            {
                File.Move(quarantinePath, destination, overwrite: false);
                created = true;
            }
            catch (IOException) when (File.Exists(destination))
            {
                TryDelete(quarantinePath);
            }
        }
        else
        {
            TryDelete(quarantinePath);
        }

        var actual = await ComputeFileSha256Async(destination, cancellationToken);
        if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
        {
            // A file whose content does not match its name is never safe to keep, whoever wrote it.
            TryDelete(destination);
            throw new InvalidAudioException("Stored file hash verification failed.");
        }

        return new CommitResult(relative, created);
    }

    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    public async Task VerifySignatureAsync(string path, string extension, CancellationToken cancellationToken)
    {
        var header = new byte[16];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var read = await stream.ReadAsync(header, cancellationToken);
        if (read < 4 || !HasValidSignature(header.AsSpan(0, read), extension))
        {
            throw new InvalidAudioException("The file signature does not match a supported audio format.");
        }
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> header, string extension)
    {
        return extension switch
        {
            ".mp3" => header.Length >= 3 && ((header[0] == 'I' && header[1] == 'D' && header[2] == '3') ||
                                             (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
            ".flac" => header.Length >= 4 && header[..4].SequenceEqual("fLaC"u8),
            ".wav" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8),
            ".m4a" => header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8),
            ".aac" => header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xF6) == 0xF0,
            ".ogg" or ".opus" => header.Length >= 4 && header[..4].SequenceEqual("OggS"u8),
            _ => false
        };
    }

    public static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup is best effort; the integrity scanner reports leftovers.
        }
    }
}

public class MediaValidationException(string message) : Exception(message);
public sealed class UnsupportedMediaException(string message) : MediaValidationException(message);
public sealed class InvalidAudioException(string message) : MediaValidationException(message);
public sealed class UploadTooLargeException(string message) : MediaValidationException(message);
