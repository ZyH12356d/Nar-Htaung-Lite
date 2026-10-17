using System.Text.Json;
using Microsoft.Data.Sqlite;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

// All public operations run on a worker: Microsoft.Data.Sqlite's async methods still do synchronous I/O.
public sealed class LibraryDatabase(string root)
{
    // Root the native provider for trimming and initialize it on the database worker.
    private static readonly Lazy<bool> NativeProvider = new(() => { SQLitePCL.Batteries_V2.Init(); return true; });

    public string DatabasePath { get; } = Path.Combine(root, "library.db");
    private const string SongColumns = "Id,Title,Author,FilePath,VideoId,AddedAtUtcTicks,ThumbnailUrl,ThumbnailPath,ThumbnailRevision";

    public Task<CatalogSnapshot> LoadAsync() => Task.Run(async () =>
    {
        using var db = Open();
        var version = Convert.ToInt32(Scalar(db, null, "PRAGMA user_version"));
        if (version > 1) throw new IOException("This library was created by a newer app version.");
        using (var schema = db.BeginTransaction())
        {
            Execute(db, schema, """
                CREATE TABLE IF NOT EXISTS Metadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS Songs (
                    Id TEXT PRIMARY KEY, Title TEXT NOT NULL, Author TEXT NOT NULL,
                    FilePath TEXT NOT NULL UNIQUE, VideoId TEXT NOT NULL,
                    AddedAtUtcTicks INTEGER NOT NULL, ThumbnailUrl TEXT NOT NULL, ThumbnailPath TEXT NOT NULL, ThumbnailRevision INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_Songs_Added ON Songs(AddedAtUtcTicks DESC, Id DESC);
                CREATE INDEX IF NOT EXISTS IX_Songs_VideoId ON Songs(VideoId);
                CREATE TABLE IF NOT EXISTS Collections (
                    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, NameKey TEXT NOT NULL UNIQUE, CreatedAtUtcTicks INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS CollectionSongs (
                    CollectionId TEXT NOT NULL REFERENCES Collections(Id) ON DELETE CASCADE,
                    SongId TEXT NOT NULL REFERENCES Songs(Id) ON DELETE CASCADE,
                    PRIMARY KEY(CollectionId, SongId));
                CREATE INDEX IF NOT EXISTS IX_CollectionSongs_SongId ON CollectionSongs(SongId);
                PRAGMA user_version=1;
                """);
            schema.Commit();
        }

        if (!HasMarker(db, "songs_migrated"))
            await ImportSongsAsync(db);

        var collectionError = "";
        if (!HasMarker(db, "collections_migrated"))
        {
            try { ImportCollections(db); }
            catch (JsonException ex)
            {
                collectionError = "Saved collections could not be opened. Your downloaded songs are still available.";
                Console.WriteLine($"Could not migrate collections: {ex.Message}");
            }
        }

        var songs = new List<Song>();
        // Read lightweight records in batches. Image bytes never enter the catalog snapshot.
        for (var offset = 0; ; offset += 256)
        {
            using var command = Command(db, null, $"SELECT {SongColumns} FROM Songs ORDER BY AddedAtUtcTicks DESC,Id DESC LIMIT 256 OFFSET $offset", ("$offset", offset));
            using var reader = command.ExecuteReader();
            var count = 0;
            while (reader.Read()) { songs.Add(ReadSong(reader)); count++; }
            if (count < 256) break;
        }
        using (var cleanup = db.BeginTransaction())
        {
            foreach (var song in songs.Where(s => !File.Exists(s.FilePath)))
            {
                // Recover a deletion interrupted before its database commit.
                var directory = Path.GetDirectoryName(song.FilePath);
                if (directory is null || !Directory.Exists(directory)) continue;
                var prefix = song.FilePath + ".delete-";
                var staged = Directory.EnumerateFiles(directory, Path.GetFileName(song.FilePath) + ".delete-*")
                    .FirstOrDefault(path => path.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(path[prefix.Length..], "N", out _));
                if (staged is not null) File.Move(staged, song.FilePath);
            }
            foreach (var missing in songs.Where(s => !File.Exists(s.FilePath)).ToList())
            {
                Execute(db, cleanup, "DELETE FROM Songs WHERE Id=$id", ("$id", missing.Id));
                songs.Remove(missing);
            }
            cleanup.Commit();
        }
        return new CatalogSnapshot(songs, ReadCollections(db), collectionError);
    });

    public Task SaveSongAsync(Song song) => Run(db =>
    {
        using var transaction = db.BeginTransaction();
        SaveSong(db, transaction, song);
        transaction.Commit();
    });

    public Task<bool> UpdateArtworkAsync(Song song) => Task.Run(() =>
    {
        using var db = Open();
        return Execute(db, null, "UPDATE Songs SET ThumbnailPath=$cover,ThumbnailRevision=$revision,VideoId=$video WHERE Id=$id",
            ("$cover", song.ThumbnailPath), ("$revision", song.ThumbnailRevision), ("$video", song.VideoId), ("$id", song.Id)) > 0;
    });

    public Task DeleteSongAsync(string id) => Run(db => Execute(db, null, "DELETE FROM Songs WHERE Id=$id", ("$id", id)));

    public Task<List<MusicCollection>> ReadCollectionsAsync() => Task.Run(() => { using var db = Open(); return ReadCollections(db); });

    public Task CreateCollectionAsync(MusicCollection collection) => Run(db =>
    {
        using var transaction = db.BeginTransaction();
        try
        {
            Execute(db, transaction, "INSERT INTO Collections(Id,Name,NameKey,CreatedAtUtcTicks) VALUES($id,$name,$key,$date)",
                ("$id", collection.Id), ("$name", collection.Name), ("$key", collection.Name.ToUpperInvariant()), ("$date", DateTime.UtcNow.Ticks));
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067)
        { throw new ArgumentException("You already have a collection with that name.", ex); }
        InsertMemberships(db, transaction, collection.Id, collection.SongPaths);
        transaction.Commit();
    });

    public Task AddMembershipsAsync(IEnumerable<string> collectionIds, IEnumerable<string> paths, bool requireCollection) => Run(db =>
    {
        using var transaction = db.BeginTransaction();
        foreach (var id in collectionIds.Distinct())
        {
            if (Scalar(db, transaction, "SELECT Id FROM Collections WHERE Id=$id", ("$id", id)) is null)
            {
                if (requireCollection) throw new ArgumentException("This collection is no longer available.");
                continue;
            }
            InsertMemberships(db, transaction, id, paths);
        }
        transaction.Commit();
    });

    public Task RemoveMembershipAsync(string collectionId, string path) => Run(db =>
        Execute(db, null, "DELETE FROM CollectionSongs WHERE CollectionId=$collection AND SongId IN (SELECT Id FROM Songs WHERE FilePath=$path)",
            ("$collection", collectionId), ("$path", path)));

    private Task Run(Action<SqliteConnection> operation) => Task.Run(() =>
    {
        using var db = Open();
        operation(db);
    });

    private SqliteConnection Open()
    {
        _ = NativeProvider.Value;
        Directory.CreateDirectory(root);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 10
        }.ToString());
        try
        {
            db.Open();
            Execute(db, null, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    private async Task ImportSongsAsync(SqliteConnection db)
    {
        var cache = Path.Combine(root, "songs_cache.json");
        if (File.Exists(cache))
        {
            try
            {
                using var transaction = db.BeginTransaction();
                await using var stream = File.OpenRead(cache);
                var ordinal = 0L;
                var now = DateTime.UtcNow.Ticks;
                var paths = new HashSet<string>(StringComparer.Ordinal);
                await foreach (var song in JsonSerializer.DeserializeAsyncEnumerable(stream, LibraryJsonContext.Default.Song))
                {
                    if (song is null || string.IsNullOrEmpty(song.FilePath) || !File.Exists(song.FilePath) || !paths.Add(song.FilePath)) continue;
                    song.AddedAtUtcTicks = song.AddedAtUtcTicks > 0 ? song.AddedAtUtcTicks : now - ordinal++;
                    await PrepareSongAsync(song);
                    SaveSong(db, transaction, song);
                }
                SetMarker(db, transaction, "songs_migrated");
                transaction.Commit();
                return;
            }
            catch (JsonException ex)
            {
                // A malformed old cache is rebuilt from audio tags; the original stays untouched.
                Console.WriteLine($"Rebuilding a damaged legacy catalog from audio: {ex.Message}");
            }
        }

        using var fallback = db.BeginTransaction();
        foreach (var path in Directory.EnumerateFiles(root)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp3" or ".m4a" or ".webm")
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var song = new Song { FilePath = path, Title = Path.GetFileNameWithoutExtension(path), Author = "Unknown artist", AddedAtUtcTicks = File.GetLastWriteTimeUtc(path).Ticks };
            try
            {
                using var audio = TagLib.File.Create(path);
                song.Title = string.IsNullOrWhiteSpace(audio.Tag.Title) ? song.Title : audio.Tag.Title;
                song.Author = audio.Tag.FirstPerformer ?? audio.Tag.FirstAlbumArtist ?? song.Author;
                if (audio.Tag.Pictures.FirstOrDefault() is { } picture && CoverArtService.FromBytes(picture.Data.Data) is { } cover)
                    song.ThumbnailPath = await ArtworkFiles.WriteAsync(root, song, cover);
            }
            catch (Exception ex) { Console.WriteLine($"Could not read legacy audio tags: {ex.Message}"); }
            await PrepareSongAsync(song);
            SaveSong(db, fallback, song);
        }
        SetMarker(db, fallback, "songs_migrated");
        fallback.Commit();
    }

    private async Task PrepareSongAsync(Song song)
    {
        song.Id = string.IsNullOrWhiteSpace(song.Id) ? Guid.NewGuid().ToString("N") : song.Id;
        song.Title ??= ""; song.Author ??= ""; song.VideoId ??= ""; song.ThumbnailUrl ??= "";
        song.ThumbnailPath ??= ""; song.ThumbnailDataUrl ??= "";
        await ArtworkFiles.PrepareLegacyAsync(root, song);
        song.ThumbnailDataUrl = "";
    }

    private void ImportCollections(SqliteConnection db)
    {
        var file = Path.Combine(root, "collections.json");
        var collections = File.Exists(file)
            ? JsonSerializer.Deserialize(File.ReadAllText(file), LibraryJsonContext.Default.Collections) ?? throw new JsonException("The collections file is empty.")
            : new List<MusicCollection>();
        using var transaction = db.BeginTransaction();
        var seen = new HashSet<string>();
        foreach (var collection in collections.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)))
        {
            if (!seen.Add(collection.Id)) continue;
            Execute(db, transaction, "INSERT OR IGNORE INTO Collections(Id,Name,NameKey,CreatedAtUtcTicks) VALUES($id,$name,$key,$date)",
                ("$id", collection.Id), ("$name", collection.Name), ("$key", collection.Name.ToUpperInvariant()), ("$date", DateTime.UtcNow.Ticks));
            // A duplicate legacy name may have been ignored by the unique constraint.
            if (Scalar(db, transaction, "SELECT Id FROM Collections WHERE Id=$id", ("$id", collection.Id)) is not null)
                InsertMemberships(db, transaction, collection.Id, collection.SongPaths ?? new());
        }
        SetMarker(db, transaction, "collections_migrated");
        transaction.Commit();
    }

    private static void SaveSong(SqliteConnection db, SqliteTransaction transaction, Song song)
    {
        // File identity is unique; updating artwork/metadata never replaces a song or its memberships.
        var existing = Scalar(db, transaction, "SELECT Id FROM Songs WHERE FilePath=$path", ("$path", song.FilePath)) as string;
        if (existing is not null) song.Id = existing;
        Execute(db, transaction, """
            INSERT INTO Songs(Id,Title,Author,FilePath,VideoId,AddedAtUtcTicks,ThumbnailUrl,ThumbnailPath,ThumbnailRevision)
            VALUES($id,$title,$author,$path,$video,$date,$url,$cover,$revision)
            ON CONFLICT(FilePath) DO UPDATE SET Title=excluded.Title,Author=excluded.Author,VideoId=excluded.VideoId,
                ThumbnailUrl=excluded.ThumbnailUrl,ThumbnailPath=excluded.ThumbnailPath,ThumbnailRevision=excluded.ThumbnailRevision
            """,
            ("$id", song.Id), ("$title", song.Title), ("$author", song.Author), ("$path", song.FilePath),
            ("$video", song.VideoId), ("$date", song.AddedAtUtcTicks), ("$url", song.ThumbnailUrl), ("$cover", song.ThumbnailPath), ("$revision", song.ThumbnailRevision));
    }

    private static void InsertMemberships(SqliteConnection db, SqliteTransaction transaction, string collectionId, IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
            Execute(db, transaction, "INSERT OR IGNORE INTO CollectionSongs(CollectionId,SongId) SELECT $collection,Id FROM Songs WHERE FilePath=$path",
                ("$collection", collectionId), ("$path", path));
    }

    private static List<MusicCollection> ReadCollections(SqliteConnection db)
    {
        var collections = new List<MusicCollection>();
        using (var command = Command(db, null, "SELECT Id,Name FROM Collections ORDER BY CreatedAtUtcTicks,Id"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) collections.Add(new MusicCollection { Id = reader.GetString(0), Name = reader.GetString(1) });
        var byId = collections.ToDictionary(c => c.Id);
        using (var command = Command(db, null, "SELECT cs.CollectionId,s.FilePath FROM CollectionSongs cs JOIN Songs s ON s.Id=cs.SongId"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) if (byId.TryGetValue(reader.GetString(0), out var collection)) collection.SongPaths.Add(reader.GetString(1));
        return collections;
    }

    private static Song ReadSong(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0), Title = reader.GetString(1), Author = reader.GetString(2), FilePath = reader.GetString(3),
        VideoId = reader.GetString(4), AddedAtUtcTicks = reader.GetInt64(5), ThumbnailUrl = reader.GetString(6), ThumbnailPath = reader.GetString(7), ThumbnailRevision = reader.GetInt64(8)
    };

    private static bool HasMarker(SqliteConnection db, string key) => Scalar(db, null, "SELECT Value FROM Metadata WHERE Key=$key", ("$key", key)) is not null;
    private static void SetMarker(SqliteConnection db, SqliteTransaction transaction, string key) =>
        Execute(db, transaction, "INSERT INTO Metadata(Key,Value) VALUES($key,'1')", ("$key", key));

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return command;
    }

    private static int Execute(SqliteConnection db, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    { using var command = Command(db, transaction, sql, parameters); return command.ExecuteNonQuery(); }

    private static object? Scalar(SqliteConnection db, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    { using var command = Command(db, transaction, sql, parameters); return command.ExecuteScalar(); }
}

public sealed record CatalogSnapshot(List<Song> Songs, List<MusicCollection> Collections, string CollectionError);
