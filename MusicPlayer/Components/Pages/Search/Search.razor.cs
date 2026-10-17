using Microsoft.AspNetCore.Components;
using YoutubeExplode;
using YoutubeExplode.Search;
namespace MusicPlayer.Components.Pages.Search;

public partial class Search : IAsyncDisposable
{
    [Inject] public Services.DownloadService DownloadService { get; set; } = default!;
    [Inject] public YoutubeClient Youtube { get; set; } = default!;
    [Inject] public Services.LibraryService Library { get; set; } = default!;
    private VideoSearchResult? _pendingDownload;
    private readonly HashSet<string> _downloadCollections = new();
    private const int PageSize = 15;
    private string searchQuery = string.Empty;
    private bool isSearching, isLoadingMore, hasMore, _hasSearched;
    private string _error = string.Empty;
    private List<VideoSearchResult> results = new();
    private IAsyncEnumerator<VideoSearchResult>? _enumerator;
    private static readonly string[] Suggestions = { "Burmese classics", "Acoustic sessions", "Late night jazz", "Piano" };
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _fetchTask;
    protected override void OnInitialized() => DownloadService.DownloadsChanged += DownloadChanged;
    private void DownloadChanged() => _ = InvokeAsync(StateHasChanged);
    private async Task SearchSuggestion(string value) { searchQuery = value; await HandleSearch(); }
    private async Task HandleSearch()
    {
        if (string.IsNullOrWhiteSpace(searchQuery) || isSearching || isLoadingMore) return;
        isSearching = true; _hasSearched = true; _error = string.Empty; results.Clear(); hasMore = false;
        try
        {
            if (_enumerator is not null) await _enumerator.DisposeAsync();
            _enumerator = Youtube.Search.GetVideosAsync(searchQuery.Trim()).GetAsyncEnumerator(_lifetime.Token);
            await FetchNextPageAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _error = $"Search couldn’t finish. {ex.Message}"; }
        finally { isSearching = false; }
    }
    private async Task LoadMore()
    {
        if (_enumerator is null || isSearching || isLoadingMore || !hasMore) return;
        isLoadingMore = true; _error = string.Empty;
        try { await FetchNextPageAsync(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _error = $"More results couldn’t load. {ex.Message}"; }
        finally { isLoadingMore = false; }
    }
    private Task FetchNextPageAsync() => _fetchTask = FetchCoreAsync();
    private async Task FetchCoreAsync()
    {
        if (_enumerator is null) return;
        var page = await Task.Run(async () =>
        {
            var loaded = new List<VideoSearchResult>();
            while (loaded.Count < PageSize && await _enumerator.MoveNextAsync()) loaded.Add(_enumerator.Current);
            return loaded;
        }, _lifetime.Token);
        results.AddRange(page.Where(video => results.All(existing => existing.Id != video.Id)));
        hasMore = page.Count == PageSize;
    }
    private async Task DownloadAudio(VideoSearchResult video)
    {
        await Library.LoadLibraryAsync();
        if (!Library.Collections.Any()) { DownloadService.Enqueue(video); return; }
        _downloadCollections.Clear();
        _pendingDownload = video;
    }
    private void ToggleDestination(string id) { if (!_downloadCollections.Add(id)) _downloadCollections.Remove(id); }
    private void CancelDownload() => _pendingDownload = null;
    private void ConfirmDownload()
    {
        if (_pendingDownload is null) return;
        DownloadService.Enqueue(_pendingDownload, _downloadCollections.ToArray());
        _pendingDownload = null;
    }
    public async ValueTask DisposeAsync()
    {
        DownloadService.DownloadsChanged -= DownloadChanged;
        await _lifetime.CancelAsync();
        if (_fetchTask is not null) { try { await _fetchTask; } catch { } }
        if (_enumerator is not null) await _enumerator.DisposeAsync();
        _lifetime.Dispose();
    }
}
