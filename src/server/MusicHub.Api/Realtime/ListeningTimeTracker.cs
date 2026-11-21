using System.Collections.Concurrent;

namespace MusicHub.Api.Realtime;

/// <summary>
/// Server-side record of audio bytes actually served to each user, so listening time can be
/// measured by the server instead of trusted from the client (B-023).
/// </summary>
/// <remarks>
/// The client-reported <c>listenedSeconds</c> on <c>POST /api/history/playback</c> is trivially
/// forged: a patched client can always report <c>0</c> and never consume its allowance. The
/// stream endpoint, however, knows exactly how many bytes left the process, and bytes divided by
/// the track's bitrate is real played wall-clock time.
///
/// This is a singleton and therefore per-process. That is deliberate and safe for the failure
/// mode that matters: the counter is a <em>lower bound</em> that is drained (not read) when a
/// playback report arrives, so a multi-instance deployment can only under-charge, never
/// over-charge. Under-charging is the pre-existing behaviour; over-charging would wrongly
/// deactivate paying accounts. If a drain never happens (app killed, no report sent) the entry is
/// bounded by the sweep below so the dictionary cannot grow without limit.
/// </remarks>
public sealed class ListeningTimeTracker
{
    /// <summary>
    /// Upper bound on a single drain, in seconds. Matches the clamp the playback endpoint applies
    /// to a client-reported value, so neither source can consume more than an hour at once.
    /// </summary>
    public const long MaxSecondsPerDrain = 3600;

    /// <summary>Entries older than this are dropped by <see cref="Prune"/>.</summary>
    public static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Pruning runs inline once the dictionary passes this size. A player that dies without
    /// sending a final report would otherwise leave one entry behind; doing it here keeps the
    /// tracker self-limiting without adding a hosted service to the shared composition root.
    /// </summary>
    private const int PruneThreshold = 512;

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private long _lastPruneTicks = DateTimeOffset.UtcNow.UtcTicks;

    private sealed class Entry
    {
        public long Bytes;
        public long LastTouchedTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    /// <summary>Records bytes actually written to the response body for this user.</summary>
    public void AddStreamedBytes(Guid userId, long bytes)
    {
        if (userId == Guid.Empty || bytes <= 0) return;
        var entry = _entries.GetOrAdd(userId, _ => new Entry());
        Interlocked.Add(ref entry.Bytes, bytes);
        Interlocked.Exchange(ref entry.LastTouchedTicks, DateTimeOffset.UtcNow.UtcTicks);
        MaybePrune();
    }

    private void MaybePrune()
    {
        if (_entries.Count < PruneThreshold) return;
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var last = Interlocked.Read(ref _lastPruneTicks);
        // At most one prune per minute, and only one thread wins the compare-exchange.
        if (now - last < TimeSpan.FromMinutes(1).Ticks) return;
        if (Interlocked.CompareExchange(ref _lastPruneTicks, now, last) != last) return;
        Prune();
    }

    /// <summary>
    /// Removes and returns the bytes accumulated for this user. Draining rather than reading is
    /// what makes the value safe to compare against a client report: both sources describe the
    /// same interval, so taking the larger of the two cannot double-count.
    /// </summary>
    public long DrainStreamedBytes(Guid userId)
    {
        if (userId == Guid.Empty) return 0;
        if (!_entries.TryGetValue(userId, out var entry)) return 0;
        return Interlocked.Exchange(ref entry.Bytes, 0);
    }

    /// <summary>Drops entries that have not been touched for <see cref="EntryLifetime"/>.</summary>
    public int Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(EntryLifetime).UtcTicks;
        var removed = 0;
        foreach (var pair in _entries)
        {
            var touched = Interlocked.Read(ref pair.Value.LastTouchedTicks);
            if (touched >= cutoff) continue;
            // Only remove when nothing arrived in the meantime, so a live stream is never dropped.
            if (Interlocked.Read(ref pair.Value.Bytes) != 0) continue;
            if (touched == Interlocked.Read(ref pair.Value.LastTouchedTicks)
                && _entries.TryRemove(pair.Key, out _)) removed++;
        }
        return removed;
    }

    /// <summary>
    /// Converts served bytes into played seconds using the track's bitrate, falling back to
    /// size/duration when the probe did not report a bitrate.
    /// </summary>
    public static long BytesToSeconds(long bytes, int? bitrate, long sizeBytes, double durationSeconds)
    {
        if (bytes <= 0) return 0;

        double bytesPerSecond = 0;
        if (bitrate is > 0) bytesPerSecond = bitrate.Value / 8.0;
        else if (durationSeconds > 0 && sizeBytes > 0) bytesPerSecond = sizeBytes / durationSeconds;

        // Without a usable rate the bytes cannot be converted; charge nothing rather than guess.
        if (bytesPerSecond <= 0) return 0;

        var seconds = (long)Math.Round(bytes / bytesPerSecond, MidpointRounding.AwayFromZero);
        return Math.Clamp(seconds, 0, MaxSecondsPerDrain);
    }
}
