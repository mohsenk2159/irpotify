namespace MusicHub.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "storage";
    public long MaxUploadBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public long DefaultUserQuotaBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public bool RequireMalwareScan { get; set; }
    public string? ClamAvHost { get; set; }
    public int ClamAvPort { get; set; } = 3310;
    public int FfprobeTimeoutSeconds { get; set; } = 45;
    public string? FfprobePath { get; set; }
    public string? FfmpegPath { get; set; }

    /// <summary>
    /// Simultaneous streams allowed per account. Read by the stream endpoint and exposed in the
    /// dashboard; it was previously a settings key with no code path behind it (CR-019).
    /// 0 or less means unlimited.
    /// </summary>
    public int MaxConcurrentStreamsPerUser { get; set; } = 4;
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "MusicHub";
    public string Audience { get; set; } = "MusicHubClients";
    public string JwtSecret { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int MaximumFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int PairingCodeMinutes { get; set; } = 5;
}
