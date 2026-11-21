using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MusicHub.Player.Models;

public sealed class TrackItem : INotifyPropertyChanged
{
    private bool _isFavorite;

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public double DurationSeconds { get; set; }
    public int? Year { get; set; }
    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public string? Codec { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FavoriteGlyph)));
        }
    }

    public string? ArtworkUrl { get; set; }

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonIgnore]
    public string DurationText => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
        : "--:--";

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Album) ? Artist : $"{Artist} · {Album}";

    [JsonIgnore]
    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim()[..1].ToUpperInvariant();
}

public sealed class AlbumItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public Guid? ArtistId { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public int TrackCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ArtworkUrl { get; set; }

    [JsonIgnore]
    public string Subtitle => string.Join(" · ", new[] { AlbumArtist, Year?.ToString(), TrackCount > 0 ? $"{TrackCount} tracks" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class ArtistItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public int TrackCount { get; set; }
    public int AlbumCount { get; set; }
    public string? ArtworkUrl { get; set; }

    [JsonIgnore]
    public string Subtitle => string.Join(" · ", new[] { TrackCount > 0 ? $"{TrackCount} tracks" : null, AlbumCount > 0 ? $"{AlbumCount} albums" : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class PlaylistItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerUserId { get; set; }
    public bool IsPublic { get; set; }
    public bool IsCollaborative { get; set; }
    public int TrackCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ArtworkUrl { get; set; }

    [JsonIgnore]
    public string Subtitle => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";
}

public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
}

public sealed class UserProfile
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool IsActive { get; set; }
    public bool CanUsePlayer { get; set; }
    public long QuotaBytes { get; set; }
}

public sealed class AuthResult
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    public UserProfile User { get; set; } = new();
}

public sealed class StreamTokenResult
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public string AbsoluteUrl { get; set; } = string.Empty;
}

public sealed class HomeResult
{
    public List<TrackItem> RecentlyAdded { get; set; } = [];
    public List<TrackItem> RecentlyPlayed { get; set; } = [];
    public List<TrackItem> MostPlayed { get; set; } = [];
    public List<TrackItem> Favorites { get; set; } = [];
    public List<AlbumItem> Albums { get; set; } = [];
}

public sealed class SearchResult
{
    public List<TrackItem> Tracks { get; set; } = [];
    public List<AlbumItem> Albums { get; set; } = [];
    public List<ArtistItem> Artists { get; set; } = [];
    public List<FolderItem> Folders { get; set; } = [];
}

public sealed class FolderItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PlaylistDetail
{
    public PlaylistItem Playlist { get; set; } = new();
    public List<TrackItem> Tracks { get; set; } = [];
}

public sealed class AlbumDetail
{
    public AlbumItem Album { get; set; } = new();
    public List<TrackItem> Tracks { get; set; } = [];
}

public sealed class ArtistDetail
{
    public ArtistItem Artist { get; set; } = new();
    public List<TrackItem> Tracks { get; set; } = [];
    public List<AlbumItem> Albums { get; set; } = [];
}

public sealed class HistoryEntry
{
    public DateTimeOffset LastPlayed { get; set; }
    public int Plays { get; set; }
    public TrackItem Track { get; set; } = new();
}

public sealed class ApiError
{
    public string? Error { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
}

public sealed class SavedSession
{
    public string ServerUrl { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    public UserProfile? User { get; set; }
    public string? Email { get; set; }
}

// ---- Admin: library storage location ----------------------------------------------------

public sealed class StorageCheckItem
{
    public string Id { get; set; } = string.Empty;
    public bool Ok { get; set; }
    public string? Message { get; set; }

    [JsonIgnore] public string Mark => Ok ? "✓" : "✗";
    [JsonIgnore] public string Label => Id.Replace('.', ' ');
    [JsonIgnore] public string Detail => Ok ? "Passed" : Message ?? "Failed";
}

public sealed class StorageAreaItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public int? Files { get; set; }
    public long? Bytes { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public string Summary => Error ?? (Exists
        ? $"{Files ?? 0} file(s) · {StorageSize.Format(Bytes)}"
        : "not created yet");
}

public sealed class DiskSpaceItem
{
    public string Name { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }

    [JsonIgnore] public string FreeText => $"{StorageSize.Format(FreeBytes)} free of {StorageSize.Format(TotalBytes)}";
}

public sealed class StorageAssetsItem
{
    public int Count { get; set; }
    public long Bytes { get; set; }

    [JsonIgnore] public string Summary => $"{Count} stored file(s) · {StorageSize.Format(Bytes)}";
}

public sealed class StorageLibraryItem
{
    public string Path { get; set; } = string.Empty;
    public string Source { get; set; } = "default";
    public string? PointerError { get; set; }
    public bool Writable { get; set; }
    public List<StorageAreaItem> Areas { get; set; } = [];
    public DiskSpaceItem? Disk { get; set; }

    [JsonIgnore]
    public string SourceText => Source == "relocated" ? "Set by an administrator" : "Default (the server data folder)";
}

public sealed class StorageRelocationJobItem
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public sealed class StorageRelocationProgressItem
{
    public string Phase { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int FilesTotal { get; set; }
    public int FilesCopied { get; set; }
    public long BytesTotal { get; set; }
    public long BytesCopied { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }

    [JsonIgnore]
    public double Percent => BytesTotal > 0
        ? Math.Clamp(BytesCopied * 100d / BytesTotal, 0, 100)
        : FilesTotal > 0 ? Math.Clamp(FilesCopied * 100d / FilesTotal, 0, 100) : 0;

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var phase = Phase switch
            {
                "copying" => "Copying files",
                "verifying" => "Verifying every file",
                "switching" => "Switching the library to the new location",
                "completed" => "Move completed",
                "failed" => "Move failed",
                _ => Phase
            };
            if (Phase == "completed")
                return $"{phase}: {FilesCopied} file(s), {StorageSize.Format(BytesCopied)}. The previous copy was kept.";
            if (Phase == "failed")
                return $"{phase}: {Error ?? "see the server log"}";
            return $"{phase}: {FilesCopied} of {FilesTotal} file(s), {StorageSize.Format(BytesCopied)} of {StorageSize.Format(BytesTotal)}";
        }
    }
}

public sealed class StorageRelocationInfo
{
    public bool Active { get; set; }
    public StorageRelocationJobItem? Job { get; set; }
    public StorageRelocationProgressItem? Progress { get; set; }
}

public sealed class StorageLocationInfo
{
    public string DataRoot { get; set; } = string.Empty;
    public string DataRootFrom { get; set; } = string.Empty;
    public StorageLibraryItem Library { get; set; } = new();
    public StorageAssetsItem Assets { get; set; } = new();
    public StorageRelocationInfo Relocation { get; set; } = new();
}

public sealed class StorageValidationResult
{
    public bool Valid { get; set; }
    public string? RequestedPath { get; set; }
    public string? ResolvedPath { get; set; }
    public List<StorageCheckItem> Checks { get; set; } = [];
    public DiskSpaceItem? Disk { get; set; }
}

public sealed class StorageRelocationStart
{
    public Guid JobId { get; set; }
    public string Target { get; set; } = string.Empty;
    public string StateUrl { get; set; } = string.Empty;
}

internal static class StorageSize
{
    public static string Format(long? bytes)
    {
        if (bytes is null) return "unknown";
        double value = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }
}

