using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayer.Components.Pages.Search
{
    partial class Search
    {
        private string searchQuery = "";
        private bool isSearching = false;
        private bool isDownloading = false;
        private List<VideoSearchResult> results = new();
        private YoutubeClient youtube = new();

        private async Task HandleSearch()
        {
            if (string.IsNullOrWhiteSpace(searchQuery)) return;

            isSearching = true;
            results.Clear();

            try
            {
                var searchResults = await youtube.Search.GetVideosAsync(searchQuery).CollectAsync(20);
                results = searchResults.ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                isSearching = false;
            }
        }

        private async Task DownloadAudio(VideoSearchResult video)
        {
            isDownloading = true;
            StateHasChanged();

            try
            {
                var httpClient = new HttpClient();
                var baseUrl = DeviceInfo.Platform == DevicePlatform.Android ? "http://10.1.40.23:5181" : "http://localhost:5181";
                
                // 1. Request the stream from the backend
                var response = await httpClient.GetAsync($"{baseUrl}/audio?id={video.Id}");

                if (!response.IsSuccessStatusCode)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        await App.Current.MainPage.DisplayAlert("API Error", $"Status: {response.StatusCode}\n{error}", "OK");
                    });
                    return;
                }

                // 2. Prepare the local file paths
                var sanitizedTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/webm";
                var extension = contentType.Contains("/") ? contentType.Split('/').Last() : "m4a";
                var fileName = $"{sanitizedTitle}.{extension}";
                var thumbName = $"{sanitizedTitle}.jpg";
                
                var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
                var thumbPath = Path.Combine(FileSystem.AppDataDirectory, thumbName);

                // 3. Download the audio stream
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(filePath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                // 4. Download and save the thumbnail
                var thumbUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Width).FirstOrDefault()?.Url;
                if (!string.IsNullOrEmpty(thumbUrl))
                {
                    try 
                    {
                        var thumbBytes = await httpClient.GetByteArrayAsync(thumbUrl);
                        await File.WriteAllBytesAsync(thumbPath, thumbBytes);
                    }
                    catch { /* Ignore thumbnail download errors */ }
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await App.Current.MainPage.DisplayAlert("Download Complete", $"Saved to:\n{filePath}", "OK");
                });
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await App.Current.MainPage.DisplayAlert("Download Error", ex.Message, "OK");
                });
            }
            finally
            {
                isDownloading = false;
                StateHasChanged();
            }
        }
    }
}
