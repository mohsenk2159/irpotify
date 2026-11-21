using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Infrastructure.Media;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Storage;

namespace MusicHub.Tests.Unit;

/// <summary>
/// Unit tests for content-addressed path building and for the storage-root escape guard. These
/// are the security-critical rules that stop a crafted path/database value from reading or
/// writing outside the configured data root.
/// </summary>
public sealed class StoragePathTests : IDisposable
{
    private const string ValidSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly string _root;
    private readonly ContentAddressedStorage _storage;

    public StoragePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "musichub-unit-storage-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
        _storage = new ContentAddressedStorage(
            Options.Create(new StorageOptions { RootPath = _root }),
            new LibraryLocationStore(Options.Create(new StorageOptions { RootPath = _root }), NullLogger<LibraryLocationStore>.Instance));
    }

    [Fact]
    public void RelativeMusicPath_IsContentAddressed_AndUsesForwardSlashes()
    {
        var relative = _storage.RelativeMusicPath(ValidSha, ".wav");

        Assert.Equal($"music/{ValidSha[..2]}/{ValidSha[2..4]}/{ValidSha}.wav", relative);
        Assert.DoesNotContain('\\', relative);

        var resolved = _storage.ResolveMusicPath(relative);
        Assert.StartsWith(Path.GetFullPath(_root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(ValidSha + ".wav", resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzz6789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    public void RelativeMusicPath_RejectsInvalidSha256(string sha)
    {
        Assert.Throws<ArgumentException>(() => _storage.RelativeMusicPath(sha, ".wav"));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".txt")]
    [InlineData("")]
    public void RelativeMusicPath_RejectsUnsupportedExtension(string extension)
    {
        Assert.Throws<UnsupportedMediaException>(() => _storage.RelativeMusicPath(ValidSha, extension));
    }

    [Theory]
    [InlineData("music/../../../etc/passwd")]
    [InlineData("../outside.wav")]
    [InlineData("music/../../outside.wav")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("music/./../../escape.wav")]
    public void ResolveMusicPath_RejectsTraversalOutsideTheRoot(string hostilePath)
    {
        Assert.Throws<UnauthorizedAccessException>(() => _storage.ResolveMusicPath(hostilePath));
    }

    [Fact]
    public void ResolveMusicPath_AcceptsAPathInsideTheRoot()
    {
        var relative = _storage.RelativeMusicPath(ValidSha, ".flac");
        var resolved = _storage.ResolveMusicPath(relative);

        var rootWithSeparator = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.StartsWith(rootWithSeparator, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveArtworkPath_StripsDirectories_AndStaysInsideTheArtworkRoot()
    {
        var resolved = _storage.ResolveArtworkPath("../../evil.jpg");
        var artworkRoot = Path.GetFullPath(_storage.ArtworkRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.StartsWith(artworkRoot, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("evil.jpg", Path.GetFileName(resolved));
    }

    [Theory]
    [InlineData("track.MP3", ".mp3")]
    [InlineData("track.mp3", ".mp3")]
    [InlineData("mp3", ".mp3")]
    [InlineData("../../evil.flac", ".flac")]
    [InlineData("track.exe", "")]
    [InlineData("track", "")]
    public void SanitizeExtension_NormalizesAndWhitelists(string input, string expected)
    {
        Assert.Equal(expected, ContentAddressedStorage.SanitizeExtension(input));
    }

    [Theory]
    [InlineData("..\\..\\windows\\system32\\evil.mp3")]
    [InlineData("/etc/cron.d/evil.mp3")]
    [InlineData("C:\\temp\\evil.mp3")]
    public void SafeOriginalFileName_RemovesDirectories(string hostileName)
    {
        var safe = ContentAddressedStorage.SafeOriginalFileName(hostileName);

        Assert.DoesNotContain("..", safe);
        Assert.DoesNotContain('\\', safe);
        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain(':', safe);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // best effort
        }
    }
}

/// <summary>
/// Unit tests for the magic-byte validation that gates every uploaded file before FFprobe runs.
/// </summary>
public sealed class AudioSignatureTests : IDisposable
{
    private readonly string _root;
    private readonly ContentAddressedStorage _storage;

    public AudioSignatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "musichub-unit-signature-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
        _storage = new ContentAddressedStorage(
            Options.Create(new StorageOptions { RootPath = _root }),
            new LibraryLocationStore(Options.Create(new StorageOptions { RootPath = _root }), NullLogger<LibraryLocationStore>.Instance));
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Header(string text, int totalLength = 64)
    {
        var buffer = new byte[totalLength];
        var bytes = System.Text.Encoding.ASCII.GetBytes(text);
        bytes.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public async Task Wav_RiffWaveHeader_IsAccepted()
    {
        var header = new byte[64];
        "RIFF"u8.CopyTo(header);
        BitConverter.GetBytes(36).CopyTo(header, 4);
        "WAVE"u8.CopyTo(header.AsSpan(8));
        var path = Write("ok.wav", header);

        await _storage.VerifySignatureAsync(path, ".wav", CancellationToken.None);
    }

    [Fact]
    public async Task Flac_And_Ogg_And_M4a_Headers_AreAccepted()
    {
        await _storage.VerifySignatureAsync(Write("ok.flac", Header("fLaC")), ".flac", CancellationToken.None);
        await _storage.VerifySignatureAsync(Write("ok.ogg", Header("OggS")), ".ogg", CancellationToken.None);
        await _storage.VerifySignatureAsync(Write("ok.opus", Header("OggS")), ".opus", CancellationToken.None);

        var m4a = new byte[64];
        "ftyp"u8.CopyTo(m4a.AsSpan(4));
        await _storage.VerifySignatureAsync(Write("ok.m4a", m4a), ".m4a", CancellationToken.None);
    }

    [Fact]
    public async Task Mp3_AcceptsId3TagOrFrameSync()
    {
        await _storage.VerifySignatureAsync(Write("id3.mp3", Header("ID3")), ".mp3", CancellationToken.None);

        var frameSync = new byte[64];
        frameSync[0] = 0xFF;
        frameSync[1] = 0xFB;
        await _storage.VerifySignatureAsync(Write("sync.mp3", frameSync), ".mp3", CancellationToken.None);
    }

    [Fact]
    public async Task RandomBytesWithMp3Extension_AreRejected()
    {
        var random = new byte[512];
        Random.Shared.NextBytes(random);
        random[0] = 0x00;
        var path = Write("random.mp3", random);

        await Assert.ThrowsAsync<InvalidAudioException>(
            () => _storage.VerifySignatureAsync(path, ".mp3", CancellationToken.None));
    }

    [Fact]
    public async Task ExecutableHeaderWithMp3Extension_IsRejected()
    {
        var executable = new byte[256];
        "MZ"u8.CopyTo(executable);
        executable[2] = 0x90;
        executable[3] = 0x00;
        var path = Write("payload.mp3", executable);

        await Assert.ThrowsAsync<InvalidAudioException>(
            () => _storage.VerifySignatureAsync(path, ".mp3", CancellationToken.None));
    }

    [Fact]
    public async Task SignatureMustMatchTheDeclaredExtension()
    {
        var wavBytes = new byte[64];
        "RIFF"u8.CopyTo(wavBytes);
        "WAVE"u8.CopyTo(wavBytes.AsSpan(8));
        var path = Write("actually-wav.flac", wavBytes);

        await Assert.ThrowsAsync<InvalidAudioException>(
            () => _storage.VerifySignatureAsync(path, ".flac", CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedFile_IsRejected()
    {
        var path = Write("short.mp3", [0xFF]);

        await Assert.ThrowsAsync<InvalidAudioException>(
            () => _storage.VerifySignatureAsync(path, ".mp3", CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // best effort
        }
    }
}
