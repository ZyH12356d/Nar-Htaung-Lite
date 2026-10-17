using System.Collections.ObjectModel;
using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public partial class LibraryService
{
    private readonly string _collectionsFilePath = Path.Combine(FileSystem.AppDataDirectory, "collections.json");
    private readonly SemaphoreSlim _collectionLock = new(1, 1);
    public ObservableCollection<MusicCollection> Collections { get; } = new();
    public string CollectionError { get; private set; } = string.Empty;

    public IEnumerable<Song> GetCollectionSongs(string? collectionId)
    {
        if (collectionId is null) return Songs;
        var paths = Collections.FirstOrDefault(c => c.Id == collectionId)?.SongPaths;
        if (paths is null) return Enumerable.Empty<Song>();
        var membership = paths.ToHashSet();
        return Songs.Where(s => membership.Contains(s.FilePath));
    }

    private async Task LoadCollectionsAsync()
    {
        try
        {
            if (!File.Exists(_collectionsFilePath)) return;
            var saved = JsonSerializer.Deserialize<List<MusicCollection>>(await File.ReadAllTextAsync(_collectionsFilePath))
                ?? throw new JsonException("The collections file is empty.");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var available = Songs.Select(s => s.FilePath).ToHashSet();
                Collections.Clear();
                foreach (var collection in saved.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)).DistinctBy(c => c.Id))
                {
                    collection.SongPaths = (collection.SongPaths ?? new()).Where(available.Contains).Distinct().ToList();
                    Collections.Add(collection);
                }
                LibraryUpdated?.Invoke();
            });
        }
        catch (Exception ex)
        {
            CollectionError = "Saved collections could not be opened. Your downloaded songs are still available.";
            Console.WriteLine($"Could not load collections: {ex.Message}");
        }
    }

    public async Task<MusicCollection> CreateCollectionAsync(string name, IEnumerable<string> songPaths)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 60)
            throw new ArgumentException("Use a collection name between 1 and 60 characters.");
        var collection = new MusicCollection { Name = trimmed, SongPaths = songPaths.Distinct().ToList() };
        await UpdateCollectionsAsync(list =>
        {
            if (list.Any(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("You already have a collection with that name.");
            list.Add(collection);
        });
        return Collections.Single(c => c.Id == collection.Id);
    }

    public Task AddSongsToCollectionAsync(string collectionId, IEnumerable<string> songPaths)
    {
        var paths = songPaths.ToList();
        return UpdateCollectionsAsync(list =>
        {
            var collection = list.FirstOrDefault(c => c.Id == collectionId)
                ?? throw new ArgumentException("This collection is no longer available.");
            collection.SongPaths.AddRange(paths);
        });
    }

    public Task RemoveSongFromCollectionAsync(string collectionId, string songPath) =>
        UpdateCollectionsAsync(list => list.FirstOrDefault(c => c.Id == collectionId)?.SongPaths.RemoveAll(p => p == songPath));

    public Task AddSongToCollectionsAsync(Song song, IEnumerable<string> collectionIds)
    {
        var ids = collectionIds.ToHashSet();
        if (ids.Count == 0) return Task.CompletedTask;
        return UpdateCollectionsAsync(list =>
        {
            foreach (var collection in list.Where(c => ids.Contains(c.Id)))
                collection.SongPaths.Add(song.FilePath);
        });
    }

    private async Task UpdateCollectionsAsync(Action<List<MusicCollection>> update)
    {
        await LoadLibraryAsync();
        if (!string.IsNullOrEmpty(CollectionError))
            throw new IOException(CollectionError);
        await _collectionLock.WaitAsync();
        try
        {
            var snapshot = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var copy = Collections.Select(c => new MusicCollection
                {
                    Id = c.Id, Name = c.Name, SongPaths = c.SongPaths.ToList()
                }).ToList();
                update(copy);
                var available = Songs.Select(s => s.FilePath).ToHashSet();
                foreach (var collection in copy)
                    collection.SongPaths = collection.SongPaths.Where(available.Contains).Distinct().ToList();
                return copy;
            });
            var temporary = _collectionsFilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, _collectionsFilePath, overwrite: true);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Collections.Clear();
                foreach (var collection in snapshot) Collections.Add(collection);
                LibraryUpdated?.Invoke();
            });
        }
        finally { _collectionLock.Release(); }
    }
}
