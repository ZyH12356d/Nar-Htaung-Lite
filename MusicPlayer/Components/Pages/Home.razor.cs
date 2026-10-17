using Microsoft.AspNetCore.Components;
using MusicPlayer.Models;
using MusicPlayer.Services;

namespace MusicPlayer.Components.Pages;

public partial class Home : IDisposable
{
    [Inject] private PlayerService PlayerService { get; set; } = default!;
    [Inject] private LibraryService LibraryService { get; set; } = default!;
    private bool _loading = true;
    private string _filter = string.Empty;
    private string Filter
    {
        get => _filter;
        set { _filter = value; ApplyFilter(); }
    }
    private string? _libraryError;
    private List<Song> FilteredSongs = new();
    private void ApplyFilter() => FilteredSongs = LibraryService.Songs.Where(s =>
        s.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || s.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
    private Song? _songToDelete;
    protected override async Task OnInitializedAsync()
    {
        LibraryService.LibraryUpdated += LibraryChanged;
        PlayerService.PropertyChanged += PlayerChanged;
        await LibraryService.LoadLibraryAsync();
        await PlayerService.InitializeAsync();
        ApplyFilter();
        _loading = false;
    }
    private void LibraryChanged() => _ = InvokeAsync(() =>
    {
        ApplyFilter();
        StateHasChanged();
    });
    private void PlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.CurrentSong) or nameof(PlayerService.IsPlaying))
            _ = InvokeAsync(StateHasChanged);
    }
    private async Task PlayAndExpand(Song song)
    {
        await PlayerService.PlayFromCollectionAsync(null, song);
        PlayerService.IsPlayerExpanded = true;
    }
    private async Task ShuffleAll()
    {
        await PlayerService.PlayFromCollectionAsync(null, shuffle: true);
        PlayerService.IsPlayerExpanded = PlayerService.CurrentSong is not null;
    }
    private void AskDelete(Song song) => _songToDelete = song;
    private void CancelDelete() => _songToDelete = null;
    private async Task ConfirmDelete()
    {
        try { if (_songToDelete is { } song) await LibraryService.DeleteSongAsync(song); }
        catch (Exception ex) { _libraryError = $"Could not delete this track. {ex.Message}"; }
        _songToDelete = null;
    }
    public void Dispose()
    {
        LibraryService.LibraryUpdated -= LibraryChanged;
        PlayerService.PropertyChanged -= PlayerChanged;
    }
}

