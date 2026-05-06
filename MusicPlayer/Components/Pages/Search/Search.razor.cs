using MusicPlayer.Models;
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
        [Microsoft.AspNetCore.Components.Inject]
        public MusicPlayer.Services.DownloadService DownloadService { get; set; } = default!;

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

        private void DownloadAudio(VideoSearchResult video)
        {
            DownloadService.Enqueue(video);
        }
    }
}
