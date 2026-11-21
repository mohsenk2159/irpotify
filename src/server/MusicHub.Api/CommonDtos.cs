using MusicHub.Domain;

namespace MusicHub.Api;

public sealed record TrackDto(
    Guid Id,
    string Title,
    string Artist,
    string Album,
    string? Genre,
    double DurationSeconds,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    string? Codec,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    bool IsFavorite,
    string? ArtworkUrl);

public static class DtoMapper
{
    public static TrackDto Track(Track track, bool favorite = false) => new(
        track.Id,
        track.Title,
        track.ArtistDisplay,
        track.AlbumDisplay,
        track.Genre?.Name,
        track.DurationSeconds,
        track.Year,
        track.TrackNumber,
        track.DiscNumber,
        track.Codec,
        track.AudioAsset.SizeBytes,
        track.AudioAsset.FileHash.Sha256,
        track.CreatedAt,
        favorite,
        string.IsNullOrWhiteSpace(track.ArtworkPath) ? null : $"/api/tracks/{track.Id}/artwork");
}
