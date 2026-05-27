using MusicPlayer.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayer.Components.Pages.Search
{
    partial class Search : IAsyncDisposable
    {
        [Microsoft.AspNetCore.Components.Inject]
        public MusicPlayer.Services.DownloadService DownloadService { get; set; } = default!;

        [Microsoft.AspNetCore.Components.Inject]
        public YoutubeClient Youtube { get; set; } = default!;

        private const int PageSize = 15;

        private string searchQuery = "";
        private bool isSearching = false;
        private bool isLoadingMore = false;
        private bool hasMore = false;
        private List<VideoSearchResult> results = new();

        // Cursor: keep the async enumerator alive between pages
        private IAsyncEnumerator<VideoSearchResult>? _enumerator;

        private async Task HandleSearch()
        {
            if (string.IsNullOrWhiteSpace(searchQuery)) return;

            // Dispose the previous search cursor
            if (_enumerator != null)
            {
                await _enumerator.DisposeAsync();
                _enumerator = null;
            }

            isSearching = true;
            results.Clear();
            hasMore = false;

            try
            {
                _enumerator = Youtube.Search.GetVideosAsync(searchQuery).GetAsyncEnumerator();
                await FetchNextPageAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search error: {ex.Message}");
            }
            finally
            {
                isSearching = false;
            }
        }

        private async Task LoadMore()
        {
            if (_enumerator == null || isLoadingMore || !hasMore) return;
            isLoadingMore = true;
            try
            {
                await FetchNextPageAsync();
            }
            finally
            {
                isLoadingMore = false;
            }
        }

        /// <summary>Advances the cursor by PageSize items and updates hasMore.</summary>
        private async Task FetchNextPageAsync()
        {
            if (_enumerator == null) return;

            int loaded = 0;
            while (loaded < PageSize)
            {
                bool moved = await _enumerator.MoveNextAsync();
                if (!moved)
                {
                    hasMore = false;
                    return;
                }
                results.Add(_enumerator.Current);
                loaded++;
            }

            // If we filled the page exactly, assume there may be more
            hasMore = true;
        }

        private void DownloadAudio(VideoSearchResult video)
        {
            DownloadService.Enqueue(video);
        }

        public async ValueTask DisposeAsync()
        {
            if (_enumerator != null)
                await _enumerator.DisposeAsync();
        }
    }
}
