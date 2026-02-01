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
        private List<VideoSearchResult> results = new();
        private YoutubeClient youtube = new();

        private async Task HandleSearch()
        {
            if (string.IsNullOrWhiteSpace(searchQuery)) return;

            isSearching = true;
            results.Clear();

            try
            {
                // This fetches the first 20 results for your search query
                var searchResults = await youtube.Search.GetVideosAsync(searchQuery).CollectAsync(20);
                results = searchResults.ToList();
            }
            catch (Exception ex)
            {
                // You can log errors here
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                isSearching = false;
            }
        }
        private async Task DownloadAudio(VideoSearchResult video)
        {
            isSearching = true;
            StateHasChanged();

            try
            {
                var httpClient = new HttpClient();

                var requestBody = new
                {
                    url = video.Url
                };

                var response = await httpClient.PostAsJsonAsync(
                    "http://192.168.100.15:5000/download/audio",
                    requestBody
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Failed to download audio.");
                }

                // Get filename from response header
                var contentDisposition = response.Content.Headers.ContentDisposition;
                var fileName = contentDisposition?.FileName?.Trim('"')
                              ?? $"{video.Id}.m4a";

                // App local storage
                var filePath = Path.Combine(
                    FileSystem.AppDataDirectory,
                    fileName
                );

                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(filePath);
                await stream.CopyToAsync(fileStream);

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    isSearching = false;
                    await App.Current.MainPage.DisplayAlert(
                        "Downloaded",
                        $"Saved to:\n{filePath}",
                        "OK"
                    );
                });
            }
            catch (Exception ex)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    isSearching = false;
                    await App.Current.MainPage.DisplayAlert(
                        "Download Failed",
                        ex.Message,
                        "OK"
                    );
                });
            }
        }
    }
}
