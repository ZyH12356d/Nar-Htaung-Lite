using System.Collections.ObjectModel;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public partial class LibraryService
{
    private readonly Dictionary<string, IReadOnlyList<Song>> _collectionSongs = new();
    public ObservableCollection<MusicCollection> Collections { get; } = new();
    public string CollectionError { get; private set; } = string.Empty;

    public IEnumerable<Song> GetCollectionSongs(string? collectionId) =>
        collectionId is null ? Songs : _collectionSongs.GetValueOrDefault(collectionId) ?? Array.Empty<Song>();

    private void PublishCollections(IEnumerable<MusicCollection> collections)
    {
        Collections.Clear();
        foreach (var collection in collections) Collections.Add(collection);
        RebuildCollectionIndex();
    }

    private void RebuildCollectionIndex()
    {
        var songs = Songs.Select((song, index) => (song, index)).ToDictionary(x => x.song.FilePath);
        _collectionSongs.Clear();
        foreach (var collection in Collections)
            _collectionSongs[collection.Id] = collection.SongPaths.Where(songs.ContainsKey)
                .Select(path => songs[path]).OrderBy(x => x.index).Select(x => x.song).ToArray();
    }

    public async Task<MusicCollection> CreateCollectionAsync(string name, IEnumerable<string> songPaths)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 60)
            throw new ArgumentException("Use a collection name between 1 and 60 characters.");
        var collection = new MusicCollection { Name = trimmed, SongPaths = songPaths.Distinct().ToList() };
        await ChangeCollectionsAsync(() => _database.CreateCollectionAsync(collection));
        return await MainThread.InvokeOnMainThreadAsync(() => Collections.Single(c => c.Id == collection.Id));
    }

    public Task AddSongsToCollectionAsync(string collectionId, IEnumerable<string> songPaths)
    {
        var paths = songPaths.ToArray();
        return ChangeCollectionsAsync(() => _database.AddMembershipsAsync(new[] { collectionId }, paths, requireCollection: true));
    }

    public Task RemoveSongFromCollectionAsync(string collectionId, string songPath) =>
        ChangeCollectionsAsync(() => _database.RemoveMembershipAsync(collectionId, songPath));

    public Task AddSongToCollectionsAsync(Song song, IEnumerable<string> collectionIds)
    {
        var ids = collectionIds.ToArray();
        return ids.Length == 0 ? Task.CompletedTask :
            ChangeCollectionsAsync(() => _database.AddMembershipsAsync(ids, new[] { song.FilePath }, requireCollection: false));
    }

    private async Task ChangeCollectionsAsync(Func<Task> save)
    {
        await LoadLibraryAsync();
        if (!string.IsNullOrEmpty(CollectionError)) throw new IOException(CollectionError);
        await _mutationLock.WaitAsync();
        try
        {
            await save();
            var saved = await _database.ReadCollectionsAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PublishCollections(saved);
                QueueChanged?.Invoke();
                LibraryUpdated?.Invoke();
            });
        }
        finally { _mutationLock.Release(); }
    }
}
