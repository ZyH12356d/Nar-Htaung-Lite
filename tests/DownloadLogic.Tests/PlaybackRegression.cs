using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using MusicPlayer.Models;
using MusicPlayer.Services;
using Plugin.Maui.Audio;

internal static class PlaybackRegression
{
    public static async Task SwitchAsync()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Player.PlayFromCollectionAsync(null, test.Songs[0]);
        var oldNative = test.Audio.Current!;
        await test.Player.SelectCollectionAsync(test.First.Id);
        Check(oldNative.Disposed && !oldNative.IsPlaying, "The old collection kept playing.");
        test.CheckFirstCollection();
        for (var i = 0; i < 12; i++)
        {
            await test.Player.PlayNext(); test.CheckFirstCollection();
            await test.Player.PlayPrevious(); test.CheckFirstCollection();
            await test.Player.HandleTrackEnded(); test.CheckFirstCollection();
        }
        var current = test.Player.CurrentSong;
        await test.Player.PlaySong(test.Songs[0]);
        Check(test.Player.CurrentSong == current, "A stale queue row played a song from another collection.");
        // A different Song instance for the same file must still be found in the active queue.
        await test.Player.PlaySong(new Song { FilePath = test.Songs[3].FilePath });
        Check(test.Player.CurrentSong?.FilePath == test.Songs[3].FilePath, "Queue membership used object identity instead of the saved file.");
        await test.Player.PlayPrevious();
        Check(test.Player.CurrentSong?.FilePath == test.Songs[1].FilePath, "Previous did not use the active collection order.");
        await test.Player.SelectCollectionAsync(null);
        Check(test.Player.Playlist.Count == test.Songs.Count, "All downloads did not restore the full queue.");
        Check(!test.Audio.UsedOffUiThread, "Native playback was updated off the UI thread.");
    }

    public static async Task RefreshAsync()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Player.PlayFromCollectionAsync(test.First.Id, test.Songs[1]);
        test.Player.Seek(27);
        var native = test.Audio.Current;
        await test.Library.CreateCollectionAsync("Unrelated", new[] { test.Songs[0].FilePath });
        await test.Player.InitializeAsync(); // Home can initialize again without taking queue ownership.
        test.CheckFirstCollection();
        Check(test.Audio.Current == native && test.Player.CurrentPosition == 27, "A library refresh restarted collection playback.");
        var added = new Song { Title = "Downloaded later", FilePath = Path.Combine(test.Files.Root, "later.m4a"), ThumbnailDataUrl = "data:image/png;base64,fixture" };
        await File.WriteAllTextAsync(added.FilePath, "audio");
        await MainThread.InvokeOnMainThreadAsync(() => test.Library.AddSongAsync(added));
        test.CheckFirstCollection();
        await test.Library.AddSongsToCollectionAsync(test.First.Id, new[] { added.FilePath });
        Check(test.Player.Playlist.Count == 3 && test.Player.Playlist.Contains(added), "Adding a song did not update the active queue.");
        await test.Library.RemoveSongFromCollectionAsync(test.First.Id, test.Songs[1].FilePath);
        Check(test.Player.CurrentSong?.FilePath != test.Songs[1].FilePath && test.Player.IsPlaying && File.Exists(test.Songs[1].FilePath), "Removing the current song did not continue inside the collection.");
        await MainThread.InvokeOnMainThreadAsync(() => test.Library.DeleteSongAsync(test.Library.Songs.Single(s => s.FilePath == test.Songs[3].FilePath)));
        Check(test.Player.Playlist.All(s => s.FilePath != test.Songs[3].FilePath), "Deleting a song left it in the play queue.");
        await test.Library.RemoveSongFromCollectionAsync(test.First.Id, added.FilePath);
        Check(test.Player.Playlist.Count == 0 && test.Player.CurrentSong is null && !test.Player.IsPlaying && test.Audio.Current!.Disposed, "An empty collection kept an old song playing.");
        await test.Player.PlayNext();
        await test.Player.PlayPrevious();
        await test.Player.HandleTrackEnded();
        Check(test.Player.CurrentSong is null, "An empty collection fell back to all downloads.");
    }

    public static async Task RestoreAsync()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Player.PlayFromCollectionAsync(test.First.Id, test.Songs[3]);
        test.Player.Seek(41);
        await MainThread.InvokeOnMainThreadAsync(test.Player.Dispose);
        using var restored = new PlayerService(test.Audio, new Notification(), test.Library);
        await restored.InitializeAsync();
        Check(restored.ActiveCollectionId == test.First.Id && restored.Playlist.Count == 2 && restored.CurrentSong?.FilePath == test.Songs[3].FilePath, "Restarting restored the wrong collection queue.");
        Check(!restored.IsPlaying && restored.CurrentPosition == 41, "Startup lost the saved position or started audio automatically.");
        await restored.TogglePlayPause();
        Check(restored.IsPlaying && test.Audio.Current!.CurrentPosition == 41, "Resume did not restore the matching song position.");
        await restored.PlayFromCollectionAsync(null, test.Songs[0]);
        Check(restored.CurrentPosition == 0, "A new song reused another song's saved position.");
        await MainThread.InvokeOnMainThreadAsync(restored.Dispose);
        Preferences.Set("PlaybackCollectionId", test.First.Id);
        Preferences.Set("LastSongPath", test.Songs[0].FilePath);
        Preferences.Set("LastSongPosition", 89.0);
        using var stale = new PlayerService(test.Audio, new Notification(), test.Library);
        await stale.InitializeAsync();
        Check(stale.CurrentSong is null && stale.Playlist.Count == 2, "A stale preference restored a song outside the saved collection.");
        await stale.SelectCollectionAsync(test.First.Id);
        Check(stale.CurrentSong?.FilePath == test.Songs[1].FilePath && !stale.IsPlaying, "Selecting a collection while paused did not select its own first song.");
        await MainThread.InvokeOnMainThreadAsync(stale.Dispose);
    }

    public static async Task ShuffleAsync()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Player.PlayFromCollectionAsync(test.First.Id, shuffle: true);
        Check(test.Player.IsShuffle && test.Player.IsPlaying, "Collection shuffle did not start playback.");
        for (var i = 0; i < 30; i++)
        {
            var previous = test.Player.CurrentSong?.FilePath;
            await test.Player.PlayNext();
            test.CheckFirstCollection();
            Check(test.Player.CurrentSong?.FilePath != previous, "Shuffle immediately repeated the same song despite other available songs.");
            await test.Player.PlayPrevious(); test.CheckFirstCollection();
        }
        test.Player.IsRepeat = true;
        var repeated = test.Player.CurrentSong?.FilePath;
        await test.Player.HandleTrackEnded();
        Check(test.Player.CurrentSong?.FilePath == repeated && test.Player.CurrentPosition == 0, "Repeat escaped the active collection or resumed at the old position.");
        var empty = await test.Library.CreateCollectionAsync("Empty", Array.Empty<string>());
        await test.Player.PlayFromCollectionAsync(empty.Id, shuffle: true);
        Check(test.Player.CurrentSong is null && !test.Player.IsPlaying, "Shuffling an empty collection retained audio from another collection.");
        await test.Player.PlayFromCollectionAsync(null, shuffle: true);
        Check(test.Player.Playlist.Count == test.Songs.Count && test.Player.CurrentSong is not null, "Home shuffle failed to select all downloads.");
    }

    public static async Task NativeFailureAsync()
    {
        await using var test = await Fixture.CreateAsync();
        await test.Player.PlayFromCollectionAsync(null, test.Songs[0]);
        var previous = test.Audio.Current!;
        test.Audio.FailNext = true;
        await test.Player.SelectCollectionAsync(test.First.Id);
        test.CheckFirstCollection();
        Check(previous.Disposed && !previous.IsPlaying && !test.Player.IsPlaying && test.Player.Duration == 0, "A native playback failure kept the old collection playing or displayed its duration.");
        await test.Player.PlayNext();
        test.CheckFirstCollection();
        Check(test.Player.IsPlaying, "Playback could not recover inside the selected collection.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Fixture : IAsyncDisposable
    {
        public DownloadCase Files { get; } = new();
        public List<Song> Songs { get; } = new();
        public LibraryService Library { get; private set; } = default!;
        public PlayerService Player { get; private set; } = default!;
        public MusicCollection First { get; private set; } = default!;
        public AudioManager Audio { get; } = new();
        private readonly HttpClient _http = new(new Handler());
        public static async Task<Fixture> CreateAsync()
        {
            var test = new Fixture();
            Preferences.Clear();
            FileSystem.AppDataDirectory = test.Files.Root;
            for (var i = 0; i < 5; i++)
            {
                var song = new Song { Title = $"Song {i}", FilePath = Path.Combine(test.Files.Root, $"song{i}.m4a"), ThumbnailDataUrl = "data:image/png;base64,fixture" };
                await File.WriteAllTextAsync(song.FilePath, "audio");
                test.Songs.Add(song);
            }
            await File.WriteAllTextAsync(Path.Combine(test.Files.Root, "songs_cache.json"), JsonSerializer.Serialize(test.Songs));
            test.Library = new LibraryService(new CoverArtService(test._http, NullLogger<CoverArtService>.Instance));
            await test.Library.LoadLibraryAsync();
            await test.Library.RefreshArtworkAsync();
            test.First = await test.Library.CreateCollectionAsync("Two favorites", new[] { test.Songs[1].FilePath, test.Songs[3].FilePath });
            test.Player = new PlayerService(test.Audio, new Notification(), test.Library);
            await test.Player.InitializeAsync();
            return test;
        }
        public void CheckFirstCollection()
        {
            var expected = new[] { Songs[1].FilePath, Songs[3].FilePath };
            Check(Player.ActiveCollectionId == First.Id && Player.Playlist.Select(s => s.FilePath).SequenceEqual(expected), "The active queue leaked songs from All downloads.");
            Check(Player.CurrentSong is not null && expected.Contains(Player.CurrentSong.FilePath), "Playback escaped the collection.");
        }
        public async ValueTask DisposeAsync()
        {
            await MainThread.InvokeOnMainThreadAsync(Player.Dispose);
            _http.Dispose();
            Files.Dispose();
        }
    }
    private sealed class AudioManager : IAudioManager
    {
        public NativePlayer? Current { get; private set; }
        public bool UsedOffUiThread { get; private set; }
        public bool FailNext { get; set; }
        public IAudioPlayer CreatePlayer(Stream stream)
        {
            UsedOffUiThread |= !MainThread.IsMainThread;
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Simulated native decoder failure"); }
            return Current = new NativePlayer();
        }
    }
    private sealed class NativePlayer : IAudioPlayer
    {
        public event EventHandler? PlaybackEnded { add { } remove { } }
        public double Duration => 180;
        public double CurrentPosition { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool Disposed { get; private set; }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public void Seek(double position) => CurrentPosition = position;
        public void Dispose() { Disposed = true; IsPlaying = false; }
    }
    private sealed class Notification : IMediaNotificationService
    {
        public void UpdateMetadata(string title, string author, string thumbnailUrl, double durationSeconds) { }
        public void UpdatePlaybackStatus(bool isPlaying, double positionSeconds) { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
