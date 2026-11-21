using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MusicHub.Api.Endpoints;
using MusicHub.Api.Realtime;
using MusicHub.Infrastructure.Options;
using MusicHub.Infrastructure.Security;

namespace MusicHub.Tests.Unit;

/// <summary>Unit tests for the password hashing primitives (PBKDF2-SHA256).</summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesVersionedPbkdf2Format_AndVerifies()
    {
        var encoded = PasswordHasher.Hash("Correct-Horse-Battery-9");

        var parts = encoded.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal(210_000, int.Parse(parts[1]));
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);

        Assert.True(PasswordHasher.Verify("Correct-Horse-Battery-9", encoded));
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("Correct-Horse-Battery-90")]
    [InlineData("correct-horse-battery-9")]
    [InlineData("")]
    public void Verify_RejectsWrongPassword(string candidate)
    {
        var encoded = PasswordHasher.Hash("Correct-Horse-Battery-9");
        Assert.False(PasswordHasher.Verify(candidate, encoded));
    }

    [Fact]
    public void Hash_UsesRandomSalt_SoIdenticalPasswordsDiffer()
    {
        var first = PasswordHasher.Hash("Same-Password-123");
        var second = PasswordHasher.Hash("Same-Password-123");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("Same-Password-123", first));
        Assert.True(PasswordHasher.Verify("Same-Password-123", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-not-a-hash")]
    [InlineData("pbkdf2-sha256$not-a-number$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$1000$not-base64$not-base64")]
    [InlineData("bcrypt$210000$c2FsdA==$a2V5")]
    public void Verify_RejectsMalformedHashes_WithoutThrowing(string encoded)
    {
        Assert.False(PasswordHasher.Verify("anything", encoded));
    }

    [Fact]
    public void Verify_HonoursTheStoredIterationCount()
    {
        // A hash produced with a different iteration count must still verify against its own
        // parameters (rotation support), and must not verify a truncated key.
        const int iterations = 1000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2("rotation-password", salt, iterations, HashAlgorithmName.SHA256, 32);
        var encoded = $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        Assert.True(PasswordHasher.Verify("rotation-password", encoded));
        Assert.False(PasswordHasher.Verify("rotation-password!", encoded));
    }
}

/// <summary>Unit tests for the SHA-256 helper used to store refresh tokens.</summary>
public sealed class RefreshTokenHashingTests
{
    [Fact]
    public void Sha256Hex_MatchesTheSha256OfTheUtf8Bytes()
    {
        const string token = "opaque-refresh-token-value";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        var actual = CryptoUtil.Sha256Hex(token);

        Assert.Equal(expected, actual);
        Assert.Equal(64, actual.Length);
        Assert.Equal(actual.ToLowerInvariant(), actual);
    }

    [Fact]
    public void Sha256Hex_IsDeterministic_AndDifferentForDifferentTokens()
    {
        Assert.Equal(CryptoUtil.Sha256Hex("token-a"), CryptoUtil.Sha256Hex("token-a"));
        Assert.NotEqual(CryptoUtil.Sha256Hex("token-a"), CryptoUtil.Sha256Hex("token-b"));
    }

    [Fact]
    public void NewOpaqueToken_IsUrlSafe_AndNeverEqualsTheStoredHash()
    {
        var first = CryptoUtil.NewOpaqueToken(48);
        var second = CryptoUtil.NewOpaqueToken(48);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);

        var stored = CryptoUtil.Sha256Hex(first);
        Assert.NotEqual(first, stored);
        Assert.Equal(64, stored.Length);
    }

    [Fact]
    public void FixedTimeEquals_ComparesContent_NotReference()
    {
        Assert.True(CryptoUtil.FixedTimeEquals("bootstrap-token", "bootstrap-token"));
        Assert.False(CryptoUtil.FixedTimeEquals("bootstrap-token", "bootstrap-tokeN"));
        Assert.False(CryptoUtil.FixedTimeEquals("short", "longer-value"));
    }
}

/// <summary>Unit tests for the HMAC stream-token signing scheme.</summary>
public sealed class StreamTokenServiceTests
{
    private static StreamTokenService CreateService(string secret = "unit-test-secret-0123456789abcdef0123456789")
        => new(Options.Create(new AuthOptions { JwtSecret = secret }));

    [Fact]
    public void Create_ThenValidate_RoundTrips_ForTheSameTrack()
    {
        var service = CreateService();
        var trackId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var (token, expiresAt) = service.Create(trackId, userId, TimeSpan.FromMinutes(10));

        Assert.Contains(':', token);
        Assert.True(expiresAt > DateTimeOffset.UtcNow.AddMinutes(9));
        Assert.True(service.Validate(token, trackId, out var issuer));
        Assert.Equal(userId, issuer);
    }

    [Fact]
    public void Validate_RejectsTokenIssuedForAnotherTrack()
    {
        var service = CreateService();
        var (token, _) = service.Create(Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(10));

        Assert.False(service.Validate(token, Guid.NewGuid(), out var issuer));
        Assert.Equal(Guid.Empty, issuer);
    }

    [Fact]
    public void Validate_RejectsExpiredToken()
    {
        var service = CreateService();
        var trackId = Guid.NewGuid();
        var (token, _) = service.Create(trackId, Guid.NewGuid(), TimeSpan.FromSeconds(-5));

        Assert.False(service.Validate(token, trackId, out _));
    }

    [Fact]
    public void Validate_RejectsTamperedPayload_AndTamperedSignature()
    {
        var service = CreateService();
        var trackId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (token, _) = service.Create(trackId, userId, TimeSpan.FromMinutes(10));

        var attackerTrack = Guid.NewGuid().ToString("N");
        var payload = token[..token.LastIndexOf('.')];
        var signature = token[(token.LastIndexOf('.') + 1)..];
        var forgedPayload = payload.Replace(trackId.ToString("N"), attackerTrack);
        Assert.False(service.Validate($"{forgedPayload}.{signature}", Guid.Parse(attackerTrack), out _));

        var flipped = signature[..^1] + (signature[^1] == 'A' ? 'B' : 'A');
        Assert.False(service.Validate($"{payload}.{flipped}", trackId, out _));

        // Extending the expiry must invalidate the signature as well.
        var parts = payload.Split(':');
        var extended = $"{parts[0]}:{long.Parse(parts[1]) + 3600}:{parts[2]}";
        Assert.False(service.Validate($"{extended}.{signature}", trackId, out _));

        // Substituting the charged user must invalidate the signature too, otherwise a client
        // could shift its own listening time onto another account.
        var swappedUser = $"{parts[0]}:{parts[1]}:{Guid.NewGuid():N}";
        Assert.False(service.Validate($"{swappedUser}.{signature}", trackId, out _));
    }

    [Fact]
    public void Validate_RejectsGarbage_AndTokensFromAnotherSecret()
    {
        var service = CreateService();
        var trackId = Guid.NewGuid();

        Assert.False(service.Validate(string.Empty, trackId, out _));
        Assert.False(service.Validate("no-dot-here", trackId, out _));
        Assert.False(service.Validate($"{Guid.NewGuid():N}:9999999999:abcdef.xyz", trackId, out _));

        var foreign = CreateService("another-secret-fedcba9876543210fedcba9876543210");
        var (foreignToken, _) = foreign.Create(trackId, Guid.NewGuid(), TimeSpan.FromMinutes(10));
        Assert.False(service.Validate(foreignToken, trackId, out _));
    }
}

/// <summary>
/// Unit tests for the server-measured listening time that closes B-023. The property under test is
/// that bytes served on the stream endpoint cost real listening time even when the client lies.
/// </summary>
public sealed class ListeningTimeTrackerTests
{
    [Fact]
    public void Bytes_To_Seconds_Uses_The_Reported_Bitrate()
    {
        // 128 kbit/s = 16 000 bytes/s, so 160 000 bytes is exactly ten seconds.
        Assert.Equal(10, ListeningTimeTracker.BytesToSeconds(160_000, 128_000, 0, 0));
        Assert.Equal(600, ListeningTimeTracker.BytesToSeconds(9_600_000, 128_000, 0, 0));
    }

    [Fact]
    public void Bytes_To_Seconds_Falls_Back_To_Size_Over_Duration()
    {
        // 1 000 000 bytes over 100 s = 10 000 bytes/s; 50 000 bytes is therefore five seconds.
        Assert.Equal(5, ListeningTimeTracker.BytesToSeconds(50_000, null, 1_000_000, 100));
    }

    [Fact]
    public void Bytes_To_Seconds_Charges_Nothing_When_The_Rate_Is_Unknown()
    {
        // Never guess: charging nothing is the safe direction (the client report still applies).
        Assert.Equal(0, ListeningTimeTracker.BytesToSeconds(50_000, null, 0, 0));
        Assert.Equal(0, ListeningTimeTracker.BytesToSeconds(50_000, 0, 1_000_000, 0));
        Assert.Equal(0, ListeningTimeTracker.BytesToSeconds(0, 128_000, 1_000_000, 100));
    }

    [Fact]
    public void Bytes_To_Seconds_Is_Clamped_To_The_Per_Drain_Bound()
    {
        // A huge range request must not cost more than the documented hour.
        var seconds = ListeningTimeTracker.BytesToSeconds(long.MaxValue / 2, 8_000, 0, 0);
        Assert.Equal(ListeningTimeTracker.MaxSecondsPerDrain, seconds);
    }

    [Fact]
    public void Drain_Accumulates_Then_Resets()
    {
        var tracker = new ListeningTimeTracker();
        var userId = Guid.NewGuid();

        tracker.AddStreamedBytes(userId, 1_000);
        tracker.AddStreamedBytes(userId, 2_000);

        Assert.Equal(3_000, tracker.DrainStreamedBytes(userId));
        // A second drain of the same interval must be empty, otherwise one stream could be charged twice.
        Assert.Equal(0, tracker.DrainStreamedBytes(userId));
    }

    [Fact]
    public void Drain_Is_Per_User()
    {
        var tracker = new ListeningTimeTracker();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        tracker.AddStreamedBytes(first, 500);
        tracker.AddStreamedBytes(second, 700);

        Assert.Equal(500, tracker.DrainStreamedBytes(first));
        Assert.Equal(700, tracker.DrainStreamedBytes(second));
    }

    [Fact]
    public void Anonymous_Or_Empty_Input_Is_Ignored()
    {
        var tracker = new ListeningTimeTracker();

        tracker.AddStreamedBytes(Guid.Empty, 1_000);
        tracker.AddStreamedBytes(Guid.NewGuid(), 0);
        tracker.AddStreamedBytes(Guid.NewGuid(), -5);

        Assert.Equal(0, tracker.DrainStreamedBytes(Guid.Empty));
    }

    [Fact]
    public void Prune_Drops_Only_Idle_Empty_Entries()
    {
        var tracker = new ListeningTimeTracker();
        var userId = Guid.NewGuid();
        tracker.AddStreamedBytes(userId, 1_000);

        // Activity is recent, so nothing is eligible yet.
        Assert.Equal(0, tracker.Prune());
        Assert.Equal(1_000, tracker.DrainStreamedBytes(userId));

        // Drained and still recent -> kept until the lifetime elapses.
        Assert.Equal(0, tracker.Prune());
    }
}
