using Microsoft.Extensions.Logging;
using MusicPlayer.Models;
using System.Collections.ObjectModel;
using System.Net;
using YoutubeExplode.Converter;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayer.Services
{
    public class DownloadService
    {
        private readonly YoutubeExplode.YoutubeClient _youtube;
        private readonly LibraryService _libraryService;
        private readonly ILogger<DownloadService> _logger;
        private readonly CoverArtService _coverArt;
        private bool _isProcessing;
        private Task? _ffmpegInitializationTask;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        public int ActiveDownloadCount => Downloads.Count(IsActive);

        public event Action? DownloadsChanged;

        public DownloadService(YoutubeExplode.YoutubeClient youtube,
            LibraryService libraryService, ILogger<DownloadService> logger, CoverArtService coverArt)
        {
            _youtube = youtube;
            _libraryService = libraryService;
            _logger = logger;
            _coverArt = coverArt;
        }

        private async Task InitializeFfmpegAsync()
        {
            var ffmpegPath = Path.Combine(FileSystem.AppDataDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                await Xabe.FFmpeg.Downloader.FFmpegDownloader.GetLatestVersion(
                    Xabe.FFmpeg.Downloader.FFmpegVersion.Official,
                    FileSystem.AppDataDirectory);
            }
        }

        public void Enqueue(VideoSearchResult video, IEnumerable<string>? collectionIds = null)
        {
            var destinations = collectionIds?.Distinct().ToArray() ?? Array.Empty<string>();
            // Queue ownership stays on the UI thread; downloads run on a worker.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Downloads.Any(d => d.VideoId == video.Id.Value && IsActive(d)))
                    return;

                Downloads.Insert(0, new DownloadItem
                {
                    VideoId = video.Id.Value,
                    Title = video.Title,
                    Author = video.Author.ChannelTitle,
                    ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Width)
                        .FirstOrDefault()?.Url ?? string.Empty,
                    ThumbnailUrls = video.Thumbnails.OrderByDescending(t => t.Resolution.Width).Select(t => t.Url).ToArray(),
                    CollectionIds = destinations,
                    Status = DownloadStatus.Waiting
                });
                DownloadsChanged?.Invoke();
                _ = ProcessQueueAsync();
            });
        }

        private async Task ProcessQueueAsync()
        {
            if (_isProcessing)
                return;

            _isProcessing = true;
            try
            {
                // The collection shows newest first; process requests oldest first.
                while (Downloads.LastOrDefault(d => d.Status == DownloadStatus.Waiting) is { } item)
                    await Task.Run(() => DownloadItemAsync(item));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The download queue stopped unexpectedly.");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task DownloadItemAsync(DownloadItem item)
        {
            var stage = "Preparing download";
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    item.Status = DownloadStatus.Processing;
                    item.Progress = 0;
                    item.ErrorMessage = string.Empty;
                });

                var useConverter = DeviceInfo.Platform == DevicePlatform.WinUI;
                var extension = useConverter ? ".mp3" : ".m4a";
                var title = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars()));
                title = title.Length > 60 ? title[..60] : title;
                title = title.Trim().TrimEnd('.');
                if (string.IsNullOrEmpty(title))
                    title = "Audio";

                // Different videos with the same title must not overwrite each other.
                var filePath = Path.Combine(FileSystem.AppDataDirectory,
                    $"{title}-{item.VideoId}-{Guid.NewGuid():N}{extension}");

                var ffmpegPath = Path.Combine(FileSystem.AppDataDirectory, "ffmpeg.exe");
                if (useConverter)
                {
                    stage = "Initializing FFmpeg";
                    try
                    {
                        _ffmpegInitializationTask ??= InitializeFfmpegAsync();
                        await _ffmpegInitializationTask;
                    }
                    catch
                    {
                        _ffmpegInitializationTask = null;
                        throw;
                    }

                    if (!File.Exists(ffmpegPath))
                        throw new FileNotFoundException("FFmpeg is required for Windows MP3 conversion.");
                }

                var artworkTask = _coverArt.GetAsync(item.VideoId,
                    item.ThumbnailUrls.Count > 0 ? item.ThumbnailUrls : new[] { item.ThumbnailUrl });
                var transferAttempt = 0;

                await DownloadFile.SaveAsync(filePath, async temporaryPath =>
                {
                    var currentAttempt = ++transferAttempt;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        item.Status = DownloadStatus.Processing;
                        item.Progress = 0;
                        item.ErrorMessage = string.Empty;
                    });
                    var progress = new Progress<double>(p =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            // Ignore late callbacks after retrying or starting tagging.
                            if (currentAttempt == transferAttempt && item.Status == DownloadStatus.Downloading)
                                item.Progress = Math.Round(Math.Clamp(p, 0, 1) * 100, 1);
                        });
                    });

                    if (useConverter)
                    {
                        stage = "Downloading and converting audio";
                        await MainThread.InvokeOnMainThreadAsync(() => { item.Status = DownloadStatus.Downloading; });
                        await _youtube.Videos.DownloadAsync(item.VideoId, temporaryPath,
                            options => options.SetContainer("mp3")
                                .SetPreset(ConversionPreset.UltraFast)
                                .SetFFmpegPath(ffmpegPath), progress);
                    }
                    else
                    {
                        // Resolve inside each attempt, so a retry gets fresh stream URLs.
                        stage = "Getting audio streams";
                        var manifest = await _youtube.Videos.Streams.GetManifestAsync(item.VideoId);
                        var streams = manifest.GetAudioOnlyStreams()
                            .Where(s => s.Container == Container.Mp4).ToArray();
                        if (streams.Length == 0)
                            throw new InvalidOperationException("No downloadable M4A audio stream is available for this video.");

                        var stream = streams.GetWithHighestBitrate();
                        stage = "Downloading audio";
                        await MainThread.InvokeOnMainThreadAsync(() => { item.Status = DownloadStatus.Downloading; });
                        await _youtube.Videos.Streams.DownloadAsync(stream, temporaryPath, progress);

                        if (new FileInfo(temporaryPath).Length != stream.Size.Bytes)
                            throw new HttpRequestException("The audio download ended before the complete file was received.");
                    }

                    await MainThread.InvokeOnMainThreadAsync(() => { item.Status = DownloadStatus.Processing; });
                    stage = "Saving audio";
                }, async metadataPath =>
                {
                    var artwork = await artworkTask;
                    // MP4 artwork tags support JPEG/PNG. Other images remain available in the UI cache.
                    var tagArtwork = artwork?.MimeType is "image/jpeg" or "image/png" ? artwork : null;
                    WriteTags(metadataPath, item.Title, item.Author, tagArtwork?.Bytes, tagArtwork?.MimeType ?? "image/jpeg");
                }, (nextAttempt, wait, ex) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        item.Status = DownloadStatus.Processing;
                        item.Progress = 0;
                        item.ErrorMessage = $"Connection interrupted. Retrying ({nextAttempt}/{DownloadRetryPolicy.MaxAttempts}) in {wait.TotalSeconds:0}s...";
                    });
                    _logger.LogWarning(ex, "Retrying {VideoId} at {Stage}, attempt {Attempt}.",
                        item.VideoId, stage, nextAttempt);
                }, ex => _logger.LogWarning(ex, "Keeping audio without tags for {VideoId}.", item.VideoId),
                ex => _logger.LogWarning(ex, "Could not remove a temporary file for {VideoId}.", item.VideoId));

                stage = "Adding audio to library";
                var song = new Song
                {
                    Title = item.Title,
                    Author = item.Author,
                    FilePath = filePath,
                    VideoId = item.VideoId,
                    ThumbnailUrl = item.ThumbnailUrl
                };

                if (await artworkTask is { } cover)
                {
                    song.ThumbnailDataUrl = cover.DataUrl;
                    try { await _libraryService.CacheArtworkAsync(song, cover); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not cache artwork for {VideoId}.", item.VideoId); }
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await _libraryService.AddSongAsync(song);
                    item.ErrorMessage = string.Empty;
                    try { await _libraryService.AddSongToCollectionsAsync(song, item.CollectionIds); }
                    catch (Exception ex)
                    {
                        item.ErrorMessage = "Saved to All downloads, but the selected collection could not be updated.";
                        _logger.LogWarning(ex, "Could not add {VideoId} to its selected collections.", item.VideoId);
                    }
                    item.Progress = 100;
                    item.Status = DownloadStatus.Completed;
                });
            }
            catch (Exception ex)
            {
                var detail = ex switch
                {
                    RequestLimitExceededException => $"YouTube is limiting requests. Wait before trying again. {ex.Message}",
                    HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                        $"YouTube is limiting requests (HTTP 429). Wait before trying again. {ex.Message}",
                    HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
                        $"Access denied (HTTP 403). {ex.Message}",
                    _ => ex.Message
                };
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = $"{stage}: {detail}";
                });
                _logger.LogError(ex, "Download failed for {VideoId} at {Stage}.", item.VideoId, stage);
            }
            finally
            {
                await MainThread.InvokeOnMainThreadAsync(() => DownloadsChanged?.Invoke());
            }
        }

        private static void WriteTags(string path, string title, string author, byte[]? thumbnail, string mimeType)
        {
            using var file = TagLib.File.Create(path);
            file.Tag.Title = title;
            file.Tag.Performers = new[] { author };
            file.Tag.AlbumArtists = new[] { author };
            file.Tag.Album = author;
            if (thumbnail is not null)
            {
                file.Tag.Pictures = new TagLib.IPicture[]
                {
                    new TagLib.Picture(new TagLib.ByteVector(thumbnail))
                    {
                        Type = TagLib.PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = mimeType
                    }
                };
            }
            file.Save();
        }

        private static bool IsActive(DownloadItem item) =>
            item.Status is DownloadStatus.Waiting or DownloadStatus.Processing or DownloadStatus.Downloading;

        public void ClearHistory()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var item in Downloads.Where(d => !IsActive(d)).ToList())
                    Downloads.Remove(item);
            });
        }

        public void Retry(DownloadItem item)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (item.Status != DownloadStatus.Failed || Downloads.Any(d => d != item && d.VideoId == item.VideoId && IsActive(d)))
                    return;
                item.ErrorMessage = string.Empty;
                item.Progress = 0;
                item.Status = DownloadStatus.Waiting;
                DownloadsChanged?.Invoke();
                _ = ProcessQueueAsync();
            });
        }
    }
}
