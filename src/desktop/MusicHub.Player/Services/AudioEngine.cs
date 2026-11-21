using LibVLCSharp.Shared;

namespace MusicHub.Player.Services;

/// <summary>
/// Thin wrapper around LibVLC. LibVLC is used instead of WPF MediaPlayer because the
/// library contains FLAC, OGG, OPUS and M4A files, which Media Foundation does not
/// reliably decode. Events are raised on LibVLC's native threads; consumers must marshal.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private bool _disposed;

    public AudioEngine()
    {
        Core.Initialize();
        _libVlc = new LibVLC("--no-video", "--quiet", "--no-osd");
        _player = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = false
        };
        _player.TimeChanged += (_, e) => TimeChanged?.Invoke(e.Time);
        _player.LengthChanged += (_, e) => LengthChanged?.Invoke(e.Length);
        _player.Playing += (_, _) => PlayingChanged?.Invoke(true);
        _player.Paused += (_, _) => PlayingChanged?.Invoke(false);
        _player.Stopped += (_, _) => PlayingChanged?.Invoke(false);
        _player.EndReached += (_, _) => EndReached?.Invoke();
        _player.EncounteredError += (_, _) => ErrorOccurred?.Invoke("This track could not be played. The file may be missing or use an unsupported codec.");
    }

    public event Action<long>? TimeChanged;
    public event Action<long>? LengthChanged;
    public event Action<bool>? PlayingChanged;
    public event Action? EndReached;
    public event Action<string>? ErrorOccurred;

    public bool IsPlaying => !_disposed && _player.IsPlaying;
    public long Length => _disposed ? 0 : _player.Length;
    public long Time
    {
        get => _disposed ? 0 : _player.Time;
        set { if (!_disposed) _player.Time = Math.Max(0, value); }
    }

    public int Volume
    {
        get => _disposed ? 0 : _player.Volume;
        set { if (!_disposed) _player.Volume = Math.Clamp(value, 0, 100); }
    }

    public bool Mute
    {
        get => !_disposed && _player.Mute;
        set { if (!_disposed) _player.Mute = value; }
    }

    public void Play(string url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var media = new Media(_libVlc, new Uri(url))
        {
            // A short buffer keeps seeking responsive on a LAN; 1s tolerates Wi-Fi jitter.
        };
        media.AddOption(":network-caching=1000");
        media.AddOption(":http-reconnect=true");
        _player.Play(media);
    }

    public void Pause()
    {
        if (!_disposed && _player.IsPlaying) _player.Pause();
    }

    public void Resume()
    {
        if (!_disposed && !_player.IsPlaying) _player.Play();
    }

    public void Stop()
    {
        if (!_disposed) _player.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _player.Dispose();
        _libVlc.Dispose();
    }
}
