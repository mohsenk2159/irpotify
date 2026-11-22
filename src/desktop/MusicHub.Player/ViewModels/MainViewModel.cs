using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MusicHub.Player.Models;
using MusicHub.Player.Services;

namespace MusicHub.Player.ViewModels;

public enum PlayerView
{
    Home,
    Search,
    Tracks,
    Albums,
    Artists,
    Playlists,
    Favorites,
    Settings,
    Storage
}

public enum CardKind
{
    Album,
    Artist,
    Playlist
}

public sealed class CardItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public CardKind Kind { get; init; }
    public string Initial => string.IsNullOrWhiteSpace(Title) ? "?" : Title.Trim()[..1].ToUpperInvariant();
    public string Badge => Kind.ToString();
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly PlayerApiClient _api;
    private readonly AudioEngine _audio;
    private readonly Dispatcher _dispatcher;
    private readonly PlayerPreferences _preferences;
    private int _playRequestId;
    private bool _seeking;
    private bool _disposed;
    private const double ListenFlushSeconds = 30;
    private readonly System.Diagnostics.Stopwatch _listenClock = new();
    private double _pendingListenSeconds;

    public MainViewModel(PlayerApiClient api)
    {
        _api = api;
        AppLog.Write("MainViewModel: creating audio engine");
        _audio = new AudioEngine();
        AppLog.Write("MainViewModel: audio engine ready");
        _dispatcher = Application.Current.Dispatcher;
        _preferences = SettingsStore.Load();

        _audio.TimeChanged += OnAudioTimeChanged;
        _audio.LengthChanged += OnAudioLengthChanged;
        _audio.PlayingChanged += OnAudioPlayingChanged;
        _audio.EndReached += OnAudioEndReached;
        _audio.ErrorOccurred += OnAudioError;

        _audio.Volume = _preferences.Volume;
        Shuffle = _preferences.Shuffle;
        RepeatMode = _preferences.RepeatMode;
        Queue.CollectionChanged += (_, _) => OnPropertyChanged(nameof(QueueSummary));
        Cards.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasNoCards));
            OnPropertyChanged(nameof(EmptyCardsMessage));
        };

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "Home"));
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        PlayTrackCommand = new AsyncRelayCommand(p => p is TrackItem track ? PlayTrackAsync(track, TrackContext()) : Task.CompletedTask);
        PlayDetailCommand = new AsyncRelayCommand(_ => PlayCollectionAsync(DetailTracks, DetailTitle));
        PlayQueueItemCommand = new AsyncRelayCommand(p => p is TrackItem track ? PlayQueueItemAsync(track) : Task.CompletedTask);
        PlayPauseCommand = new AsyncRelayCommand(_ => TogglePlayPauseAsync());
        NextCommand = new AsyncRelayCommand(_ => NextAsync(userInitiated: true));
        PreviousCommand = new AsyncRelayCommand(_ => PreviousAsync());
        ToggleFavoriteCommand = new AsyncRelayCommand(p => p is TrackItem track ? ToggleFavoriteAsync(track) : Task.CompletedTask);
        TogglePlaylistFavoriteCommand = new AsyncRelayCommand(_ => CurrentTrack is null ? Task.CompletedTask : ToggleFavoriteAsync(CurrentTrack));
        SearchCommand = new AsyncRelayCommand(_ => RunSearchAsync());
        OpenCardCommand = new AsyncRelayCommand(p => p is CardItem card ? OpenCardAsync(card) : Task.CompletedTask);
        CloseDetailCommand = new RelayCommand(_ => CloseDetail());
        ToggleShuffleCommand = new RelayCommand(_ => Shuffle = !Shuffle);
        CycleRepeatCommand = new RelayCommand(_ => RepeatMode = (RepeatMode + 1) % 3);
        ToggleMuteCommand = new RelayCommand(_ => IsMuted = !IsMuted);
        ToggleQueueCommand = new RelayCommand(_ => IsQueueOpen = !IsQueueOpen);
        SignOutCommand = new AsyncRelayCommand(_ => SignOutAsync());
        ClearErrorCommand = new RelayCommand(_ => ErrorMessage = null);
        CheckStoragePathCommand = new AsyncRelayCommand(_ => CheckStoragePathAsync());
        RelocateStorageCommand = new AsyncRelayCommand(_ => RelocateStorageAsync());
        RefreshStorageCommand = new AsyncRelayCommand(_ => LoadStorageAsync());
    }

    public PlayerApiClient Api => _api;

    public ObservableCollection<TrackItem> Tracks { get; } = [];
    public ObservableCollection<TrackItem> DetailTracks { get; } = [];
    public ObservableCollection<TrackItem> RecentlyAdded { get; } = [];
    public ObservableCollection<TrackItem> RecentlyPlayed { get; } = [];
    public ObservableCollection<TrackItem> MostPlayed { get; } = [];
    public ObservableCollection<TrackItem> FavoriteTracks { get; } = [];
    public ObservableCollection<CardItem> Cards { get; } = [];
    public ObservableCollection<CardItem> HomeAlbums { get; } = [];
    public ObservableCollection<TrackItem> SearchTracks { get; } = [];
    public ObservableCollection<CardItem> SearchCards { get; } = [];
    public ObservableCollection<TrackItem> Queue { get; } = [];
    public ObservableCollection<PlaylistItem> Playlists { get; } = [];

    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PlayTrackCommand { get; }
    public AsyncRelayCommand PlayDetailCommand { get; }
    public AsyncRelayCommand PlayQueueItemCommand { get; }
    public AsyncRelayCommand PlayPauseCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand ToggleFavoriteCommand { get; }
    public AsyncRelayCommand TogglePlaylistFavoriteCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public RelayCommand CloseDetailCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand CycleRepeatCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand ToggleQueueCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }
    public RelayCommand ClearErrorCommand { get; }
    public AsyncRelayCommand CheckStoragePathCommand { get; }
    public AsyncRelayCommand RelocateStorageCommand { get; }
    public AsyncRelayCommand RefreshStorageCommand { get; }

    public event Action? SignedOut;

    private PlayerView _view = PlayerView.Home;
    public PlayerView View
    {
        get => _view;
        private set
        {
            if (!SetProperty(ref _view, value)) return;
            RaiseViewFlags();
        }
    }

    private void RaiseViewFlags()
    {
        OnPropertyChanged(nameof(IsHomeView));
        OnPropertyChanged(nameof(IsTracksView));
        OnPropertyChanged(nameof(IsAlbumsView));
        OnPropertyChanged(nameof(IsArtistsView));
        OnPropertyChanged(nameof(IsPlaylistsView));
        OnPropertyChanged(nameof(IsFavoritesView));
        OnPropertyChanged(nameof(IsSearchView));
        OnPropertyChanged(nameof(IsSettingsView));
        OnPropertyChanged(nameof(IsStorageView));
        OnPropertyChanged(nameof(IsCardView));
        OnPropertyChanged(nameof(IsPlainTrackView));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(HasNoCards));
        OnPropertyChanged(nameof(EmptyCardsMessage));
    }

    public bool IsHomeView => View == PlayerView.Home;
    public bool IsTracksView => View == PlayerView.Tracks;
    public bool IsAlbumsView => View == PlayerView.Albums;
    public bool IsArtistsView => View == PlayerView.Artists;
    public bool IsPlaylistsView => View == PlayerView.Playlists;
    public bool IsFavoritesView => View == PlayerView.Favorites;
    public bool IsSearchView => View == PlayerView.Search;
    public bool IsSettingsView => View == PlayerView.Settings;
    public bool IsStorageView => View == PlayerView.Storage;

    public bool IsCardView => (IsAlbumsView || IsArtistsView || IsPlaylistsView) && !IsDetailOpen;
    public bool IsPlainTrackView => (IsTracksView || IsFavoritesView) && !IsDetailOpen;

    public bool HasNoCards => Cards.Count == 0;

    public string EmptyCardsMessage => View switch
    {
        PlayerView.Artists => "No artists in your library yet.",
        PlayerView.Playlists => "No playlists yet. Use New playlist to create one.",
        _ => "No albums in your library yet."
    };

    public string PageTitle => View switch
    {
        PlayerView.Home => "Home",
        PlayerView.Search => "Search",
        PlayerView.Tracks => "All tracks",
        PlayerView.Albums => "Albums",
        PlayerView.Artists => "Artists",
        PlayerView.Playlists => "Playlists",
        PlayerView.Favorites => "Favorites",
        PlayerView.Settings => "Settings",
        PlayerView.Storage => "Storage",
        _ => "MusicHub"
    };

    public string PageSubtitle => View switch
    {
        PlayerView.Home => $"Welcome back, {_api.CurrentUser?.DisplayName ?? "listener"}",
        PlayerView.Search => "Find tracks, albums and artists",
        PlayerView.Tracks => Tracks.Count == 1 ? "1 track in your library" : $"{Tracks.Count} tracks in your library",
        PlayerView.Albums => "Browse the albums in your library",
        PlayerView.Artists => "Browse artists",
        PlayerView.Playlists => "Your playlists and public playlists",
        PlayerView.Favorites => "Tracks you marked as favorite",
        PlayerView.Settings => "Account, server and playback preferences",
        PlayerView.Storage => "Where the server keeps the music library",
        _ => string.Empty
    };

    // ---- Detail state -------------------------------------------------------------------

    private bool _isDetailOpen;
    public bool IsDetailOpen
    {
        get => _isDetailOpen;
        private set
        {
            if (!SetProperty(ref _isDetailOpen, value)) return;
            OnPropertyChanged(nameof(IsCardView));
            OnPropertyChanged(nameof(IsPlainTrackView));
        }
    }

    private string _detailTitle = string.Empty;
    public string DetailTitle { get => _detailTitle; private set => SetProperty(ref _detailTitle, value); }

    private string _detailSubtitle = string.Empty;
    public string DetailSubtitle { get => _detailSubtitle; private set => SetProperty(ref _detailSubtitle, value); }

    private CardKind _detailKind;
    public CardKind DetailKind { get => _detailKind; private set => SetProperty(ref _detailKind, value); }

    private Guid? _detailPlaylistId;
    public bool CanEditPlaylist => DetailKind == CardKind.Playlist && _detailPlaylistId is not null;

    // ---- Async state --------------------------------------------------------------------

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    // ---- Search -------------------------------------------------------------------------

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            OnPropertyChanged(nameof(HasSearchText));
        }
    }

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    private bool _hasSearched;
    public bool HasSearched { get => _hasSearched; private set => SetProperty(ref _hasSearched, value); }

    // ---- Now playing --------------------------------------------------------------------

    private TrackItem? _currentTrack;
    public TrackItem? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            if (!SetProperty(ref _currentTrack, value)) return;
            OnPropertyChanged(nameof(HasCurrentTrack));
            OnPropertyChanged(nameof(CurrentTitle));
            OnPropertyChanged(nameof(CurrentSubtitle));
            OnPropertyChanged(nameof(CurrentInitial));
        }
    }

    public bool HasCurrentTrack => CurrentTrack is not null;
    public string CurrentTitle => CurrentTrack?.Title ?? "Nothing playing";
    public string CurrentSubtitle => CurrentTrack?.Subtitle ?? "Pick a track to start listening";
    public string CurrentInitial => CurrentTrack?.Initial ?? "♪";

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!SetProperty(ref _isPlaying, value)) return;
            OnPropertyChanged(nameof(PlayPauseGlyph));
            OnPropertyChanged(nameof(PlayPauseTooltip));
        }
    }

    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";
    public string PlayPauseTooltip => IsPlaying ? "Pause" : "Play";

    private double _positionSeconds;
    public double PositionSeconds
    {
        get => _positionSeconds;
        private set
        {
            if (!SetProperty(ref _positionSeconds, value)) return;
            OnPropertyChanged(nameof(PositionText));
        }
    }

    private double _durationSeconds;
    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (!SetProperty(ref _durationSeconds, value)) return;
            OnPropertyChanged(nameof(DurationText));
        }
    }

    public string PositionText => Format(TimeSpan.FromSeconds(Math.Max(0, PositionSeconds)));
    public string DurationText => Format(TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)));

    private BitmapImage? _currentArtwork;
    public BitmapImage? CurrentArtwork
    {
        get => _currentArtwork;
        private set
        {
            if (!SetProperty(ref _currentArtwork, value)) return;
            OnPropertyChanged(nameof(HasCurrentArtwork));
        }
    }

    public bool HasCurrentArtwork => CurrentArtwork is not null;

    private int _volume;
    public int Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, value)) return;
            _audio.Volume = value;
            if (value > 0) IsMuted = false;
            SavePreferences();
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value)) return;
            _audio.Mute = value;
            OnPropertyChanged(nameof(VolumeGlyph));
        }
    }

    public string VolumeGlyph => IsMuted || Volume == 0 ? "🔇" : "🔊";

    private bool _shuffle;
    public bool Shuffle
    {
        get => _shuffle;
        set { if (SetProperty(ref _shuffle, value)) { OnPropertyChanged(nameof(ShuffleGlyph)); SavePreferences(); } }
    }

    public string ShuffleGlyph => Shuffle ? "🔀" : "➡";

    private int _repeatMode;
    public int RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (!SetProperty(ref _repeatMode, value)) return;
            OnPropertyChanged(nameof(RepeatGlyph));
            OnPropertyChanged(nameof(RepeatTooltip));
            SavePreferences();
        }
    }

    public string RepeatGlyph => RepeatMode switch { 1 => "🔁", 2 => "🔂", _ => "➡" };
    public string RepeatTooltip => RepeatMode switch { 1 => "Repeat all", 2 => "Repeat one", _ => "Repeat off" };

    private bool _isQueueOpen;
    public bool IsQueueOpen
    {
        get => _isQueueOpen;
        set
        {
            if (!SetProperty(ref _isQueueOpen, value)) return;
            OnPropertyChanged(nameof(QueueVisibility));
        }
    }

    public Visibility QueueVisibility => IsQueueOpen ? Visibility.Visible : Visibility.Collapsed;

    private int _queueIndex = -1;

    public string UserDisplayName => _api.CurrentUser?.DisplayName ?? "Listener";
    public string UserEmail => _api.CurrentUser?.Email ?? string.Empty;
    public string UserRole => _api.CurrentUser?.Role ?? string.Empty;
    public string ServerUrl => _api.BaseUrl;

    // ---- Admin: library storage location ------------------------------------------------

    /// <summary>
    /// Decides whether the admin screen is offered. Both shapes are checked because the server
    /// returns one primary role and an array of all roles. This is presentation only: every
    /// storage request is authorized again by the server, which answers 403 for anyone else.
    /// </summary>
    public bool IsAdmin =>
        string.Equals(_api.CurrentUser?.Role, "Administrator", StringComparison.Ordinal)
        || (_api.CurrentUser?.Roles?.Contains("Administrator", StringComparer.Ordinal) ?? false);

    private StorageLocationInfo? _storageInfo;
    public StorageLocationInfo? StorageInfo
    {
        get => _storageInfo;
        private set
        {
            if (!SetProperty(ref _storageInfo, value)) return;
            OnPropertyChanged(nameof(HasStorageInfo));
            OnPropertyChanged(nameof(HasPointerError));
            OnPropertyChanged(nameof(StorageDiskText));
        }
    }

    public bool HasStorageInfo => StorageInfo is not null;

    public bool HasPointerError => !string.IsNullOrWhiteSpace(StorageInfo?.Library.PointerError);

    public bool HasStorageChecks => StorageChecks.Count > 0;

    public string StorageDiskText => StorageInfo?.Library.Disk?.FreeText ?? "Disk space could not be read.";

    private string _storagePathInput = string.Empty;
    public string StoragePathInput
    {
        get => _storagePathInput;
        set
        {
            if (!SetProperty(ref _storagePathInput, value)) return;
            // A changed path invalidates the previous safety report.
            _storageValidated = false;
            OnPropertyChanged(nameof(StorageCanRelocate));
        }
    }

    public ObservableCollection<StorageCheckItem> StorageChecks { get; } = [];

    private bool _storageValidated;
    private string _storageMessage = string.Empty;
    public string StorageMessage
    {
        get => _storageMessage;
        private set
        {
            if (!SetProperty(ref _storageMessage, value)) return;
            OnPropertyChanged(nameof(HasStorageMessage));
        }
    }

    public bool HasStorageMessage => !string.IsNullOrWhiteSpace(StorageMessage);

    private bool _storageRelocating;
    public bool IsStorageRelocating
    {
        get => _storageRelocating;
        private set
        {
            if (!SetProperty(ref _storageRelocating, value)) return;
            OnPropertyChanged(nameof(StorageCanRelocate));
            OnPropertyChanged(nameof(StorageRelocateLabel));
        }
    }

    /// <summary>Only a path that just passed every safety check, with no move in flight, may be used.</summary>
    public bool StorageCanRelocate => _storageValidated && !IsStorageRelocating && IsAdmin;

    public string StorageRelocateLabel => IsStorageRelocating ? "Move in progress…" : "Move library to this folder";

    private bool _hasStorageProgress;
    public bool HasStorageProgress
    {
        get => _hasStorageProgress;
        set => SetProperty(ref _hasStorageProgress, value);
    }

    private double _storageProgressPercent;
    public double StorageProgressPercent
    {
        get => _storageProgressPercent;
        set => SetProperty(ref _storageProgressPercent, value);
    }

    private string _storageProgressText = string.Empty;
    public string StorageProgressText
    {
        get => _storageProgressText;
        set => SetProperty(ref _storageProgressText, value);
    }

    private DispatcherTimer? _storageTimer;
    private bool _storagePolling;


    // ---- Admin: library storage location ------------------------------------------------

    private async Task LoadStorageAsync()
    {
        if (!IsAdmin)
        {
            ErrorMessage = "Only an administrator can view or change the library location.";
            return;
        }

        await RunAsync(async () =>
        {
            var info = await _api.GetStorageLocationAsync();
            ApplyStorage(info);
            if (string.IsNullOrWhiteSpace(StoragePathInput)) StoragePathInput = info.Library.Path;
            if (!info.Relocation.Active && info.Relocation.Job is null)
            {
                StorageMessage = "Choose a folder, check it, then start the move. No files change until you do.";
            }
        });
    }

    private void ApplyStorage(StorageLocationInfo info)
    {
        StorageInfo = info;
        var progress = info.Relocation.Progress;
        HasStorageProgress = progress is not null;
        StorageProgressPercent = progress?.Percent ?? 0;
        StorageProgressText = progress?.Summary ?? string.Empty;
        IsStorageRelocating = info.Relocation.Active;
        if (info.Relocation.Active)
        {
            StartStoragePolling();
        }
        else
        {
            StopStoragePolling();
        }
    }

    private async Task CheckStoragePathAsync()
    {
        var candidate = StoragePathInput.Trim();
        if (candidate.Length == 0)
        {
            StorageMessage = "Enter the full path of the folder that should hold the library.";
            return;
        }

        await RunAsync(async () =>
        {
            var report = await _api.ValidateStoragePathAsync(candidate);
            StorageChecks.Clear();
            foreach (var check in report.Checks) StorageChecks.Add(check);
            _storageValidated = report.Valid;
            OnPropertyChanged(nameof(StorageCanRelocate));
            OnPropertyChanged(nameof(HasStorageChecks));
            StorageMessage = report.Valid
                ? "This folder passed every check. Nothing has moved yet."
                : "This folder cannot be used yet. Fix the failing checks below.";
        });
    }

    private async Task RelocateStorageAsync()
    {
        if (!StorageCanRelocate) return;

        var target = StoragePathInput.Trim();
        await RunAsync(async () =>
        {
            var started = await _api.StartStorageRelocationAsync(target);
            _storageValidated = false;
            OnPropertyChanged(nameof(StorageCanRelocate));
            IsStorageRelocating = true;
            StorageMessage = $"The move is queued. The server starts copying to {started.Target} within a few seconds.";
            StorageProgressText = "Waiting for the worker to pick up the move…";
            HasStorageProgress = true;
            StorageProgressPercent = 0;
            StartStoragePolling();
        });
    }

    private void StartStoragePolling()
    {
        if (_storageTimer is not null || _disposed) return;
        _storageTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, OnStoragePoll, _dispatcher);
        _storageTimer.Start();
    }

    private void StopStoragePolling()
    {
        _storageTimer?.Stop();
        _storageTimer = null;
    }

    private async void OnStoragePoll(object? sender, EventArgs e)
    {
        // A timer tick must never overlap the previous one: a slow server would otherwise
        // queue an unbounded number of requests.
        if (_storagePolling || _disposed) return;
        _storagePolling = true;
        try
        {
            var relocation = await _api.GetStorageRelocationAsync();
            var progress = relocation.Progress;
            HasStorageProgress = progress is not null;
            StorageProgressPercent = progress?.Percent ?? 0;
            StorageProgressText = progress?.Summary ?? "Waiting for the worker…";
            IsStorageRelocating = relocation.Active;
            if (relocation.Active) return;

            StopStoragePolling();
            await LoadStorageAsync();
            var failed = relocation.Job?.Error is { Length: > 0 } error;
            StorageMessage = failed
                ? "The move did not finish: " + relocation.Job!.Error
                : "The library location is now the new folder. The previous copy was kept; remove it once you have verified playback.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Storage relocation poll failed: " + ex.Message);
        }
        finally
        {
            _storagePolling = false;
        }
    }

    // ---- Lifecycle ----------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        Volume = _preferences.Volume;
        await NavigateAsync("Home");
    }

    private void Navigate(string destination)
    {
        _ = NavigateAsync(destination);
    }

    private async Task NavigateAsync(string destination)
    {
        ErrorMessage = null;
        CloseDetail();
        switch (destination.ToLowerInvariant())
        {
            case "home":
                View = PlayerView.Home;
                await LoadHomeAsync();
                break;
            case "search":
                View = PlayerView.Search;
                break;
            case "tracks":
                View = PlayerView.Tracks;
                await LoadTracksAsync(null, false);
                break;
            case "albums":
                View = PlayerView.Albums;
                await LoadAlbumsAsync();
                break;
            case "artists":
                View = PlayerView.Artists;
                await LoadArtistsAsync();
                break;
            case "playlists":
                View = PlayerView.Playlists;
                await LoadPlaylistsAsync();
                break;
            case "favorites":
                View = PlayerView.Favorites;
                await LoadTracksAsync(null, true);
                break;
            case "settings":
                View = PlayerView.Settings;
                break;
            case "storage":
                if (!IsAdmin)
                {
                    ErrorMessage = "Only an administrator can view or change the library location.";
                    return;
                }
                View = PlayerView.Storage;
                await LoadStorageAsync();
                break;
        }
    }

    private async Task RefreshAsync()
    {
        await NavigateAsync(View.ToString());
    }

    private async Task LoadHomeAsync()
    {
        await RunAsync(async () =>
        {
            var home = await _api.GetHomeAsync();
            Replace(RecentlyAdded, home.RecentlyAdded);
            Replace(RecentlyPlayed, home.RecentlyPlayed);
            Replace(MostPlayed, home.MostPlayed);
            Replace(FavoriteTracks, home.Favorites);
            HomeAlbums.Clear();
            foreach (var album in home.Albums)
            {
                HomeAlbums.Add(new CardItem { Id = album.Id, Title = album.Title, Subtitle = album.Subtitle, Kind = CardKind.Album });
            }
            OnPropertyChanged(nameof(PageSubtitle));
        });
    }

    private async Task LoadTracksAsync(string? query, bool favorites)
    {
        await RunAsync(async () =>
        {
            var page = await _api.GetTracksAsync(query, favorites ? true : null, sort: "artist");
            Tracks.Clear();
            foreach (var track in page.Items) Tracks.Add(track);
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(PageTitle));
        });
    }

    private async Task LoadAlbumsAsync()
    {
        await RunAsync(async () =>
        {
            var page = await _api.GetAlbumsAsync(pageSize: 300);
            Cards.Clear();
            foreach (var album in page.Items)
            {
                Cards.Add(new CardItem { Id = album.Id, Title = album.Title, Subtitle = album.Subtitle, Kind = CardKind.Album });
            }
        });
    }

    private async Task LoadArtistsAsync()
    {
        await RunAsync(async () =>
        {
            var page = await _api.GetArtistsAsync(pageSize: 300);
            Cards.Clear();
            foreach (var artist in page.Items)
            {
                Cards.Add(new CardItem { Id = artist.Id, Title = artist.Name, Subtitle = artist.Subtitle, Kind = CardKind.Artist });
            }
        });
    }

    private async Task LoadPlaylistsAsync()
    {
        await RunAsync(async () =>
        {
            var playlists = await _api.GetPlaylistsAsync();
            Playlists.Clear();
            Cards.Clear();
            foreach (var playlist in playlists)
            {
                Playlists.Add(playlist);
                Cards.Add(new CardItem { Id = playlist.Id, Title = playlist.Name, Subtitle = playlist.Subtitle, Kind = CardKind.Playlist });
            }
        });
    }

    private async Task RunSearchAsync()
    {
        var term = SearchText.Trim();
        if (term.Length == 0) return;
        await RunAsync(async () =>
        {
            var result = await _api.SearchAsync(term);
            Replace(SearchTracks, result.Tracks);
            SearchCards.Clear();
            foreach (var album in result.Albums)
            {
                SearchCards.Add(new CardItem { Id = album.Id, Title = album.Title, Subtitle = album.AlbumArtist, Kind = CardKind.Album });
            }
            foreach (var artist in result.Artists)
            {
                SearchCards.Add(new CardItem { Id = artist.Id, Title = artist.Name, Subtitle = "Artist", Kind = CardKind.Artist });
            }
            HasSearched = true;
        });
    }

    private async Task OpenCardAsync(CardItem card)
    {
        await RunAsync(async () =>
        {
            DetailTracks.Clear();
            switch (card.Kind)
            {
                case CardKind.Album:
                {
                    var detail = await _api.GetAlbumAsync(card.Id);
                    DetailTitle = detail.Album.Title;
                    DetailSubtitle = detail.Album.Subtitle;
                    DetailKind = CardKind.Album;
                    _detailPlaylistId = null;
                    foreach (var track in detail.Tracks) DetailTracks.Add(track);
                    break;
                }
                case CardKind.Artist:
                {
                    var detail = await _api.GetArtistAsync(card.Id);
                    DetailTitle = detail.Artist.Name;
                    DetailSubtitle = detail.Artist.Subtitle;
                    DetailKind = CardKind.Artist;
                    _detailPlaylistId = null;
                    foreach (var track in detail.Tracks) DetailTracks.Add(track);
                    break;
                }
                case CardKind.Playlist:
                {
                    var detail = await _api.GetPlaylistAsync(card.Id);
                    DetailTitle = detail.Playlist.Name;
                    DetailSubtitle = detail.Playlist.Subtitle;
                    DetailKind = CardKind.Playlist;
                    _detailPlaylistId = card.Id;
                    foreach (var track in detail.Tracks) DetailTracks.Add(track);
                    break;
                }
            }
            IsDetailOpen = true;
            OnPropertyChanged(nameof(CanEditPlaylist));
        });
    }

    private void CloseDetail()
    {
        IsDetailOpen = false;
        DetailTracks.Clear();
        _detailPlaylistId = null;
    }

    // ---- Playback -----------------------------------------------------------------------

    public async Task PlayTrackAsync(TrackItem track, IEnumerable<TrackItem>? context = null)
    {
        var list = context?.ToList();
        if (list is not null && list.Count > 0)
        {
            Queue.Clear();
            foreach (var item in list) Queue.Add(item);
            _queueIndex = Queue.IndexOf(track);
            if (_queueIndex < 0)
            {
                Queue.Insert(0, track);
                _queueIndex = 0;
            }
        }
        await StartPlaybackAsync(track);
    }

    private async Task PlayCollectionAsync(IEnumerable<TrackItem> tracks, string label)
    {
        var list = tracks.ToList();
        if (list.Count == 0) return;
        Queue.Clear();
        foreach (var item in list) Queue.Add(item);
        _queueIndex = 0;
        StatusMessage = $"Playing {label}";
        await StartPlaybackAsync(list[0]);
    }

    private async Task PlayQueueItemAsync(TrackItem track)
    {
        var index = Queue.IndexOf(track);
        if (index < 0) return;
        _queueIndex = index;
        await StartPlaybackAsync(track);
    }

    private async Task StartPlaybackAsync(TrackItem track)
    {
        // Report the previous track's remaining listening time before switching.
        FlushListenTime(force: true);
        _listenClock.Stop();
        _pendingListenSeconds = 0;

        var requestId = ++_playRequestId;
        CurrentTrack = track;
        PositionSeconds = 0;
        DurationSeconds = track.DurationSeconds;
        CurrentArtwork = null;
        _ = LoadArtworkAsync(track.ArtworkUrl, requestId);
        try
        {
            var url = await _api.GetStreamUrlAsync(track.Id);
            if (requestId != _playRequestId) return;
            _audio.Play(url);
            IsPlaying = true;
            StatusMessage = $"Playing {track.Title}";
            _ = _api.RecordPlaybackAsync(track.Id, 0, false).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            IsPlaying = false;
        }
    }

    private async Task LoadArtworkAsync(string? relativeUrl, int requestId)
    {
        var bytes = await _api.GetArtworkAsync(relativeUrl);
        if (bytes is null || requestId != _playRequestId) return;
        await _dispatcher.InvokeAsync(() =>
        {
            if (requestId != _playRequestId) return;
            try
            {
                var image = new BitmapImage();
                using var stream = new System.IO.MemoryStream(bytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                CurrentArtwork = image;
            }
            catch
            {
                CurrentArtwork = null;
            }
        });
    }

    private async Task TogglePlayPauseAsync()
    {
        if (CurrentTrack is null)
        {
            if (Queue.Count > 0) await StartPlaybackAsync(Queue[0]);
            return;
        }
        if (_audio.IsPlaying) _audio.Pause();
        else _audio.Resume();
        IsPlaying = _audio.IsPlaying;
        await Task.CompletedTask;
    }

    private async Task NextAsync(bool userInitiated)
    {
        if (Queue.Count == 0)
        {
            if (!userInitiated) IsPlaying = false;
            return;
        }
        if (RepeatMode == 2 && CurrentTrack is not null)
        {
            await StartPlaybackAsync(CurrentTrack);
            return;
        }
        int nextIndex;
        if (Shuffle)
        {
            if (Queue.Count == 1) nextIndex = 0;
            else
            {
                var random = Random.Shared;
                do nextIndex = random.Next(Queue.Count);
                while (nextIndex == _queueIndex);
            }
        }
        else
        {
            nextIndex = _queueIndex + 1;
            if (nextIndex >= Queue.Count)
            {
                if (RepeatMode == 1 || userInitiated) nextIndex = 0;
                else { IsPlaying = false; return; }
            }
        }
        _queueIndex = nextIndex;
        await StartPlaybackAsync(Queue[nextIndex]);
    }

    private async Task PreviousAsync()
    {
        if (Queue.Count == 0) return;
        int index = _queueIndex - 1;
        if (index < 0) index = RepeatMode == 1 ? Queue.Count - 1 : 0;
        _queueIndex = index;
        await StartPlaybackAsync(Queue[index]);
    }

    private async Task ToggleFavoriteAsync(TrackItem track)
    {
        try
        {
            var newValue = !track.IsFavorite;
            await _api.ToggleFavoriteAsync(track.Id, newValue);
            track.IsFavorite = newValue;
            OnPropertyChanged(nameof(CurrentTrack));
            foreach (var item in new[] { Tracks, DetailTracks, RecentlyAdded, RecentlyPlayed, MostPlayed, FavoriteTracks, SearchTracks, Queue }.SelectMany(x => x))
            {
                if (item.Id == track.Id && !ReferenceEquals(item, track)) item.IsFavorite = newValue;
            }
            if (newValue && View == PlayerView.Favorites && !FavoriteTracks.Any(x => x.Id == track.Id)) FavoriteTracks.Add(track);
            if (!newValue && View == PlayerView.Favorites) FavoriteTracks.Remove(track);
            StatusMessage = newValue ? "Added to favorites" : "Removed from favorites";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task CreatePlaylistAsync(string name, string? description, bool isPublic)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _api.CreatePlaylistAsync(name.Trim(), description, isPublic);
            await LoadPlaylistsAsync();
            StatusMessage = "Playlist created";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task DeletePlaylistAsync(Guid playlistId)
    {
        try
        {
            await _api.DeletePlaylistAsync(playlistId);
            CloseDetail();
            await LoadPlaylistsAsync();
            StatusMessage = "Playlist deleted";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task AddCurrentTrackToPlaylistAsync(Guid playlistId)
    {
        if (CurrentTrack is null) return;
        try
        {
            await _api.AddTrackToPlaylistAsync(playlistId, CurrentTrack.Id);
            StatusMessage = "Added to playlist";
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            StatusMessage = "This track is already in that playlist";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task RemoveFromDetailPlaylistAsync(TrackItem track)
    {
        if (_detailPlaylistId is not { } playlistId) return;
        try
        {
            await _api.RemoveTrackFromPlaylistAsync(playlistId, track.Id);
            DetailTracks.Remove(track);
            OnPropertyChanged(nameof(DetailSubtitle));
            StatusMessage = "Removed from playlist";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task SignOutAsync()
    {
        FlushListenTime(force: true);
        _listenClock.Stop();
        await _api.LogoutAsync();
        SessionStore.Clear();
        _audio.Stop();
        SignedOut?.Invoke();
    }

    public async Task DeleteOpenPlaylistAsync()
    {
        if (_detailPlaylistId is not { } id) return;
        await DeletePlaylistAsync(id);
    }

    public async Task<IReadOnlyList<PlaylistItem>> GetPlaylistsForPickerAsync()
    {
        try
        {
            var playlists = await _api.GetPlaylistsAsync();
            Playlists.Clear();
            foreach (var playlist in playlists) Playlists.Add(playlist);
            return playlists;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return [];
        }
    }

    public string QueueSummary => Queue.Count switch
    {
        0 => "The queue is empty",
        1 => "1 track in the queue",
        _ => $"{Queue.Count} tracks in the queue"
    };

    // ---- Seek / volume helpers (called from code-behind) ---------------------------------

    public void BeginSeek() => _seeking = true;

    public void EndSeek(double seconds)
    {
        _seeking = false;
        if (CurrentTrack is null) return;
        _audio.Time = (long)(Math.Max(0, seconds) * 1000);
        PositionSeconds = Math.Max(0, seconds);
    }

    private void SavePreferences()
    {
        _preferences.Volume = Volume;
        _preferences.Shuffle = Shuffle;
        _preferences.RepeatMode = RepeatMode;
        SettingsStore.Save(_preferences);
    }

    // ---- Audio event marshalling --------------------------------------------------------

    private void OnAudioTimeChanged(long milliseconds)
    {
        if (_seeking) return;
        var seconds = milliseconds / 1000.0;
        _dispatcher.BeginInvoke(() =>
        {
            if (!_seeking) PositionSeconds = seconds;
            AccumulateListenTime();
            if (_pendingListenSeconds >= ListenFlushSeconds) FlushListenTime(force: false);
        });
    }

    private void OnAudioLengthChanged(long milliseconds)
    {
        var seconds = milliseconds / 1000.0;
        _dispatcher.BeginInvoke(() => { if (seconds > 0) DurationSeconds = seconds; });
    }

    private void OnAudioPlayingChanged(bool playing)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsPlaying = playing;
            if (playing)
            {
                _listenClock.Start();
            }
            else
            {
                AccumulateListenTime();
                _listenClock.Stop();
                FlushListenTime(force: true);
            }
        });
    }

    // ---- Listening-time accounting ------------------------------------------------------

    /// <summary>Adds wall-clock time spent playing to the pending report total.</summary>
    private void AccumulateListenTime()
    {
        if (!_listenClock.IsRunning) return;
        var elapsed = _listenClock.Elapsed.TotalSeconds;
        _listenClock.Restart();
        if (elapsed > 0) _pendingListenSeconds += elapsed;
    }

    /// <summary>
    /// Sends accumulated listening time to the server. Called on the UI thread; the network
    /// call runs in the background so playback is never blocked.
    /// </summary>
    private void FlushListenTime(bool force)
    {
        AccumulateListenTime();
        if (CurrentTrack is not { } track) return;
        var threshold = force ? 1.0 : ListenFlushSeconds;
        if (_pendingListenSeconds < threshold) return;
        var seconds = Math.Round(_pendingListenSeconds, 1);
        var position = PositionSeconds;
        _pendingListenSeconds = 0;
        _ = ReportListenTimeAsync(track.Id, seconds, position);
    }

    private async Task ReportListenTimeAsync(Guid trackId, double listenedSeconds, double positionSeconds)
    {
        try
        {
            await _api.RecordPlaybackAsync(trackId, positionSeconds, completed: false, listenedSeconds);
        }
        catch (ApiException ex) when (ex.StatusCode == 403)
        {
            // The account was deactivated (expired, or a quota was used up) while playing.
            _ = _dispatcher.BeginInvoke(() =>
            {
                _listenClock.Stop();
                _pendingListenSeconds = 0;
                _audio.Stop();
                IsPlaying = false;
                ErrorMessage = ex.Message;
            });
        }
        catch
        {
            // Reporting must never interrupt playback; the next interval retries.
        }
    }

    private void OnAudioEndReached()
    {
        _dispatcher.BeginInvoke(async () =>
        {
            if (RepeatMode == 2 && CurrentTrack is not null)
            {
                await StartPlaybackAsync(CurrentTrack);
                return;
            }
            await NextAsync(userInitiated: false);
            if (CurrentTrack is not null && IsPlaying)
            {
                _ = _api.RecordPlaybackAsync(CurrentTrack.Id, DurationSeconds, true).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        });
    }

    private void OnAudioError(string message)
    {
        _dispatcher.BeginInvoke(() =>
        {
            ErrorMessage = message;
            IsPlaying = false;
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = $"Cannot reach the server at {_api.BaseUrl}. Check the address and that MusicHub is running.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "The server took too long to respond.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private IEnumerable<TrackItem> TrackContext()
    {
        if (IsDetailOpen) return DetailTracks;
        if (IsSearchView) return SearchTracks;
        return Tracks;
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopStoragePolling();
        FlushListenTime(force: true);
        _audio.Dispose();
    }
}
