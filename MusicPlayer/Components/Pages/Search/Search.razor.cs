using System;
using System.Collections.Generic;
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
            // 1. Give the user immediate feedback in the UI
            isSearching = true;
            StateHasChanged();

            await Task.Run(async () =>
            {
                try
                {
                    // Use a Timeout so it doesn't hang forever if internet is slow
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

                    // Get manifest without blocking the UI
                    var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id)
                        .ConfigureAwait(false);

                    var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                    // Prepare File Path
                    string folder = FileSystem.Current.AppDataDirectory;
                    string cleanName = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
                    string filePath = Path.Combine(folder, $"{cleanName}.mp3");

                    // Use DownloadAsync with the cancellation token
                    await youtube.Videos.Streams.DownloadAsync(streamInfo, filePath, null, cts.Token)
                        .ConfigureAwait(false);

                    await MainThread.InvokeOnMainThreadAsync(async () => {
                        isSearching = false;
                        await App.Current.MainPage.DisplayAlert("Success", "Saved!", "OK");
                    });
                }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () => {
                        isSearching = false;
                        await App.Current.MainPage.DisplayAlert("Download Failed", ex.Message, "OK");
                    });
                }
            }).ConfigureAwait(false);
        }
    }
}
