using Plugin.Maui.Audio;
using MusicPlayer.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using Timer = System.Timers.Timer;

namespace MusicPlayer.Services;

public class PlayerService : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioManager _audioManager;
    private readonly IMediaNotificationService _notificationService;
    private readonly LibraryService _library;
    private readonly Timer _progressTimer = new(500) { AutoReset = true };
    private IAudioPlayer? _audioPlayer;
    private Stream? _audioStream;
    private IReadOnlyList<Song> _playlist = Array.Empty<Song>();
    private Song? _currentSong;
    private bool _isPlaying, _isShuffle, _isRepeat, _isPlayerExpanded, _initialized, _disposed;
    private double _currentPosition, _duration, _resumePosition;
    private Task? _initialization;

    public PlayerService(IAudioManager audioManager, IMediaNotificationService notificationService, LibraryService library)
    {
        _audioManager = audioManager;
        _notificationService = notificationService;
        _library = library;
        _library.LibraryUpdated += LibraryChanged;
        _progressTimer.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateProgress);
    }

    // Only the player changes the queue. Visiting Home or repairing artwork must not replace it.
    public IReadOnlyList<Song> Playlist => _playlist;
    public string? ActiveCollectionId { get; private set; }
    public string ActiveCollectionName => ActiveCollectionId is null ? "All downloads"
        : _library.Collections.FirstOrDefault(c => c.Id == ActiveCollectionId)?.Name ?? "Collection";
    public Song? CurrentSong { get => _currentSong; private set { _currentSong = value; OnPropertyChanged(); } }
    public bool IsPlaying { get => _isPlaying; private set { _isPlaying = value; OnPropertyChanged(); } }
    public bool IsShuffle { get => _isShuffle; set { _isShuffle = value; OnPropertyChanged(); } }
    public bool IsRepeat { get => _isRepeat; set { _isRepeat = value; OnPropertyChanged(); } }
    public bool IsPlayerExpanded { get => _isPlayerExpanded; set { _isPlayerExpanded = value; OnPropertyChanged(); } }
    public double CurrentPosition { get => _currentPosition; private set { _currentPosition = value; OnPropertyChanged(); } }
    public double Duration { get => _duration; private set { _duration = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await _library.LoadLibraryAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed) return;
            var savedCollection = Preferences.Get("PlaybackCollectionId", "");
            ActiveCollectionId = _library.Collections.Any(c => c.Id == savedCollection) ? savedCollection : null;
            ReplaceQueue();
            var lastPath = Preferences.Get("LastSongPath", "");
            CurrentSong = _playlist.FirstOrDefault(s => s.FilePath == lastPath);
            _resumePosition = CurrentSong is null ? 0 : Math.Max(0, Preferences.Get("LastSongPosition", 0.0));
            CurrentPosition = _resumePosition;
            _initialized = true;
        });
    }

    private async Task RunAsync(Action action)
    {
        await InitializeAsync();
        await MainThread.InvokeOnMainThreadAsync(() => { if (!_disposed) action(); });
    }

    public Task SelectCollectionAsync(string? collectionId) => RunAsync(() =>
    {
        ActiveCollectionId = collectionId;
        ReplaceQueue();
        ReconcileCurrentSong(selectFirst: true);
    });

    public Task PlayFromCollectionAsync(string? collectionId, Song? song = null, bool shuffle = false) => RunAsync(() =>
    {
        ActiveCollectionId = collectionId;
        IsShuffle = shuffle;
        ReplaceQueue();
        var selected = song is not null ? _playlist.FirstOrDefault(s => s.FilePath == song.FilePath)
            : _playlist.Count == 0 ? null : _playlist[shuffle ? Random.Shared.Next(_playlist.Count) : 0];
        if (selected is null) { ReconcileCurrentSong(selectFirst: true); return; }
        PlayCore(selected);
    });

    private void LibraryChanged() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!_initialized || _disposed) return;
        ReplaceQueue();
        ReconcileCurrentSong(selectFirst: false);
    });

    private void ReplaceQueue()
    {
        _playlist = Array.AsReadOnly(_library.GetCollectionSongs(ActiveCollectionId)
            .Where(s => File.Exists(s.FilePath)).DistinctBy(s => s.FilePath).ToArray());
        Preferences.Set("PlaybackCollectionId", ActiveCollectionId ?? "");
        OnPropertyChanged(nameof(Playlist));
        OnPropertyChanged(nameof(ActiveCollectionId));
        OnPropertyChanged(nameof(ActiveCollectionName));
    }

    private void ReconcileCurrentSong(bool selectFirst)
    {
        var retained = _playlist.FirstOrDefault(s => s.FilePath == CurrentSong?.FilePath);
        if (retained is not null)
        {
            if (!ReferenceEquals(retained, CurrentSong)) CurrentSong = retained;
            return;
        }
        if (CurrentSong is null && !selectFirst) return;
        var continuePlaying = IsPlaying;
        var next = _playlist.FirstOrDefault();
        if (next is not null && continuePlaying) PlayCore(next);
        else
        {
            StopNativePlayer();
            IsPlaying = false;
            CurrentSong = next;
            Duration = CurrentPosition = _resumePosition = 0;
            Preferences.Set("LastSongPath", next?.FilePath ?? "");
            Preferences.Set("LastSongPosition", 0.0);
            if (next is null) _notificationService.Clear();
            else
            {
                _notificationService.UpdateMetadata(next.Title, next.Author, next.ThumbnailDataUrl, 0);
                _notificationService.UpdatePlaybackStatus(false, 0);
            }
            if (next is null) IsPlayerExpanded = false;
        }
    }

    // Queue rows and skip controls may only play members of the active collection.
    public Task PlaySong(Song song) => RunAsync(() =>
    {
        var member = _playlist.FirstOrDefault(s => s.FilePath == song.FilePath);
        if (member is not null) PlayCore(member);
    });

    private void PlayCore(Song song, double resumeAt = 0)
    {
        StopNativePlayer();
        try
        {
            // Drop the previous source even if the new file cannot be opened or decoded.
            CurrentSong = song;
            Duration = CurrentPosition = _resumePosition = 0;
            Preferences.Set("LastSongPath", song.FilePath);
            Preferences.Set("LastSongPosition", 0.0);
            _audioStream = File.OpenRead(song.FilePath);
            _audioPlayer = _audioManager.CreatePlayer(_audioStream);
            _audioPlayer.PlaybackEnded += OnPlaybackEnded;
            Duration = _audioPlayer.Duration;
            CurrentPosition = resumeAt > 0 && resumeAt < Duration ? resumeAt : 0;
            if (CurrentPosition > 0) _audioPlayer.Seek(CurrentPosition);
            Preferences.Set("LastSongPosition", CurrentPosition);
            _audioPlayer.Play();
            IsPlaying = true;
            _progressTimer.Start();
            _notificationService.UpdateMetadata(song.Title, song.Author, song.ThumbnailDataUrl, Duration);
            _notificationService.UpdatePlaybackStatus(true, CurrentPosition);
        }
        catch (Exception ex)
        {
            StopNativePlayer();
            IsPlaying = false;
            _notificationService.Clear();
            Console.WriteLine($"Error playing song natively: {ex.Message}");
        }
    }

    private void StopNativePlayer()
    {
        _progressTimer.Stop();
        if (_audioPlayer is not null)
        {
            _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
            _audioPlayer.Stop();
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }
        _audioStream?.Dispose();
        _audioStream = null;
    }

    private void UpdateProgress()
    {
        if (_disposed || _audioPlayer is null || !IsPlaying) return;
        var position = _audioPlayer.CurrentPosition;
        if (Math.Floor(position) == Math.Floor(CurrentPosition)) return;
        CurrentPosition = position;
        Preferences.Set("LastSongPosition", position);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_disposed || (sender is not null && !ReferenceEquals(sender, _audioPlayer))) return;
        _progressTimer.Stop();
        TrackEndedCore();
    });

    public Task TogglePlayPause() => RunAsync(TogglePlayPauseCore);
    public Task HandleMediaActionAsync(string? action) => action switch
    {
        MediaPlaybackActions.Previous => PlayPrevious(),
        MediaPlaybackActions.Next => PlayNext(),
        MediaPlaybackActions.Toggle => TogglePlayPause(),
        MediaPlaybackActions.Play => RunAsync(() => { if (!IsPlaying) TogglePlayPauseCore(); }),
        MediaPlaybackActions.Pause => RunAsync(() => { if (IsPlaying) TogglePlayPauseCore(); }),
        _ => Task.CompletedTask
    };

    private void TogglePlayPauseCore()
    {
        if (_audioPlayer is null)
        {
            if (CurrentSong is { } song && _playlist.Any(s => s.FilePath == song.FilePath)) PlayCore(song, _resumePosition);
            return;
        }
        if (IsPlaying) { _audioPlayer.Pause(); _progressTimer.Stop(); IsPlaying = false; }
        else { _audioPlayer.Play(); _progressTimer.Start(); IsPlaying = true; }
        _notificationService.UpdatePlaybackStatus(IsPlaying, _audioPlayer.CurrentPosition);
    }

    public void Seek(double seconds)
    {
        if (_audioPlayer is null || _disposed) return;
        _audioPlayer.Seek(Math.Clamp(seconds, 0, Duration));
        CurrentPosition = _audioPlayer.CurrentPosition;
        Preferences.Set("LastSongPosition", CurrentPosition);
        _notificationService.UpdatePlaybackStatus(IsPlaying, CurrentPosition);
    }

    public Task PlayNext() => RunAsync(NextCore);
    private void NextCore()
    {
        if (_playlist.Count == 0) return;
        var index = _playlist.ToList().FindIndex(s => s.FilePath == CurrentSong?.FilePath);
        if (IsShuffle && _playlist.Count > 1)
        {
            var choices = _playlist.Where(s => s.FilePath != CurrentSong?.FilePath).ToArray();
            PlayCore(choices[Random.Shared.Next(choices.Length)]);
        }
        else PlayCore(_playlist[(index + 1) % _playlist.Count]);
    }

    public Task PlayPrevious() => RunAsync(() =>
    {
        if (_playlist.Count == 0) return;
        var index = _playlist.ToList().FindIndex(s => s.FilePath == CurrentSong?.FilePath);
        PlayCore(_playlist[index < 0 ? 0 : (index - 1 + _playlist.Count) % _playlist.Count]);
    });

    public Task HandleTrackEnded() => RunAsync(TrackEndedCore);
    private void TrackEndedCore()
    {
        if (IsRepeat && CurrentSong is { } song && _playlist.Any(s => s.FilePath == song.FilePath)) PlayCore(song);
        else NextCore();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _library.LibraryUpdated -= LibraryChanged;
        StopNativePlayer();
        _notificationService.Clear();
        _progressTimer.Dispose();
    }
}
