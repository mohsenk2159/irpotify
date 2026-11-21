using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicHub.Infrastructure.Options;

namespace MusicHub.Infrastructure.Media;

public sealed record AudioMetadata(
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    string Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    double DurationSeconds,
    long? Bitrate,
    int? SampleRate,
    int? Channels,
    string Codec);

public sealed class AudioProbeService(
    IOptions<StorageOptions> options,
    ILogger<AudioProbeService> logger)
{
    private readonly StorageOptions _options = options.Value;

    public async Task<AudioMetadata> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var output = await RunAsync(_options.FfprobePath ?? "ffprobe",
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
            cancellationToken);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidAudioException("FFprobe returned no media streams.");
        }

        JsonElement? audio = null;
        foreach (var stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio")
            {
                audio = stream;
                break;
            }
        }

        if (audio is null)
        {
            throw new InvalidAudioException("The file contains no audio stream.");
        }

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("tags", out var formatTags))
        {
            ReadTags(formatTags, tags);
        }
        if (audio.Value.TryGetProperty("tags", out var streamTags))
        {
            ReadTags(streamTags, tags);
        }

        var duration = GetDouble(root, "format", "duration") ?? GetDouble(audio.Value, "duration") ?? 0;
        if (duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration))
        {
            throw new InvalidAudioException("The audio duration could not be determined.");
        }

        var bitRate = GetLong(root, "format", "bit_rate") ?? GetLong(audio.Value, "bit_rate");
        // No title tag means "unknown", not "whatever this file happens to be called on disk".
        // The probe only ever sees the quarantine file ({guid}.part), so a file-name fallback
        // here would title every untagged upload with a random GUID. TrackImportService falls
        // back to the uploader's original file name instead (B-062).
        var title = Tag(tags, "title") ?? string.Empty;
        var artist = Tag(tags, "artist") ?? Tag(tags, "album_artist") ?? "Unknown Artist";
        var album = Tag(tags, "album") ?? string.Empty;
        var albumArtist = Tag(tags, "album_artist") ?? artist;
        var genre = Tag(tags, "genre") ?? string.Empty;
        var year = ParseYear(Tag(tags, "date") ?? Tag(tags, "year"));
        var trackNumber = ParseIndex(Tag(tags, "track"));
        var discNumber = ParseIndex(Tag(tags, "disc"));
        var sampleRate = GetInt(audio.Value, "sample_rate");
        var channels = GetInt(audio.Value, "channels");
        var codec = audio.Value.TryGetProperty("codec_name", out var codecName) ? codecName.GetString() ?? "unknown" : "unknown";

        return new AudioMetadata(
            Truncate(title, 400),
            Truncate(artist, 400),
            Truncate(album, 400),
            Truncate(albumArtist, 400),
            Truncate(genre, 160),
            year,
            trackNumber,
            discNumber,
            Math.Round(duration, 3),
            bitRate,
            sampleRate,
            channels,
            codec);
    }

    public async Task<string?> TryExtractArtworkAsync(string audioPath, Guid trackId, string artworkRoot, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(artworkRoot, trackId.ToString("N") + ".jpg");
        try
        {
            await RunAsync(_options.FfmpegPath ?? "ffmpeg",
                ["-hide_banner", "-loglevel", "error", "-y", "-i", audioPath, "-map", "0:v:0", "-frames:v", "1", "-c:v", "mjpeg", destination],
                cancellationToken);
            var info = new FileInfo(destination);
            if (info.Exists && info.Length is > 0 and < 20 * 1024 * 1024)
            {
                return Path.Combine("artwork", Path.GetFileName(destination)).Replace('\\', '/');
            }
            ContentAddressedStorage.TryDelete(destination);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No extractable embedded artwork for {TrackId}", trackId);
            ContentAddressedStorage.TryDelete(destination);
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(_options.FfprobePath ?? "ffprobe", ["-version"], cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidAudioException("The media analyzer could not be started.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.FfprobeTimeoutSeconds, 5, 300)));
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidAudioException($"Media analysis failed: {Truncate(error.Trim(), 500)}");
            }
            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new InvalidAudioException("Media analysis timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static void ReadTags(JsonElement tags, IDictionary<string, string> target)
    {
        if (tags.ValueKind != JsonValueKind.Object) return;
        foreach (var property in tags.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                target[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }
    }

    private static string? Tag(IReadOnlyDictionary<string, string> tags, string key)
        => tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static double? GetDouble(JsonElement element, string parent, string name)
    {
        if (!element.TryGetProperty(parent, out var p) || !p.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static long? GetLong(JsonElement element, string parent, string name)
    {
        if (!element.TryGetProperty(parent, out var p) || !p.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetInt64();
        return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetInt64();
        return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number) return value.GetInt32();
        return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var first = value.Length >= 4 ? value[..4] : value;
        return int.TryParse(first, out var year) && year is >= 1000 and <= 2200 ? year : null;
    }

    private static int? ParseIndex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var slash = value.IndexOf('/');
        var first = slash > 0 ? value[..slash] : value;
        return int.TryParse(first, out var number) && number > 0 ? number : null;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
