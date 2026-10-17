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
    private Song? _songToDelete;
    private IEnumerable<Song> FilteredSongs => LibraryService.Songs.Where(s =>
        s.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase) || s.Author.Contains(_filter, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        LibraryService.LibraryUpdated += LibraryChanged;
        PlayerService.PropertyChanged += PlayerChanged;
        await LibraryService.LoadLibraryAsync();
        PlayerService.Playlist = LibraryService.Songs.ToList();
        _loading = false;
    }
    private void LibraryChanged() => _ = InvokeAsync(() =>
    {
        PlayerService.Playlist = LibraryService.Songs.ToList();
        StateHasChanged();
    });
    private void PlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.CurrentSong) or nameof(PlayerService.IsPlaying))
            _ = InvokeAsync(StateHasChanged);
    }
    private async Task PlayAndExpand(Song song)
    {
        PlayerService.Playlist = LibraryService.Songs.ToList();
        await PlayerService.PlaySong(song);
        PlayerService.IsPlayerExpanded = true;
    }
    private async Task ShuffleAll()
    {
        PlayerService.Playlist = LibraryService.Songs.ToList();
        PlayerService.IsShuffle = true;
        await PlayAndExpand(LibraryService.Songs[Random.Shared.Next(LibraryService.Songs.Count)]);
    }
    private void AskDelete(Song song) => _songToDelete = song;
    private void CancelDelete() => _songToDelete = null;
    private async Task ConfirmDelete()
    {
        if (_songToDelete is { } song) await LibraryService.DeleteSongAsync(song);
        _songToDelete = null;
    }
    public void Dispose()
    {
        LibraryService.LibraryUpdated -= LibraryChanged;
        PlayerService.PropertyChanged -= PlayerChanged;
    }
}

