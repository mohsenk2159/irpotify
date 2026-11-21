using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MusicHub.Api;

namespace MusicHub.Api.Realtime;

[Authorize]
public sealed class LibraryHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "library");
        await base.OnConnectedAsync();
    }
}

public sealed class RealtimeNotifier(IHubContext<LibraryHub> hub)
{
    public Task TrackAdded(object track) => hub.Clients.All.SendAsync("TrackAdded", track);
    public Task TrackUpdated(object track) => hub.Clients.All.SendAsync("TrackUpdated", track);
    public Task TrackDeleted(Guid trackId) => hub.Clients.All.SendAsync("TrackDeleted", new { id = trackId });
    public Task AlbumUpdated(object album) => hub.Clients.All.SendAsync("AlbumUpdated", album);
    public Task FolderUpdated(object folder) => hub.Clients.All.SendAsync("FolderUpdated", folder);
    public Task PlaylistUpdated(object playlist) => hub.Clients.All.SendAsync("PlaylistUpdated", playlist);
    public Task UploadCompleted(object upload) => hub.Clients.All.SendAsync("UploadCompleted", upload);
    public Task TelegramPublished(object publication) => hub.Clients.All.SendAsync("TelegramPublished", publication);
    public Task StorageWarning(object warning) => hub.Clients.All.SendAsync("StorageWarning", warning);
}

public sealed class StreamTracker
{
    private int _active;

    // Per-user dimension. streaming.maxConcurrentPerUser was exposed and rendered in the dashboard
    // but nothing read it, because the only accounting was this global counter - a dead control
    // (CR-019).
    private readonly ConcurrentDictionary<Guid, int> _perUser = new();

    public int Active => Volatile.Read(ref _active);

    public int ActiveFor(Guid userId) => _perUser.TryGetValue(userId, out var count) ? Volatile.Read(ref count) : 0;

    public IDisposable Track() => Track(Guid.Empty);

    public IDisposable Track(Guid userId)
    {
        Interlocked.Increment(ref _active);
        if (userId != Guid.Empty)
        {
            _perUser.AddOrUpdate(userId, 1, (_, current) => Interlocked.Increment(ref current));
        }
        return new Releaser(this, userId);
    }

    private sealed class Releaser(StreamTracker tracker, Guid userId) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Decrement(ref tracker._active);
            if (userId == Guid.Empty) return;
            // Remove the entry when it reaches zero so the dictionary does not retain an entry for
            // every user who ever streamed.
            var updated = tracker._perUser.AddOrUpdate(userId, 0, (_, current) =>
            {
                var next = Interlocked.Decrement(ref current);
                return next < 0 ? 0 : next;
            });
            if (updated <= 0) tracker._perUser.TryRemove(new KeyValuePair<Guid, int>(userId, updated));
        }
    }
}
