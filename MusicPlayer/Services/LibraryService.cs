using System.Collections.ObjectModel;
using System.Text.Json;
using MusicPlayer.Models;

namespace MusicPlayer.Services
{
    public class LibraryService
    {
        private const string CacheFileName = "songs_cache.json";
        private readonly string _cacheFilePath;

        public ObservableCollection<Song> Songs { get; private set; } = new();

        public event Action? LibraryUpdated;

        public LibraryService()
        {
            _cacheFilePath = Path.Combine(FileSystem.AppDataDirectory, CacheFileName);
        }

        public async Task LoadLibraryAsync()
        {
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath);
                    var cachedSongs = JsonSerializer.Deserialize<List<Song>>(json);
                    
                    if (cachedSongs != null)
                    {
                        // Verify files still exist
                        Songs.Clear();
                        foreach (var song in cachedSongs.Where(s => File.Exists(s.FilePath)))
                        {
                            Songs.Add(song);
                        }
                        
                        LibraryUpdated?.Invoke();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading library cache: {ex.Message}");
                }
            }

            // Fallback: Scan directory (only happens if cache is missing or corrupt)
            await ScanDirectoryAndSaveAsync();
        }

        private async Task ScanDirectoryAndSaveAsync()
        {
            var path = FileSystem.AppDataDirectory;
            var files = Directory.EnumerateFiles(path)
                                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".m4a") || f.EndsWith(".webm"))
                                .Select(f => new FileInfo(f))
                                .OrderByDescending(fi => fi.LastWriteTime)
                                .Select(fi => fi.FullName)
                                .ToList();

            var newSongs = new List<Song>();

            foreach (var f in files)
            {
                try
                {
                    using (var tfile = TagLib.File.Create(f))
                    {
                        var song = new Song
                        {
                            Title = !string.IsNullOrEmpty(tfile.Tag.Title) ? tfile.Tag.Title : Path.GetFileNameWithoutExtension(f),
                            Author = tfile.Tag.FirstPerformer ?? tfile.Tag.FirstAlbumArtist ?? "Unknown Artist",
                            FilePath = f
                        };

                        if (tfile.Tag.Pictures != null && tfile.Tag.Pictures.Length > 0)
                        {
                            var bin = tfile.Tag.Pictures[0].Data.Data;
                            var mimeType = tfile.Tag.Pictures[0].MimeType ?? "image/jpeg";
                            song.ThumbnailDataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(bin)}";
                        }

                        newSongs.Add(song);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading tags for {f}: {ex.Message}");
                    newSongs.Add(new Song
                    {
                        Title = Path.GetFileNameWithoutExtension(f),
                        Author = "Unknown Artist",
                        FilePath = f
                    });
                }
            }

            Songs.Clear();
            foreach (var song in newSongs)
            {
                Songs.Add(song);
            }

            LibraryUpdated?.Invoke();
            await SaveCacheAsync();
        }

        public async Task AddSongAsync(Song song)
        {
            Songs.Insert(0, song); // Add to top
            LibraryUpdated?.Invoke();
            await SaveCacheAsync();
        }

        public async Task DeleteSongAsync(Song song)
        {
            if (File.Exists(song.FilePath))
            {
                try
                {
                    File.Delete(song.FilePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deleting file: {ex.Message}");
                }
            }

            Songs.Remove(song);
            LibraryUpdated?.Invoke();
            await SaveCacheAsync();
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Songs.ToList());
                await File.WriteAllTextAsync(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving library cache: {ex.Message}");
            }
        }
    }
}
