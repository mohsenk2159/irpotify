namespace MusicHub.Domain;

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Contributor = "Contributor";
    public const string Listener = "Listener";

    public static readonly string[] All = [Administrator, Contributor, Listener];
}

public enum UploadStatus
{
    Pending,
    Validating,
    Stored,
    Duplicate,
    Rejected,
    Failed
}

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    DeadLetter
}

public enum TelegramPublishStatus
{
    Pending,
    Publishing,
    Published,
    Failed,
    DeadLetter
}

public enum IntegrityCheckStatus
{
    Running,
    Completed,
    Failed
}

public enum Severity
{
    Informational,
    Low,
    Medium,
    High,
    Critical
}
