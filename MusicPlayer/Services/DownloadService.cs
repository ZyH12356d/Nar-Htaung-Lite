using MusicPlayer.Models;
using System.Collections.ObjectModel;
using YoutubeExplode.Search;

namespace MusicPlayer.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isProcessing = false;

        public ObservableCollection<DownloadItem> Downloads { get; } = new();

        public DownloadService(HttpClient httpClient)
        {
            _httpClient = httpClient;
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

                var baseUrl = DeviceInfo.Platform == DevicePlatform.Android ? "http://192.168.137.1:5181" : "http://localhost:5181";
                var url = $"{baseUrl}/audio?id={item.VideoId}";

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                
                if (!response.IsSuccessStatusCode)
                {
                    item.Status = DownloadStatus.Failed;
                    item.ErrorMessage = $"Server returned {response.StatusCode}";
                    return;
                }

                item.Status = DownloadStatus.Downloading;
                var totalBytes = response.Content.Headers.ContentLength;
                
                using var contentStream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[8192];
                var totalRead = 0L;
                int bytesRead;

                var sanitizedTitle = string.Join(" ", item.Title.Split(Path.GetInvalidFileNameChars()));
                var fileName = $"{sanitizedTitle}.mp3";
                var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes.HasValue)
                    {
                        item.Progress = Math.Round((double)totalRead / totalBytes.Value * 100, 1);
                    }
                }

                item.Status = DownloadStatus.Completed;
                item.Progress = 100;
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
