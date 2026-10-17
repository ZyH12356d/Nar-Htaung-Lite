using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

internal static class CollectionRegression
{
    public static async Task PersistenceAsync()
    {
        using var files = new DownloadCase();
        using var http = Client();
        var library = await LibraryAsync(files, http);
        Check(library.Collections.Count == 0 && library.GetCollectionSongs(null).Count() == 2, "Existing tracks did not enter All downloads.");
        var songs = library.Songs.ToList();
        var created = await library.CreateCollectionAsync("  Night drive  ", new[] { songs[0].FilePath, songs[0].FilePath, "/missing.m4a" });
        Check(created.Name == "Night drive" && created.SongPaths.Count == 1, "Names or selected tracks were not normalized.");
        var reloaded = new LibraryService(Covers(http));
        await reloaded.LoadLibraryAsync();
        await reloaded.RefreshArtworkAsync();
        Check(reloaded.Collections.Single().Id == created.Id && reloaded.GetCollectionSongs(created.Id).Single().Title == songs[0].Title, "The collection or song selection did not survive a reload.");
        Check(reloaded.GetCollectionSongs(null).Count() == 2, "Creating a collection removed songs from All downloads.");
    }

    public static async Task MembershipAsync()
    {
        using var files = new DownloadCase();
        using var http = Client();
        var library = await LibraryAsync(files, http);
        var songs = library.Songs.ToList();
        var first = await library.CreateCollectionAsync("First", Array.Empty<string>());
        var second = await library.CreateCollectionAsync("Second", new[] { songs[0].FilePath });
        await Task.WhenAll(
            library.AddSongsToCollectionAsync(first.Id, new[] { songs[0].FilePath }),
            library.AddSongsToCollectionAsync(first.Id, new[] { songs[1].FilePath }));
        Check(library.GetCollectionSongs(first.Id).Count() == 2, "Concurrent additions lost a song.");
        var added = new Song { Title = "New download", FilePath = Path.Combine(files.Root, "new.m4a"), ThumbnailDataUrl = "data:image/png;base64,fixture" };
        await File.WriteAllTextAsync(added.FilePath, "audio");
        await MainThread.InvokeOnMainThreadAsync(() => library.AddSongAsync(added));
        await library.AddSongToCollectionsAsync(added, new[] { first.Id, second.Id, "deleted-while-downloading" });
        Check(library.GetCollectionSongs(null).Count() == 3 && library.GetCollectionSongs(first.Id).Contains(added) && library.GetCollectionSongs(second.Id).Contains(added), "A download was not added to both default and selected collections.");
        await library.RemoveSongFromCollectionAsync(first.Id, added.FilePath);
        Check(File.Exists(added.FilePath) && library.Songs.Contains(added) && library.GetCollectionSongs(second.Id).Contains(added), "Removing membership deleted the audio or affected another collection.");
        await MainThread.InvokeOnMainThreadAsync(() => library.DeleteSongAsync(songs[0]));
        Check(library.Collections.All(c => !c.SongPaths.Contains(songs[0].FilePath)) && !File.Exists(songs[0].FilePath), "Deleting a downloaded song left dangling memberships.");
        var reloaded = new LibraryService(Covers(http));
        await reloaded.LoadLibraryAsync();
        await reloaded.RefreshArtworkAsync();
        Check(reloaded.GetCollectionSongs(first.Id).Count() == 1 && reloaded.GetCollectionSongs(second.Id).Single().Title == added.Title, "Membership changes were not persisted.");
    }

    public static async Task ValidationAsync()
    {
        using var files = new DownloadCase();
        using var http = Client();
        var library = await LibraryAsync(files, http);
        await library.CreateCollectionAsync("Favorites", Array.Empty<string>());
        foreach (var name in new[] { "", "   ", new string('x', 61), " favorites " })
        {
            try { await library.CreateCollectionAsync(name, Array.Empty<string>()); throw new Exception("An invalid collection name was accepted."); }
            catch (ArgumentException) { }
        }
        Check(library.Collections.Count == 1, "Invalid collection creation changed the library.");
        Directory.CreateDirectory(Path.Combine(files.Root, "collections.json.tmp"));
        try { await library.CreateCollectionAsync("Cannot save", Array.Empty<string>()); throw new Exception("The failed write was ignored."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Check(library.Collections.Count == 1, "A failed write published an unsaved collection.");
    }

    public static async Task CorruptFileAsync()
    {
        using var files = new DownloadCase();
        using var http = Client();
        await LibraryAsync(files, http);
        var path = Path.Combine(files.Root, "collections.json");
        await File.WriteAllTextAsync(path, "{broken");
        var library = new LibraryService(Covers(http));
        await library.LoadLibraryAsync();
        await library.RefreshArtworkAsync();
        Check(library.GetCollectionSongs(null).Count() == 2 && library.CollectionError.Length > 0, "A damaged collection file blocked the downloaded library.");
        try { await library.CreateCollectionAsync("Overwrite", Array.Empty<string>()); throw new Exception("Damaged collections were overwritten."); }
        catch (IOException) { }
        Check(await File.ReadAllTextAsync(path) == "{broken", "The saved collections file was destroyed.");
    }

    private static async Task<LibraryService> LibraryAsync(DownloadCase files, HttpClient http)
    {
        FileSystem.AppDataDirectory = files.Root;
        var songs = Enumerable.Range(1, 2).Select(i => new Song { Title = $"Track {i}", Author = "Artist", FilePath = Path.Combine(files.Root, $"track{i}.m4a"), ThumbnailDataUrl = "data:image/png;base64,fixture" }).ToList();
        foreach (var song in songs) await File.WriteAllTextAsync(song.FilePath, "audio");
        await File.WriteAllTextAsync(Path.Combine(files.Root, "songs_cache.json"), JsonSerializer.Serialize(songs));
        var library = new LibraryService(Covers(http));
        await library.LoadLibraryAsync();
        await library.RefreshArtworkAsync();
        return library;
    }
    private static CoverArtService Covers(HttpClient http) => new(http, NullLogger<CoverArtService>.Instance);
    private static HttpClient Client() => new(new Handler());
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
