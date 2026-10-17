using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MusicPlayer.Models;
using YoutubeExplode.Common;
using YoutubeExplode.Search;

namespace MusicPlayer.Services;

public class LibraryService
{
    public ObservableCollection<Song> Songs { get; } = new(Fixtures.Songs());
    public ObservableCollection<MusicCollection> Collections { get; } = new();
    public string CollectionError => "";
    public event Action? LibraryUpdated;
    public LibraryService()
    {
        Collections.Add(new MusicCollection { Id = "night", Name = "Late night", SongPaths = Songs.Take(2).Select(s => s.FilePath).ToList() });
        Collections.Add(new MusicCollection { Id = "morning", Name = "Sunday mornings", SongPaths = Songs.Skip(2).Take(2).Select(s => s.FilePath).ToList() });
        Collections.Add(new MusicCollection { Id = "travel", Name = "On the move", SongPaths = Songs.Skip(4).Take(2).Select(s => s.FilePath).ToList() });
        Collections.Add(new MusicCollection { Id = "empty", Name = "A new beginning", SongPaths = new() });
    }
    public Task LoadLibraryAsync() => Task.CompletedTask;
    public IEnumerable<Song> GetCollectionSongs(string? id) => id is null ? Songs : Songs.Where(s => Collections.FirstOrDefault(c => c.Id == id)?.SongPaths.Contains(s.FilePath) == true);
    public Task<MusicCollection> CreateCollectionAsync(string name, IEnumerable<string> paths)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("Use a collection name between 1 and 60 characters.");
        if (Collections.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("You already have a collection with that name.");
        var collection = new MusicCollection { Name = name, SongPaths = paths.Distinct().ToList() };
        Collections.Add(collection); LibraryUpdated?.Invoke(); return Task.FromResult(collection);
    }
    public Task AddSongsToCollectionAsync(string id, IEnumerable<string> paths) { var c = Collections.Single(c => c.Id == id); c.SongPaths = c.SongPaths.Concat(paths).Distinct().ToList(); LibraryUpdated?.Invoke(); return Task.CompletedTask; }
    public Task RemoveSongFromCollectionAsync(string id, string path) { Collections.Single(c => c.Id == id).SongPaths.Remove(path); LibraryUpdated?.Invoke(); return Task.CompletedTask; }
    public Task DeleteSongAsync(Song song) { Songs.Remove(song); foreach (var c in Collections) c.SongPaths.Remove(song.FilePath); LibraryUpdated?.Invoke(); return Task.CompletedTask; }
    public void Clear() { Songs.Clear(); Collections.Clear(); LibraryUpdated?.Invoke(); }
}
public class PlayerService : INotifyPropertyChanged, IDisposable
{
    private readonly LibraryService _library;
    private Song? _song;
    private bool _playing = true, _shuffle, _repeat, _expanded;
    private double _position = 62;
    public IReadOnlyList<Song> Playlist { get; private set; } = Array.Empty<Song>();
    public string? ActiveCollectionId { get; private set; }
    public string ActiveCollectionName => ActiveCollectionId is null ? "All downloads" : _library.Collections.FirstOrDefault(c => c.Id == ActiveCollectionId)?.Name ?? "Collection";
    public Song? CurrentSong { get => _song; private set { _song = value; Changed(); } }
    public bool IsPlaying { get => _playing; set { _playing = value; Changed(); } }
    public bool IsShuffle { get => _shuffle; set { _shuffle = value; Changed(); } }
    public bool IsRepeat { get => _repeat; set { _repeat = value; Changed(); } }
    public bool IsPlayerExpanded { get => _expanded; set { _expanded = value; Changed(); } }
    public double Duration => 243;
    public double CurrentPosition { get => _position; private set { _position = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public PlayerService(LibraryService library) { _library = library; ReplaceQueue(); _song = Playlist.FirstOrDefault(); library.LibraryUpdated += LibraryChanged; }
    public Task InitializeAsync() => Task.CompletedTask;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void ReplaceQueue()
    {
        Playlist = _library.GetCollectionSongs(ActiveCollectionId).ToList().AsReadOnly();
        Changed(nameof(Playlist)); Changed(nameof(ActiveCollectionId)); Changed(nameof(ActiveCollectionName));
    }
    private void Reconcile(bool selectFirst)
    {
        if (Playlist.Any(s => s.FilePath == CurrentSong?.FilePath)) return;
        if (CurrentSong is null && !selectFirst) return;
        CurrentSong = Playlist.FirstOrDefault(); CurrentPosition = 0;
        if (CurrentSong is null) { IsPlaying = false; IsPlayerExpanded = false; }
    }
    private void LibraryChanged() { ReplaceQueue(); Reconcile(false); }
    public Task SelectCollectionAsync(string? id) { ActiveCollectionId = id; ReplaceQueue(); Reconcile(true); return Task.CompletedTask; }
    public Task PlayFromCollectionAsync(string? id, Song? song = null, bool shuffle = false)
    {
        ActiveCollectionId = id; IsShuffle = shuffle; ReplaceQueue();
        var selected = song is not null ? Playlist.FirstOrDefault(s => s.FilePath == song.FilePath) : Playlist.Count == 0 ? null : Playlist[shuffle ? Random.Shared.Next(Playlist.Count) : 0];
        if (selected is null) { Reconcile(true); return Task.CompletedTask; }
        return PlaySong(selected);
    }
    public Task PlaySong(Song song)
    {
        var member = Playlist.FirstOrDefault(s => s.FilePath == song.FilePath);
        if (member is not null) { CurrentSong = member; IsPlaying = true; CurrentPosition = 0; }
        return Task.CompletedTask;
    }
    public Task TogglePlayPause() { if (CurrentSong is not null) IsPlaying = !IsPlaying; return Task.CompletedTask; }
    public Task PlayNext()
    {
        if (Playlist.Count == 0) return Task.CompletedTask;
        var index = Playlist.ToList().FindIndex(s => s.FilePath == CurrentSong?.FilePath);
        var choices = Playlist.Where(s => s.FilePath != CurrentSong?.FilePath).ToList();
        return PlaySong(IsShuffle && choices.Count > 0 ? choices[Random.Shared.Next(choices.Count)] : Playlist[(index + 1) % Playlist.Count]);
    }
    public Task PlayPrevious()
    {
        if (Playlist.Count == 0) return Task.CompletedTask;
        var index = Playlist.ToList().FindIndex(s => s.FilePath == CurrentSong?.FilePath);
        return PlaySong(Playlist[index < 0 ? 0 : (index - 1 + Playlist.Count) % Playlist.Count]);
    }
    public void Seek(double value) => CurrentPosition = value;
    public void Dispose() => _library.LibraryUpdated -= LibraryChanged;
}
public class DownloadService
{
    public ObservableCollection<DownloadItem> Downloads { get; } = new();
    public event Action? DownloadsChanged;
    public int ActiveDownloadCount => Downloads.Count(d => d.Status is DownloadStatus.Waiting or DownloadStatus.Processing or DownloadStatus.Downloading);
    public DownloadService()
    {
        var songs = Fixtures.Songs();
        for (var i = 0; i < 4; i++)
            Downloads.Add(new DownloadItem { Title = songs[i].Title, Author = songs[i].Author, ThumbnailUrl = songs[i].ArtworkSource, VideoId = "jNQXAC9IVRw", Progress = i == 0 ? 63 : 0, Status = i switch { 0 => DownloadStatus.Downloading, 1 => DownloadStatus.Completed, 2 => DownloadStatus.Waiting, _ => DownloadStatus.Failed }, ErrorMessage = i == 3 ? "Getting audio streams: This video is unavailable. Try another track or retry later." : "" });
    }
    public void ClearHistory() { foreach (var d in Downloads.Where(d => d.Status is DownloadStatus.Completed or DownloadStatus.Failed).ToList()) Downloads.Remove(d); DownloadsChanged?.Invoke(); }
    public void Retry(DownloadItem item) { item.Status = DownloadStatus.Waiting; item.ErrorMessage = ""; DownloadsChanged?.Invoke(); }
    public void Enqueue(VideoSearchResult video, IEnumerable<string>? collectionIds = null) { Downloads.Insert(0, new DownloadItem { VideoId = video.Id.Value, Title = video.Title, Author = video.Author.ChannelTitle, CollectionIds = collectionIds?.ToArray() ?? Array.Empty<string>(), Status = DownloadStatus.Waiting }); DownloadsChanged?.Invoke(); }
}
public static class Fixtures
{
    public static List<Song> Songs() =>
    [
        Song("Midnight on the way home", "The Quiet Hours", 0),
        Song("Rain over Yangon", "Sunday Collective", 1),
        Song("အမှတ်တရညများ", "ကိုယ်ပိုင်သံစဉ်", 2),
        Song("Everything in its right place — the long way around (Acoustic Session)", "The Quiet Hours", 3),
        Song("A little closer", "Sunday Collective", 4),
        new Song { Title = "An old favorite", Author = "Unknown artist", FilePath = "/preview/old.m4a" },
        Song("Still, the morning comes", "The Quiet Hours", 2)
    ];
    private static Song Song(string title, string author, int tone) => new() { Title = title, Author = author, FilePath = $"/preview/{tone}-{title}.m4a", ThumbnailDataUrl = Art(tone) };
    public static string Art(int tone)
    {
        string[] sky = ["#d79b72", "#46747a", "#9ba09c", "#6e6682", "#b7b887"];
        string[] hill = ["#493f3b", "#193d41", "#3c4b43", "#292a45", "#506248"];
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='500' height='500' viewBox='0 0 500 500'><defs><linearGradient id='a' x2='.4' y2='1'><stop stop-color='{sky[tone]}'/><stop offset='1' stop-color='{hill[tone]}'/></linearGradient><filter id='n'><feTurbulence baseFrequency='.7' numOctaves='3' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter></defs><rect width='500' height='500' fill='url(#a)'/><circle cx='{340-tone*28}' cy='160' r='72' fill='#f4dab3' opacity='.65'/><path d='M0 365 155 215 325 390 455 295 500 330V500H0Z' fill='{hill[tone]}' opacity='.8'/><path d='M0 425Q130 330 280 440T500 390V500H0Z' fill='#172c29' opacity='.7'/><rect width='500' height='500' filter='url(#n)' opacity='.045'/><path d='M35 36h430M35 464h430' stroke='#ffffff25'/><text x='36' y='443' fill='#f6f0df' font-family='sans-serif' font-size='12' letter-spacing='5'>NAR HTAUNG / SESSIONS</text></svg>";
        return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
    }
}
