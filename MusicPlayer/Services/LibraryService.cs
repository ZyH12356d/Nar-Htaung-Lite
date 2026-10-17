using System.Collections.ObjectModel;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public partial class LibraryService
{
    private readonly CoverArtService _coverArt;
    private readonly string _root;
    private readonly LibraryDatabase _database;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly object _initializationLock = new();
    private Task? _loadTask, _artworkRepairTask;

    public LibraryService(CoverArtService coverArt)
    {
        _coverArt = coverArt;
        _root = FileSystem.AppDataDirectory;
        _database = new LibraryDatabase(_root);
    }

    // Lightweight metadata only. Artwork bytes are served from disk when a visible image requests them.
    public ObservableCollection<Song> Songs { get; } = new();
    public event Action? LibraryUpdated;
    public event Action? QueueChanged;
    public event Action<Song>? ArtworkUpdated;

    public Task LoadLibraryAsync()
    {
        lock (_initializationLock) return _loadTask ??= LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        var catalog = await _database.LoadAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Songs.Clear();
            foreach (var song in catalog.Songs) Songs.Add(song);
            PublishCollections(catalog.Collections);
            CollectionError = catalog.CollectionError;
            LibraryUpdated?.Invoke();
        });
        _ = RefreshArtworkAsync();
    }

    public async Task CacheArtworkAsync(Song song, CoverArt artwork)
    {
        song.ThumbnailPath = await ArtworkFiles.WriteAsync(_root, song, artwork);
        song.ThumbnailDataUrl = "";
    }

    public Task RefreshArtworkAsync()
    {
        lock (_initializationLock)
            return _artworkRepairTask is { IsCompleted: false } ? _artworkRepairTask : _artworkRepairTask = RepairArtworkAsync();
    }

    private async Task RepairArtworkAsync()
    {
        try
        {
            var snapshot = await MainThread.InvokeOnMainThreadAsync(() => Songs.ToList());
            // Checking the local cache stays off the UI thread. Existing covers aren't loaded into memory.
            var missing = await Task.Run(() => snapshot.Where(s =>
                !ArtworkFiles.IsManagedPath(_root, s.ThumbnailPath) || !File.Exists(s.ThumbnailPath)).ToList());
            var pendingUiUpdate = 0;
            foreach (var song in missing)
            {
                CoverArt? artwork = await Task.Run(() =>
                {
                    try
                    {
                        using var audio = TagLib.File.Create(song.FilePath);
                        var picture = audio.Tag.Pictures.FirstOrDefault();
                        return picture is null ? null : CoverArtService.FromBytes(picture.Data.Data);
                    }
                    catch { return null; }
                });
                artwork ??= await _coverArt.GetAsync(song.VideoId, new[] { song.ThumbnailUrl });
                if (artwork is null) continue;

                await _mutationLock.WaitAsync();
                try
                {
                    if (!await MainThread.InvokeOnMainThreadAsync(() => Songs.Contains(song)) ||
                        !await Task.Run(() => File.Exists(song.FilePath))) continue;
                    var repaired = new Song { Id = song.Id, FilePath = song.FilePath, VideoId = song.VideoId };
                    await CacheArtworkAsync(repaired, artwork);
                    if (!await _database.UpdateArtworkAsync(repaired)) continue;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        song.ThumbnailPath = repaired.ThumbnailPath;
                        song.ThumbnailRevision = repaired.ThumbnailRevision;
                        ArtworkUpdated?.Invoke(song);
                    });
                    if (++pendingUiUpdate >= 16)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() => LibraryUpdated?.Invoke());
                        pendingUiUpdate = 0;
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Could not repair cover for {song.Title}: {ex.Message}"); }
                finally { _mutationLock.Release(); }
            }
            if (pendingUiUpdate > 0) await MainThread.InvokeOnMainThreadAsync(() => LibraryUpdated?.Invoke());
        }
        catch (Exception ex) { Console.WriteLine($"Could not repair cover artwork: {ex.Message}"); }
    }

    public async Task AddSongAsync(Song song)
    {
        await LoadLibraryAsync();
        await _mutationLock.WaitAsync();
        try
        {
            await ArtworkFiles.PrepareLegacyAsync(_root, song);
            song.ThumbnailDataUrl = "";
            if (song.AddedAtUtcTicks <= 0) song.AddedAtUtcTicks = DateTime.UtcNow.Ticks;
            await _database.SaveSongAsync(song);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var existing = Songs.FirstOrDefault(s => s.FilePath == song.FilePath);
                if (existing is not null) Songs.Remove(existing);
                Songs.Insert(0, song);
                RebuildCollectionIndex();
                QueueChanged?.Invoke();
                LibraryUpdated?.Invoke();
            });
        }
        finally { _mutationLock.Release(); }
    }

    public async Task DeleteSongAsync(Song song)
    {
        await LoadLibraryAsync();
        await _mutationLock.WaitAsync();
        var staged = song.FilePath + ".delete-" + Guid.NewGuid().ToString("N");
        var moved = false;
        try
        {
            // Stage the file so a failed database transaction can restore it.
            moved = await Task.Run(() =>
            {
                if (!File.Exists(song.FilePath)) return false;
                File.Move(song.FilePath, staged);
                return true;
            });
            try { await _database.DeleteSongAsync(song.Id); }
            catch
            {
                if (moved) await Task.Run(() => File.Move(staged, song.FilePath));
                throw;
            }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Songs.Remove(song);
                foreach (var collection in Collections) collection.SongPaths.Remove(song.FilePath);
                RebuildCollectionIndex();
                QueueChanged?.Invoke();
                LibraryUpdated?.Invoke();
            });
            await Task.Run(() =>
            {
                try
                {
                    if (moved) File.Delete(staged);
                    if (ArtworkFiles.IsManagedPath(_root, song.ThumbnailPath)) File.Delete(song.ThumbnailPath);
                }
                catch (IOException ex) { Console.WriteLine($"Could not remove a deleted track's files: {ex.Message}"); }
            });
        }
        finally { _mutationLock.Release(); }
    }
}
