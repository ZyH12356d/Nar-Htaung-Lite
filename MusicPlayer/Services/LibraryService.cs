using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public partial class LibraryService(CoverArtService coverArt)
{
    private readonly string _cacheFilePath = Path.Combine(FileSystem.AppDataDirectory, "songs_cache.json");
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Task? _loadTask;
    private Task? _artworkRepairTask;

    public ObservableCollection<Song> Songs { get; } = new();
    public event Action? LibraryUpdated;

    public Task LoadLibraryAsync() => _loadTask ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        List<Song>? songs = null;
        try
        {
            if (File.Exists(_cacheFilePath))
                songs = JsonSerializer.Deserialize<List<Song>>(await File.ReadAllTextAsync(_cacheFilePath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not load the music library: {ex.Message}");
        }

        songs ??= await Task.Run(ScanDirectory);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Songs.Clear();
            foreach (var song in songs.Where(s => File.Exists(s.FilePath)))
                Songs.Add(song);
            LibraryUpdated?.Invoke();
        });
        await SaveCacheAsync();

        await LoadCollectionsAsync();

        // Display the library immediately; repair older missing images in the background.
        _ = RefreshArtworkAsync();
    }

    private static List<Song> ScanDirectory()
    {
        return Directory.EnumerateFiles(FileSystem.AppDataDirectory)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".m4a" or ".webm")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path =>
            {
                var song = new Song { FilePath = path, Title = Path.GetFileNameWithoutExtension(path), Author = "Unknown artist" };
                try
                {
                    using var file = TagLib.File.Create(path);
                    song.Title = string.IsNullOrWhiteSpace(file.Tag.Title) ? song.Title : file.Tag.Title;
                    song.Author = file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist ?? song.Author;
                    var picture = file.Tag.Pictures.FirstOrDefault();
                    if (picture is not null && CoverArtService.FromBytes(picture.Data.Data) is { } embedded)
                        song.ThumbnailDataUrl = embedded.DataUrl;
                }
                catch (Exception ex) { Console.WriteLine($"Could not read audio tags: {ex.Message}"); }
                return song;
            }).ToList();
    }

    public async Task CacheArtworkAsync(Song song, CoverArt artwork)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "artwork");
        Directory.CreateDirectory(directory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(song.FilePath)));
        var extension = artwork.MimeType switch { "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".jpg" };
        var path = Path.Combine(directory, key + extension);
        await File.WriteAllBytesAsync(path, artwork.Bytes);
        song.ThumbnailPath = path;
        song.ThumbnailDataUrl = artwork.DataUrl;
    }

    public Task RefreshArtworkAsync() => _artworkRepairTask is { IsCompleted: false }
        ? _artworkRepairTask : _artworkRepairTask = RepairArtworkAsync();

    private async Task RepairArtworkAsync()
    {
        try
        {
            var snapshot = await MainThread.InvokeOnMainThreadAsync(() => Songs.ToList());
            foreach (var song in snapshot.Where(s => string.IsNullOrWhiteSpace(s.ThumbnailDataUrl)))
            {
                // Newer downloads encode the video ID immediately before their GUID.
                if (!CoverArtService.IsVideoId(song.VideoId))
                {
                    var match = Regex.Match(Path.GetFileNameWithoutExtension(song.FilePath), @"-([A-Za-z0-9_-]{11})-[a-fA-F0-9]{32}$");
                    if (match.Success)
                        song.VideoId = match.Groups[1].Value;
                }

                CoverArt? artwork = await Task.Run(() =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(song.ThumbnailPath) && File.Exists(song.ThumbnailPath) &&
                            CoverArtService.FromBytes(File.ReadAllBytes(song.ThumbnailPath)) is { } cached)
                            return cached;
                        using var audio = TagLib.File.Create(song.FilePath);
                        var picture = audio.Tag.Pictures.FirstOrDefault();
                        return picture is null ? null : CoverArtService.FromBytes(picture.Data.Data);
                    }
                    catch { return null; }
                });

                artwork ??= await coverArt.GetAsync(song.VideoId, new[] { song.ThumbnailUrl });
                if (artwork is null)
                    continue;
                // A user might delete a song while its artwork request is in flight.
                if (!File.Exists(song.FilePath))
                    continue;

                await CacheArtworkAsync(song, artwork);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (Songs.Contains(song))
                        LibraryUpdated?.Invoke();
                });
            }
            await SaveCacheAsync();
        }
        catch (Exception ex) { Console.WriteLine($"Could not repair cover artwork: {ex.Message}"); }
    }

    public async Task AddSongAsync(Song song)
    {
        await LoadLibraryAsync();
        var existing = Songs.FirstOrDefault(s => s.FilePath == song.FilePath);
        if (existing is not null) Songs.Remove(existing);
        Songs.Insert(0, song);
        LibraryUpdated?.Invoke();
        await SaveCacheAsync();
    }

    public async Task DeleteSongAsync(Song song)
    {
        try
        {
            if (File.Exists(song.FilePath))
                File.Delete(song.FilePath);
            var artworkDirectory = Path.GetFullPath(Path.Combine(FileSystem.AppDataDirectory, "artwork")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(song.ThumbnailPath) &&
                Path.GetFullPath(song.ThumbnailPath).StartsWith(artworkDirectory, StringComparison.OrdinalIgnoreCase))
                File.Delete(song.ThumbnailPath);
        }
        catch (Exception ex) { Console.WriteLine($"Could not delete the song: {ex.Message}"); return; }

        Songs.Remove(song);
        LibraryUpdated?.Invoke();
        await SaveCacheAsync();
        try { await UpdateCollectionsAsync(list => { foreach (var collection in list) collection.SongPaths.Remove(song.FilePath); }); }
        catch (Exception ex) { Console.WriteLine($"Could not update collections after deleting a song: {ex.Message}"); }
    }

    private async Task SaveCacheAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var json = await MainThread.InvokeOnMainThreadAsync(() => JsonSerializer.Serialize(Songs.ToList()));
            var temporary = _cacheFilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, _cacheFilePath, overwrite: true);
        }
        catch (Exception ex) { Console.WriteLine($"Could not save the music library: {ex.Message}"); }
        finally { _saveLock.Release(); }
    }
}
