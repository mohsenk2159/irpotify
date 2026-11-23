using Microsoft.EntityFrameworkCore;
using MusicHub.Domain;

namespace MusicHub.Infrastructure;

public sealed class MusicHubDbContext(DbContextOptions<MusicHubDbContext> options) : DbContext(options)
{
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PairingSession> PairingSessions => Set<PairingSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FileHash> FileHashes => Set<FileHash>();
    public DbSet<AudioAsset> AudioAssets => Set<AudioAsset>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<AlbumTrack> AlbumTracks => Set<AlbumTrack>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderTrack> FolderTracks => Set<FolderTrack>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<PlaybackEvent> PlaybackEvents => Set<PlaybackEvent>();
    public DbSet<ListeningHistory> ListeningHistory => Set<ListeningHistory>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<UploadJob> UploadJobs => Set<UploadJob>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<StorageIntegrityCheck> StorageIntegrityChecks => Set<StorageIntegrityCheck>();
    public DbSet<DuplicateReport> DuplicateReports => Set<DuplicateReport>();
    public DbSet<TelegramConfiguration> TelegramConfigurations => Set<TelegramConfiguration>();
    public DbSet<TelegramUpdate> TelegramUpdates => Set<TelegramUpdate>();
    public DbSet<TelegramImport> TelegramImports => Set<TelegramImport>();
    public DbSet<TelegramPublication> TelegramPublications => Set<TelegramPublication>();
    public DbSet<TelegramMessageMapping> TelegramMessageMappings => Set<TelegramMessageMapping>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<FailedJob> FailedJobs => Set<FailedJob>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
    public DbSet<BackupHistory> BackupHistory => Set<BackupHistory>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<SystemNotification> Notifications => Set<SystemNotification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.HasData(
                new Role { Id = 1, Name = RoleNames.Administrator, Description = "Full system administration and the only role that may change library content", IsSystem = true },
                // Contributor no longer grants write access: only administrators may create, edit,
                // move or delete tracks, albums, artists, genres, folders or uploads. The role is
                // kept so existing assignments remain valid; it is now read-only (CR-004).
                new Role { Id = 2, Name = RoleNames.Contributor, Description = "Read-only library access; content changes require an administrator", IsSystem = true },
                new Role { Id = 3, Name = RoleNames.Listener, Description = "Browse, stream, favorite, and create playlists", IsSystem = true });
        });

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.DeactivationReason).HasMaxLength(32);
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Device>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Name });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LoginHistory>(e =>
        {
            e.HasIndex(x => new { x.Email, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<Invitation>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.Email, x.AcceptedAt });
        });

        b.Entity<PairingSession>(e =>
        {
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<FileHash>(e =>
        {
            e.HasIndex(x => x.Sha256).IsUnique();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        });

        b.Entity<AudioAsset>(e =>
        {
            e.HasIndex(x => x.FileHashId).IsUnique();
            e.HasIndex(x => x.StoragePath).IsUnique();
            e.HasOne(x => x.FileHash).WithOne(x => x.AudioAsset).HasForeignKey<AudioAsset>(x => x.FileHashId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.StoragePath).HasMaxLength(512).IsRequired();
        });

        b.Entity<Track>(e =>
        {
            e.HasIndex(x => x.NormalizedTitle);
            e.HasIndex(x => x.ArtistDisplay);
            e.HasIndex(x => x.AlbumDisplay);
            e.HasIndex(x => new { x.IsDeleted, x.CreatedAt });
            e.HasIndex(x => x.GenreId);
            e.Property(x => x.Title).HasMaxLength(400).IsRequired();
            e.Property(x => x.NormalizedTitle).HasMaxLength(400).IsRequired();
            e.Property(x => x.ArtistDisplay).HasMaxLength(400);
            e.Property(x => x.AlbumDisplay).HasMaxLength(400);
            e.HasOne(x => x.AudioAsset).WithMany(x => x.Tracks).HasForeignKey(x => x.AudioAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Genre).WithMany(x => x.Tracks).HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(300).IsRequired();
        });

        b.Entity<TrackArtist>(e =>
        {
            e.HasKey(x => new { x.TrackId, x.ArtistId });
            e.HasIndex(x => x.ArtistId);
            e.HasOne(x => x.Track).WithMany(x => x.TrackArtists).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Artist).WithMany(x => x.TrackArtists).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Role).HasMaxLength(64);
        });

        b.Entity<Album>(e =>
        {
            e.HasIndex(x => x.NormalizedTitle);
            e.HasIndex(x => x.ArtistId);
            e.Property(x => x.Title).HasMaxLength(400).IsRequired();
            e.Property(x => x.NormalizedTitle).HasMaxLength(400).IsRequired();
            e.Property(x => x.AlbumArtist).HasMaxLength(400);
            e.HasOne(x => x.Artist).WithMany(x => x.Albums).HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<AlbumTrack>(e =>
        {
            e.HasKey(x => new { x.AlbumId, x.TrackId });
            e.HasIndex(x => new { x.AlbumId, x.Position });
            e.HasOne(x => x.Album).WithMany(x => x.AlbumTracks).HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany(x => x.AlbumTracks).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Genre>(e =>
        {
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
        });

        b.Entity<Folder>(e =>
        {
            e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<FolderTrack>(e =>
        {
            e.HasKey(x => new { x.FolderId, x.TrackId });
            e.HasIndex(x => x.TrackId);
            e.HasOne(x => x.Folder).WithMany(x => x.FolderTracks).HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany(x => x.FolderTracks).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Playlist>(e =>
        {
            e.HasIndex(x => new { x.OwnerUserId, x.IsDeleted });
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlaylistTrack>(e =>
        {
            e.HasKey(x => new { x.PlaylistId, x.TrackId });
            e.HasIndex(x => new { x.PlaylistId, x.Position });
            e.HasOne(x => x.Playlist).WithMany(x => x.Tracks).HasForeignKey(x => x.PlaylistId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Favorite>(e =>
        {
            e.HasKey(x => new { x.UserId, x.TrackId });
            e.HasIndex(x => x.TrackId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlaybackEvent>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.StartedAt });
            e.HasIndex(x => new { x.TrackId, x.StartedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ListeningHistory>(e =>
        {
            // One row per (user, track). The handler maintains it as an atomic upsert against this
            // index; without it two concurrent reports both inserted and every later report for
            // that pair failed with "Sequence contains more than one element" (CR-003).
            e.HasIndex(x => new { x.UserId, x.TrackId }).IsUnique();
            e.HasIndex(x => x.PlayedAt);
        });

        b.Entity<UploadSession>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => x.Sha256);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UploadJob>(e =>
        {
            e.HasOne(x => x.UploadSession).WithMany().HasForeignKey(x => x.UploadSessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Status);
        });

        b.Entity<StorageLocation>(e =>
        {
            e.HasIndex(x => x.IsDefault).HasFilter("\"IsDefault\" = true").IsUnique();
            e.Property(x => x.RootPath).HasMaxLength(1024).IsRequired();
        });

        b.Entity<StorageIntegrityCheck>(e => e.HasIndex(x => x.StartedAt));
        b.Entity<DuplicateReport>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Sha256);
        });

        b.Entity<TelegramConfiguration>(e =>
        {
            e.Property(x => x.BotTokenProtected).HasMaxLength(4096);
            e.Property(x => x.ApiEndpoint).HasMaxLength(512);
            e.Property(x => x.LocalBotApiEndpoint).HasMaxLength(512);
        });

        b.Entity<TelegramUpdate>(e =>
        {
            e.HasIndex(x => x.TelegramUpdateId).IsUnique();
        });

        b.Entity<TelegramImport>(e =>
        {
            e.HasIndex(x => new { x.TelegramUserId, x.CreatedAt });
            e.HasIndex(x => x.Status);
        });

        b.Entity<TelegramPublication>(e =>
        {
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => x.TrackId);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TelegramMessageMapping>(e =>
        {
            e.HasIndex(x => new { x.ChatId, x.MessageId }).IsUnique();
            e.HasIndex(x => x.TrackId);
        });

        b.Entity<BackgroundJob>(e =>
        {
            e.HasIndex(x => new { x.Status, x.AvailableAt });
            e.Property(x => x.Type).HasMaxLength(200).IsRequired();
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(x => new { x.ProcessedAt, x.NextAttemptAt });
            e.Property(x => x.Type).HasMaxLength(200).IsRequired();
        });

        b.Entity<FailedJob>(e => e.HasIndex(x => x.FailedAt));

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
        });

        b.Entity<SystemEvent>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.Level, x.CreatedAt });
        });

        b.Entity<BackupHistory>(e => e.HasIndex(x => x.StartedAt));
        b.Entity<FeatureFlag>(e => e.HasKey(x => x.Name));

        b.Entity<SystemNotification>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.IsRead);
            e.Property(x => x.Type).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        foreach (var entityType in b.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType.IsEnum)
                {
                    property.SetProviderClrType(typeof(string));
                }
            }
        }
    }
}
