using MusicPlayer.Models;
using System.Collections.ObjectModel;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayer.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly YoutubeExplode.YoutubeClient _youtube;
        private readonly LibraryService _libraryService;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isProcessing = false;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        /// <summary>Number of songs currently waiting, processing, or downloading.</summary>
        public int ActiveDownloadCount =>
            Downloads.Count(d => d.Status == DownloadStatus.Waiting ||
                                 d.Status == DownloadStatus.Processing ||
                                 d.Status == DownloadStatus.Downloading);

        /// <summary>Fires whenever a download is enqueued, completes, or fails.</summary>
        public event Action? DownloadsChanged;

        private Task? _ffmpegInitializationTask;

        public DownloadService(HttpClient httpClient, YoutubeExplode.YoutubeClient youtube, LibraryService libraryService)
        {
            _httpClient = httpClient;
            _youtube = youtube;
            _libraryService = libraryService;

            // Ensure FFmpeg is available on Windows
            if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                _ffmpegInitializationTask = InitializeFfmpegAsync();
            }
        }

        private async Task InitializeFfmpegAsync()
        {
            try
            {
                var ffmpegPath = Path.Combine(FileSystem.AppDataDirectory, "ffmpeg.exe");
                Console.WriteLine($"Checking for FFmpeg at: {ffmpegPath}");
                
                if (!File.Exists(ffmpegPath))
                {
                    Console.WriteLine("FFmpeg not found, downloading...");
                    // This will download ffmpeg.exe, ffprobe.exe etc to the AppDataDirectory
                    await Xabe.FFmpeg.Downloader.FFmpegDownloader.GetLatestVersion(
                        Xabe.FFmpeg.Downloader.FFmpegVersion.Official, 
                        FileSystem.AppDataDirectory);
                    Console.WriteLine("FFmpeg download completed.");
                }
                else
                {
                    Console.WriteLine("FFmpeg already exists.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FFmpeg initialization failed: {ex.Message}");
                throw; // Rethrow to let the waiter know it failed
            }
        }

        public void Enqueue(VideoSearchResult video)
        {
            var item = new DownloadItem
            {
                VideoId = video.Id.Value,
                Title = video.Title,
                Author = video.Author.ChannelTitle,
                ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Width).FirstOrDefault()?.Url ?? string.Empty,
                Status = DownloadStatus.Waiting,
                Progress = 0
            };

            Downloads.Insert(0, item); // Show newest at top
            DownloadsChanged?.Invoke();
            
            _ = Task.Run(() => ProcessQueueAsync());
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;

                while (true)
                {
                    var item = Downloads.FirstOrDefault(d => d.Status == DownloadStatus.Waiting);
                    if (item == null) break;

                    await DownloadItemAsync(item);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task DownloadItemAsync(DownloadItem item)
        {
            await _semaphore.WaitAsync();
            try
            {
                item.Status = DownloadStatus.Processing;
                item.Progress = 0;

                var sanitizedTitle = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars()));
                
                // Truncate to prevent IO Path TooLong on Android (max 60 characters)
                if (sanitizedTitle.Length > 60)
                {
                    sanitizedTitle = sanitizedTitle.Substring(0, 60).TrimEnd('_');
                }

                // Progress reporter
                var progressReporter = new Progress<double>(p =>
                {
                    item.Progress = Math.Round(p * 100, 1);
                    if (item.Progress > 0 && item.Status == DownloadStatus.Processing)
                    {
                        item.Status = DownloadStatus.Downloading;
                    }
                });

                bool useConverter = DeviceInfo.Platform == DevicePlatform.WinUI;
                string fileName = sanitizedTitle + (useConverter ? ".mp3" : ".m4a");
                string filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                if (useConverter)
                {
                    // Windows: Use FFmpeg for MP3 conversion
                    if (_ffmpegInitializationTask != null)
                    {
                        item.Status = DownloadStatus.Processing;
                        item.ErrorMessage = "Initializing FFmpeg...";
                        await _ffmpegInitializationTask;
                    }

                    var ffmpegPath = Path.Combine(FileSystem.AppDataDirectory, "ffmpeg.exe");
                    if (!File.Exists(ffmpegPath))
                    {
                         throw new Exception($"FFmpeg not found at {ffmpegPath}. Please restart the app to retry downloading it.");
                    }

                    int maxRetries = 3;
                    int delayMs = 2000;
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            await _youtube.Videos.DownloadAsync(
                                item.VideoId, 
                                filePath, 
                                o => o.SetContainer("mp3")
                                      .SetPreset(ConversionPreset.UltraFast)
                                      .SetFFmpegPath(ffmpegPath),
                                progressReporter);
                            break; // Success
                        }
                        catch (Exception ex) when (i < maxRetries - 1)
                        {
                            Console.WriteLine($"Windows download attempt {i + 1} failed, retrying in {delayMs}ms... Error: {ex.Message}");
                            await Task.Delay(delayMs);
                            delayMs *= 2;
                        }
                    }
                }
                else
                {
                    // Android/iOS: Download M4A stream directly (no FFmpeg needed)
                    var manifest = await _youtube.Videos.Streams.GetManifestAsync(item.VideoId);
                    var streamInfo = manifest.GetAudioOnlyStreams()
                        .Where(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                        .GetWithHighestBitrate();
                    
                    if (streamInfo == null) 
                        throw new Exception("No suitable M4A audio stream found for this video.");

                    int maxRetries = 3;
                    int delayMs = 2000;
                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            await _youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progressReporter);
                            break; // Success
                        }
                        catch (Exception ex) when (i < maxRetries - 1)
                        {
                            Console.WriteLine($"Android/iOS download attempt {i + 1} failed, retrying in {delayMs}ms... Error: {ex.Message}");
                            await Task.Delay(delayMs);
                            delayMs *= 2;
                        }
                    }
                }

                item.Status = DownloadStatus.Processing; // Tagging phase
                
                // Get video metadata for tagging
                var video = await _youtube.Videos.GetAsync(item.VideoId);

                // Download thumbnail for cover art
                byte[]? thumbBytes = null;
                var thumb = video.Thumbnails.GetWithHighestResolution();
                if (thumb != null)
                {
                    try
                    {
                        thumbBytes = await _httpClient.GetByteArrayAsync(thumb.Url);
                    }
                    catch { /* Ignore thumbnail download errors */ }
                }

                // Tagging with TagLibSharp
                using (var tfile = TagLib.File.Create(filePath))
                {
                    tfile.Tag.Title = video.Title;
                    tfile.Tag.Performers = new[] { video.Author.ChannelTitle };
                    tfile.Tag.AlbumArtists = new[] { video.Author.ChannelTitle };
                    tfile.Tag.Album = video.Author.ChannelTitle;

                    if (thumbBytes != null)
                    {
                        var picture = new TagLib.Picture(new TagLib.ByteVector(thumbBytes))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            Description = "Cover",
                            MimeType = "image/jpeg"
                        };
                        tfile.Tag.Pictures = new TagLib.IPicture[] { picture };
                    }

                    tfile.Save();
                }

                var newSong = new Song
                {
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    FilePath = filePath
                };

                if (thumbBytes != null)
                {
                    newSong.ThumbnailDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(thumbBytes)}";
                }

                await _libraryService.AddSongAsync(newSong);

                item.Status = DownloadStatus.Completed;
                item.Progress = 100;
            }
            catch (YoutubeExplode.Exceptions.VideoUnavailableException vex)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = "YouTube is blocking the request (Bot Detection). Try again later or use a different video.";
                Console.WriteLine($"Bot Detection: {vex.Message}");
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = ex.Message;
            }
            finally
            {
                _semaphore.Release();
                DownloadsChanged?.Invoke(); // Notify badge when a download finishes or fails
            }
        }

        public void ClearHistory()
        {
            var itemsToRemove = Downloads.Where(d => d.Status == DownloadStatus.Completed || d.Status == DownloadStatus.Failed).ToList();
            foreach (var item in itemsToRemove)
            {
                Downloads.Remove(item);
            }
        }
    }
}
