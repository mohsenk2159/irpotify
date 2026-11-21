namespace MusicHub.Domain;

public sealed class UploadSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public long ExpectedSizeBytes { get; set; }
    public long ReceivedBytes { get; set; }
    public string? Sha256 { get; set; }
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public string? QuarantinePath { get; set; }
    public Guid? TrackId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class UploadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UploadSessionId { get; set; }
    public UploadSession UploadSession { get; set; } = null!;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ImportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "telegram";
    public string SourceReference { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public Guid? TrackId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Primary";
    public string RootPath { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StorageIntegrityCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public IntegrityCheckStatus Status { get; set; } = IntegrityCheckStatus.Running;
    public int AssetCount { get; set; }
    public int MissingCount { get; set; }
    public int OrphanCount { get; set; }
    public string? DetailsJson { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class DuplicateReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? ExistingTrackId { get; set; }
    public string IncomingFileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TelegramConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? BotTokenProtected { get; set; }
    public long? AdminTelegramUserId { get; set; }
    public long? AdminChatId { get; set; }
    public long? DestinationGroupId { get; set; }
    public long? DestinationTopicId { get; set; }
    public bool AutoPublishEnabled { get; set; }
    public bool IngestionEnabled { get; set; }
    public long MaxIncomingFileSizeBytes { get; set; } = 20L * 1024 * 1024;
    public int MaxAudioDurationSeconds { get; set; } = 60 * 60;
    public string ApiEndpoint { get; set; } = "https://api.telegram.org";
    public string? LocalBotApiEndpoint { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}

public sealed class TelegramUpdate
{
    public long Id { get; set; }
    public long TelegramUpdateId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class TelegramImport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long? TelegramUpdateId { get; set; }
    public long TelegramUserId { get; set; }
    public long ChatId { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public Guid? TrackId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TelegramPublication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public TelegramPublishStatus Status { get; set; } = TelegramPublishStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public long? TelegramMessageId { get; set; }
    public long? ChatId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TelegramMessageMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackId { get; set; }
    public long ChatId { get; set; }
    public long MessageId { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BackgroundJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? Error { get; set; }
}

public sealed class FailedJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? BackgroundJobId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Error { get; set; } = string.Empty;
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SystemEvent
{
    public long Id { get; set; }
    public string Level { get; set; } = "Information";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BackupHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Status { get; set; } = "Running";
    public string? Sha256 { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class FeatureFlag
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
