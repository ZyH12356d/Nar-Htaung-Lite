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
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isProcessing = false;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        private Task? _ffmpegInitializationTask;

        public DownloadService(HttpClient httpClient, YoutubeExplode.YoutubeClient youtube)
        {
            _httpClient = httpClient;
            _youtube = youtube;

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

                    await _youtube.Videos.DownloadAsync(
                        item.VideoId, 
                        filePath, 
                        o => o.SetContainer("mp3")
                              .SetPreset(ConversionPreset.UltraFast)
                              .SetFFmpegPath(ffmpegPath),
                        progressReporter);
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

                    await _youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, progressReporter);
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
