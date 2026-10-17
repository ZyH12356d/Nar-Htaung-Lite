using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

internal static class DatabaseRegression
{
    public static async Task MigrationAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        var explicitId = new Song { Title = "Original", Author = "Artist", FilePath = Path.Combine(files.Root, "original.m4a"), VideoId = "aqz-KE-bpKQ", ThumbnailDataUrl = ArtworkRegression.DataUrl };
        var inferred = new Song { Title = "Older track", FilePath = Path.Combine(files.Root, $"older-jNQXAC9IVRw-{Guid.NewGuid():N}.m4a"), ThumbnailDataUrl = ArtworkRegression.DataUrl };
        var missing = new Song { Title = "Missing", FilePath = Path.Combine(files.Root, "missing.m4a") };
        await File.WriteAllTextAsync(explicitId.FilePath, "audio");
        await File.WriteAllTextAsync(inferred.FilePath, "audio");
        var legacy = JsonSerializer.Serialize(new[] { explicitId, inferred, missing });
        var collection = new MusicCollection { Name = "Night drive", SongPaths = new() { explicitId.FilePath, inferred.FilePath, missing.FilePath } };
        var legacyCollections = JsonSerializer.Serialize(new[] { collection });
        await File.WriteAllTextAsync(Path.Combine(files.Root, "songs_cache.json"), legacy);
        await File.WriteAllTextAsync(Path.Combine(files.Root, "collections.json"), legacyCollections);
        using var http = Offline();
        var library = await LoadAsync(http);
        Check(library.Songs.Count == 2 && library.Songs[0].VideoId == explicitId.VideoId && library.Songs[1].VideoId == "jNQXAC9IVRw", "Source IDs or legacy order were lost.");
        Check(library.Songs.All(s => s.ThumbnailDataUrl.Length == 0 && File.Exists(s.ThumbnailPath)), "Image payloads remain in catalog memory.");
        Check(library.GetCollectionSongs(collection.Id).Count() == 2, "Collection membership was not migrated.");
        Check(await File.ReadAllTextAsync(Path.Combine(files.Root, "songs_cache.json")) == legacy &&
            await File.ReadAllTextAsync(Path.Combine(files.Root, "collections.json")) == legacyCollections, "Legacy recovery copies were modified.");

        var original = library.Songs[0];
        await library.DeleteSongAsync(library.Songs[1]);
        var reloaded = await LoadAsync(http);
        Check(reloaded.Songs.Single().Id == original.Id && reloaded.Songs.Single().VideoId == explicitId.VideoId, "Song identity did not survive restart.");
        Check(reloaded.Collections.Single().Id == collection.Id && reloaded.GetCollectionSongs(collection.Id).Count() == 1, "Old JSON was reimported or memberships were lost.");
        using var db = Open(files.Root);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        Check(Version.Parse((string)command.ExecuteScalar()!) >= new Version(3, 50, 2), "The bundled SQLite engine is outdated.");
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Songs') WHERE name='ThumbnailDataUrl'";
        Check((long)command.ExecuteScalar()! == 0, "Base64 artwork is stored in SQLite.");
    }

    public static async Task ExistingCoverMigrationAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        var song = new Song { FilePath = Path.Combine(files.Root, "old.m4a"), Title = "Older cached cover", VideoId = "jNQXAC9IVRw" };
        await File.WriteAllTextAsync(song.FilePath, "audio");
        song.ThumbnailPath = await ArtworkFiles.WriteAsync(files.Root, song, new CoverArt(Convert.FromBase64String(ArtworkRegression.DataUrl.Split(',')[1]), "image/png"));
        song.ThumbnailRevision = 0;
        await File.WriteAllTextAsync(Path.Combine(files.Root, "songs_cache.json"), JsonSerializer.Serialize(new[] { song }));
        using var http = new HttpClient(new Handler(_ => throw new Exception("Existing artwork should migrate without network access.")));
        var library = await LoadAsync(http);
        Check(library.Songs.Single().ThumbnailRevision > 0 && File.Exists(library.Songs.Single().ThumbnailPath),
            "An existing legacy cover was not normalized and versioned.");
    }

    public static async Task RollbackMigrationAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        var songs = Enumerable.Range(0, 2).Select(i => new Song { Id = "duplicate", Title = $"Track {i}", FilePath = Path.Combine(files.Root, $"{i}.m4a"), ThumbnailDataUrl = ArtworkRegression.DataUrl }).ToList();
        foreach (var song in songs) await File.WriteAllTextAsync(song.FilePath, "audio");
        var legacy = JsonSerializer.Serialize(songs);
        var cache = Path.Combine(files.Root, "songs_cache.json");
        await File.WriteAllTextAsync(cache, legacy);
        using var http = Offline();
        try { await LoadAsync(http); throw new Exception("Invalid migration unexpectedly committed."); }
        catch (SqliteException) { }
        using (var db = Open(files.Root))
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM Songs";
            Check((long)command.ExecuteScalar()! == 0, "A failed migration committed partial songs.");
            command.CommandText = "SELECT COUNT(*) FROM Metadata WHERE Key='songs_migrated'";
            Check((long)command.ExecuteScalar()! == 0, "A failed migration was marked complete.");
        }
        Check(await File.ReadAllTextAsync(cache) == legacy, "A failed migration damaged recovery data.");
        songs[1].Id = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(songs));
        Check((await LoadAsync(http)).Songs.Count == 2, "Migration could not recover after correcting legacy data.");
    }

    public static async Task WriteRollbackAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var http = Offline();
        var library = await LoadAsync(http);
        var song = new Song { Title = "Saved", FilePath = Path.Combine(files.Root, "saved.m4a"), ThumbnailDataUrl = ArtworkRegression.DataUrl, VideoId = "jNQXAC9IVRw" };
        await File.WriteAllTextAsync(song.FilePath, "audio");
        await library.AddSongAsync(song);
        var collection = await library.CreateCollectionAsync("Keep", new[] { song.FilePath });
        using (var db = Open(files.Root))
        using (var command = db.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER BlockDelete BEFORE DELETE ON Songs BEGIN SELECT RAISE(ABORT,'Simulated write failure'); END";
            command.ExecuteNonQuery();
        }
        try { await library.DeleteSongAsync(song); throw new Exception("The failed delete was ignored."); }
        catch (SqliteException) { }
        Check(File.Exists(song.FilePath) && library.Songs.Contains(song) && library.GetCollectionSongs(collection.Id).Contains(song), "A failed delete lost audio or published unsaved state.");

        // Simulate the app exiting between staging an audio deletion and committing its database row.
        var staged = song.FilePath + ".delete-" + Guid.NewGuid().ToString("N");
        File.Move(song.FilePath, staged);
        var restored = await LoadAsync(http);
        Check(File.Exists(song.FilePath) && restored.Songs.Count == 1 && !File.Exists(staged), "An interrupted deletion was not recovered.");
    }

    public static async Task UiThreadAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var http = Offline();
        var library = await LoadAsync(http);
        using var blocker = Open(files.Root);
        using var transaction = blocker.BeginTransaction();
        var save = MainThread.InvokeOnMainThreadAsync(async () => { await library.CreateCollectionAsync("After unlock", Array.Empty<string>()); });
        try
        {
            await Task.Delay(100);
            await MainThread.InvokeOnMainThreadAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(2));
            Check(!save.IsCompleted && library.Collections.Count == 0, "A pending database write blocked the UI or published uncommitted data.");
        }
        finally
        {
            transaction.Rollback();
            await save.WaitAsync(TimeSpan.FromSeconds(12));
        }
        Check(library.Collections.Count == 1, "The database write did not complete after the lock was released.");
    }

    public static async Task ArtworkDoesNotReplaceQueueAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        var path = Path.Combine(files.Root, "track.m4a");
        await File.WriteAllTextAsync(path, "audio");
        await File.WriteAllTextAsync(Path.Combine(files.Root, "songs_cache.json"), JsonSerializer.Serialize(new[] { new Song { Title = "Track", FilePath = path, VideoId = "jNQXAC9IVRw" } }));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async _ =>
        {
            await release.Task;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(ArtworkRegression.DataUrl.Split(',')[1])) };
        }));
        var library = new LibraryService(new CoverArtService(http, NullLogger<CoverArtService>.Instance));
        await library.LoadLibraryAsync();
        var queueChanges = 0;
        var coverChanges = 0;
        library.QueueChanged += () => queueChanges++;
        library.ArtworkUpdated += _ => coverChanges++;
        release.SetResult();
        await library.RefreshArtworkAsync();
        Check(queueChanges == 0 && coverChanges == 1 && File.Exists(library.Songs.Single().ThumbnailPath), "Artwork repair rebuilt the queue or failed to save its cover.");
        var song = library.Songs.Single();
        var previousSource = song.ArtworkSource;
        File.Delete(song.ThumbnailPath);
        await library.RefreshArtworkAsync();
        Check(queueChanges == 0 && coverChanges == 2 && song.ArtworkSource != previousSource,
            "A repaired cover kept its stale URL or rebuilt the queue.");
    }

    public static async Task CoverServingAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var http = Offline();
        var library = await LoadAsync(http);
        var song = new Song { FilePath = Path.Combine(files.Root, "audio.m4a") };
        await library.CacheArtworkAsync(song, new CoverArt(Convert.FromBase64String(ArtworkRegression.DataUrl.Split(',')[1]), "image/png"));
        var provider = new ArtworkFileProvider(files.Root);
        Check(provider.GetFileInfo(song.ArtworkSource.Split('?')[0]).Exists, "A cached cover cannot be served offline.");
        using (var stream = provider.GetFileInfo(song.ArtworkSource.Split('?')[0]).CreateReadStream())
            Check(stream.Length > 0, "The cover stream is empty.");
        foreach (var name in new[] { "library.db", "../library.db", "artwork/../library.db", "artwork/%2e%2e/library.db", "artwork/audio.m4a" })
            Check(!provider.GetFileInfo(name).Exists, "The cover provider exposed private app files.");
    }

    public static async Task LargeLibrariesAsync()
    {
        foreach (var count in new[] { 100, 500, 1000 })
        {
            using var files = new DownloadCase();
            FileSystem.AppDataDirectory = files.Root;
            var cover = Convert.FromBase64String(ArtworkRegression.DataUrl.Split(',')[1]);
            var padded = new byte[8192];
            cover.CopyTo(padded, 0);
            var dataUrl = "data:image/png;base64," + Convert.ToBase64String(padded);
            var songs = Enumerable.Range(0, count).Select(i => new Song
            {
                Title = $"Track {i:0000}", Author = $"Artist {i % 25}", VideoId = $"TEST{i:0000000}",
                FilePath = Path.Combine(files.Root, $"track{i}.m4a"), ThumbnailDataUrl = dataUrl
            }).ToList();
            foreach (var song in songs) await File.WriteAllTextAsync(song.FilePath, "audio");
            var cache = Path.Combine(files.Root, "songs_cache.json");
            await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(songs));
            using var http = Offline();
            var watch = Stopwatch.StartNew();
            var library = await LoadAsync(http);
            var migrationMs = watch.ElapsedMilliseconds;
            var selected = songs.Where((_, i) => i % 10 == 0).Select(s => s.FilePath).ToList();
            var collection = await library.CreateCollectionAsync("Every tenth", selected);
            watch.Restart();
            var restored = await LoadAsync(http);
            var loadMs = watch.ElapsedMilliseconds;
            Check(restored.Songs.Count == count && restored.GetCollectionSongs(collection.Id).Count() == selected.Count, "A large catalog lost tracks or collection membership.");
            Check(restored.Songs.All(s => s.ThumbnailDataUrl.Length == 0 && s.VideoId.Length == 11), "Large catalog records retain image payloads or lost source IDs.");
            Check(new FileInfo(Path.Combine(files.Root, "library.db")).Length < 3 * 1024 * 1024, "The metadata database grew with image payloads.");
            Console.WriteLine($"  Desktop catalog fixture: {count} songs; migration {migrationMs} ms; reload {loadMs} ms; DB {new FileInfo(Path.Combine(files.Root, "library.db")).Length / 1024} KiB.");
        }
    }

    private static async Task<LibraryService> LoadAsync(HttpClient http)
    {
        var library = new LibraryService(new CoverArtService(http, NullLogger<CoverArtService>.Instance));
        await library.LoadLibraryAsync();
        await library.RefreshArtworkAsync();
        return library;
    }
    private static SqliteConnection Open(string root)
    {
        var db = new SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Foreign Keys=True;Pooling=False");
        db.Open();
        return db;
    }
    private static HttpClient Offline() => new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
