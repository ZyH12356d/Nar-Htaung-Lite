using Plugin.Maui.Audio;
using MusicPlayer.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Storage;
using System.Timers;
using Timer = System.Timers.Timer;

namespace MusicPlayer.Services
{
    public class PlayerService : INotifyPropertyChanged, IDisposable
    {
        private readonly IAudioManager _audioManager;
        private readonly IMediaNotificationService _notificationService;
        private IAudioPlayer? _audioPlayer;
        private List<Song> _playlist = new();
        private Song? _currentSong;
        private bool _isPlaying;
        private bool _isShuffle;
        private bool _isRepeat;
        private bool _isPlayerExpanded;
        private double _currentPosition;
        private double _duration;
        private System.Timers.Timer _progressTimer;
        private Random _rng = new Random();
        private bool _isPositionRestored = false;

        public PlayerService(IAudioManager audioManager, IMediaNotificationService notificationService)
        {
            _audioManager = audioManager;
            _notificationService = notificationService;
            _progressTimer = new Timer(500); // More frequent updates for smoother UI
            _progressTimer.AutoReset = true;
            _progressTimer.Elapsed += (s, e) => UpdateProgress();
            
            // Try to load the last played song from preferences
            var lastPath = Preferences.Get("LastSongPath", string.Empty);
            if (!string.IsNullOrEmpty(lastPath) && File.Exists(lastPath))
            {
                // We'll let Home.razor populate the playlist first, 
                // but we can set the initial current song if we find it.
            }
        }

        public List<Song> Playlist
        {
            get => _playlist;
            set { 
                _playlist = value; 
                OnPropertyChanged();
                
                // If we have a stored path, try to find the actual Song object
                var lastPath = Preferences.Get("LastSongPath", string.Empty);
                if (CurrentSong == null && !string.IsNullOrEmpty(lastPath))
                {
                    CurrentSong = _playlist.FirstOrDefault(s => s.FilePath == lastPath);
                }
            }
        }

        public Song? CurrentSong
        {
            get => _currentSong;
            set { 
                _currentSong = value; 
                OnPropertyChanged(); 
                if (_currentSong != null)
                {
                    Preferences.Set("LastSongPath", _currentSong.FilePath);
                    
                    // If we are restoring the song on startup, load the stored position
                    if (!_isPositionRestored)
                    {
                        var lastPath = Preferences.Get("LastSongPath", string.Empty);
                        if (_currentSong.FilePath == lastPath)
                        {
                            CurrentPosition = Preferences.Get("LastSongPosition", 0.0);
                        }
                    }
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set { _isPlaying = value; OnPropertyChanged(); }
        }

        public bool IsShuffle
        {
            get => _isShuffle;
            set { _isShuffle = value; OnPropertyChanged(); }
        }

        public bool IsRepeat
        {
            get => _isRepeat;
            set { _isRepeat = value; OnPropertyChanged(); }
        }

        public bool IsPlayerExpanded
        {
            get => _isPlayerExpanded;
            set { _isPlayerExpanded = value; OnPropertyChanged(); }
        }

        public double CurrentPosition
        {
            get => _currentPosition;
            set { _currentPosition = value; OnPropertyChanged(); }
        }

        public double Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public async Task PlaySong(Song song)
        {
            try 
            {
                if (!File.Exists(song.FilePath))
                {
                    Console.WriteLine($"File not found: {song.FilePath}");
                    return;
                }

                // Clean up previous player
                if (_audioPlayer != null)
                {
                    _progressTimer.Stop();
                    _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
                    _audioPlayer.Stop();
                    _audioPlayer.Dispose();
                }

                CurrentSong = song;
                
                // Create native player from file stream
                var stream = File.OpenRead(song.FilePath);
                _audioPlayer = _audioManager.CreatePlayer(stream);
                _audioPlayer.PlaybackEnded += OnPlaybackEnded;

                Duration = _audioPlayer.Duration;

                // If it is the startup restored song, seek to the saved position
                if (!_isPositionRestored)
                {
                    var lastPath = Preferences.Get("LastSongPath", string.Empty);
                    if (song.FilePath == lastPath)
                    {
                        var savedPosition = Preferences.Get("LastSongPosition", 0.0);
                        if (savedPosition > 0 && savedPosition < Duration)
                        {
                            _audioPlayer.Seek(savedPosition);
                            CurrentPosition = savedPosition;
                        }
                    }
                    _isPositionRestored = true;
                }
                else
                {
                    // For a normal new song play, start from 0 and clear saved position
                    CurrentPosition = 0;
                    Preferences.Set("LastSongPosition", 0.0);
                }

                _audioPlayer.Play();
                IsPlaying = true;
                _progressTimer.Start();

                // Update notification
                _notificationService.UpdateMetadata(song.Title, song.Author, song.ThumbnailDataUrl ?? "", Duration);
                _notificationService.UpdatePlaybackStatus(true, CurrentPosition);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing song natively: {ex.Message}");
                IsPlaying = false;
            }
        }

        private void UpdateProgress()
        {
            if (_audioPlayer != null && IsPlaying)
            {
                var newPos = _audioPlayer.CurrentPosition;
                
                // If it's the same second, don't trigger property change
                if (Math.Floor(newPos) != Math.Floor(_currentPosition))
                {
                    _currentPosition = newPos;
                    OnPropertyChanged(nameof(CurrentPosition));

                    // Save position to preferences so we can restore it if killed
                    Preferences.Set("LastSongPosition", _currentPosition);
                }
            }
        }

        private void OnPlaybackEnded(object? sender, EventArgs e)
        {
            _progressTimer.Stop();
            _ = HandleTrackEnded();
        }

        public Task TogglePlayPause()
        {
            if (_audioPlayer == null && CurrentSong != null)
            {
                // If it's loaded from Preferences but not yet "played", we need to initialize it
                _ = PlaySong(CurrentSong);
                return Task.CompletedTask;
            }

            if (_audioPlayer == null) return Task.CompletedTask;

            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
                IsPlaying = false;
                _progressTimer.Stop();
                _notificationService.UpdatePlaybackStatus(false, _audioPlayer.CurrentPosition);
            }
            else
            {
                _audioPlayer.Play();
                IsPlaying = true;
                _progressTimer.Start();
                _notificationService.UpdatePlaybackStatus(true, _audioPlayer.CurrentPosition);
            }

            return Task.CompletedTask;
        }

        public void Seek(double seconds)
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.Seek(seconds);
                CurrentPosition = seconds;
                _notificationService.UpdatePlaybackStatus(IsPlaying, seconds);
            }
        }

        public async Task PlayNext()
        {
            if (!_playlist.Any()) return;

            Song nextSong;
            if (IsShuffle)
            {
                nextSong = _playlist[_rng.Next(_playlist.Count)];
            }
            else
            {
                int currentIndex = _playlist.IndexOf(CurrentSong!);
                int nextIndex = (currentIndex + 1) % _playlist.Count;
                nextSong = _playlist[nextIndex];
            }

            await PlaySong(nextSong);
        }

        public async Task PlayPrevious()
        {
            if (!_playlist.Any()) return;

            int currentIndex = _playlist.IndexOf(CurrentSong!);
            int prevIndex = (currentIndex - 1 + _playlist.Count) % _playlist.Count;
            await PlaySong(_playlist[prevIndex]);
        }

        public async Task HandleTrackEnded()
        {
            Preferences.Set("LastSongPosition", 0.0);
            
            if (IsRepeat)
            {
                if (CurrentSong != null) await PlaySong(CurrentSong);
            }
            else
            {
                await PlayNext();
            }
        }

        public void Dispose()
        {
            _progressTimer.Stop();
            _progressTimer.Dispose();
            if (_audioPlayer != null)
            {
                _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
                _audioPlayer.Dispose();
            }
        }
    }
}
