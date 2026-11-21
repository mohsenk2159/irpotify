namespace MusicHub.Domain;

public sealed class FileHash
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AudioAsset? AudioAsset { get; set; }
}

public sealed class AudioAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileHashId { get; set; }
    public FileHash FileHash { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public string StoragePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}

public sealed class Track
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string ArtistDisplay { get; set; } = string.Empty;
    public string AlbumDisplay { get; set; } = string.Empty;
    public Guid? GenreId { get; set; }
    public Genre? Genre { get; set; }
    public Guid AudioAssetId { get; set; }
    public AudioAsset AudioAsset { get; set; } = null!;
    public int? Year { get; set; }
    public int? TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public double DurationSeconds { get; set; }
    public int? Bitrate { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? Codec { get; set; }
    public string? ArtworkPath { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();
    public ICollection<AlbumTrack> AlbumTracks { get; set; } = new List<AlbumTrack>();
    public ICollection<FolderTrack> FolderTracks { get; set; } = new List<FolderTrack>();
}

public sealed class Artist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ArtworkPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<TrackArtist> TrackArtists { get; set; } = new List<TrackArtist>();
    public ICollection<Album> Albums { get; set; } = new List<Album>();
}

public sealed class TrackArtist
{
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public Guid ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public string Role { get; set; } = "Primary";
    public int Position { get; set; }
}

public sealed class Album
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public string AlbumArtist { get; set; } = string.Empty;
    public Guid? ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string? Description { get; set; }
    public string? ArtworkPath { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<AlbumTrack> AlbumTracks { get; set; } = new List<AlbumTrack>();
}

public sealed class AlbumTrack
{
    public Guid AlbumId { get; set; }
    public Album Album { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public int Position { get; set; }
}

public sealed class Genre
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}

public sealed class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public string? Description { get; set; }
    public string? ArtworkPath { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<FolderTrack> FolderTracks { get; set; } = new List<FolderTrack>();
}

public sealed class FolderTrack
{
    public Guid FolderId { get; set; }
    public Folder Folder { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public bool IsPublic { get; set; }
    public bool IsCollaborative { get; set; }
    public string? ArtworkPath { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PlaylistTrack> Tracks { get; set; } = new List<PlaylistTrack>();
}

public sealed class PlaylistTrack
{
    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public int Position { get; set; }
    public Guid? AddedByUserId { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Favorite
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlaybackEvent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public Guid? DeviceId { get; set; }
    public double PositionSeconds { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ListeningHistory
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TrackId { get; set; }
    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.UtcNow;
    public int PlayCount { get; set; } = 1;
}
