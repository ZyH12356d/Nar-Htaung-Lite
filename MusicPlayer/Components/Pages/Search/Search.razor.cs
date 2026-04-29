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
                
                // 1. Request the Song model from the backend
                var song = await httpClient.GetFromJsonAsync<Song>($"{baseUrl}/audio?id={video.Id}");

                if (song == null || song.SongFile == null)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await App.Current.MainPage.DisplayAlert("API Error", "Failed to get song data from API.", "OK");
                    });
                    return;
                }

                // 2. Prepare the local file paths
                var sanitizedTitle = string.Join("_", song.Title.Split(Path.GetInvalidFileNameChars()));
                var fileName = $"{sanitizedTitle}.webm";
                var thumbName = $"{sanitizedTitle}.jpg";
                
                var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
                var thumbPath = Path.Combine(FileSystem.AppDataDirectory, thumbName);

                // 3. Save the audio file
                await File.WriteAllBytesAsync(filePath, song.SongFile);

                // 4. Save the thumbnail
                if (!string.IsNullOrEmpty(song.ThumbnailDataUrl))
                {
                    try 
                    {
                        var base64Data = song.ThumbnailDataUrl.Contains(",") ? song.ThumbnailDataUrl.Split(',')[1] : song.ThumbnailDataUrl;
                        var thumbBytes = Convert.FromBase64String(base64Data);
                        await File.WriteAllBytesAsync(thumbPath, thumbBytes);
                    }
                    catch { /* Ignore thumbnail save errors */ }
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
