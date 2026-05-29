using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MusicPlayer.Services;
using MusicPlayer.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MusicPlayer.Components.Pages
{
    partial class Home : IDisposable
    {
        [Inject] private PlayerService PlayerService { get; set; } = default!;
        [Inject] private LibraryService LibraryService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        private System.Timers.Timer? _pressTimer;
        private bool _longPressTriggered;

        // Delete confirmation modal state
        private bool _showDeleteModal;
        private Song? _songToDelete;

        protected override async Task OnInitializedAsync()
        {
            LibraryService.LibraryUpdated += OnLibraryUpdated;
            PlayerService.PropertyChanged += OnPlayerServicePropertyChanged;
            
            await LibraryService.LoadLibraryAsync();
            PlayerService.Playlist = LibraryService.Songs.ToList();
        }

        private void OnLibraryUpdated()
        {
            PlayerService.Playlist = LibraryService.Songs.ToList();
            _ = InvokeAsync(StateHasChanged);
        }

        private void OnPlayerServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerService.CurrentSong) || e.PropertyName == nameof(PlayerService.IsPlaying))
            {
                _ = InvokeAsync(StateHasChanged);
            }
        }

        public void Dispose()
        {
            LibraryService.LibraryUpdated -= OnLibraryUpdated;
            PlayerService.PropertyChanged -= OnPlayerServicePropertyChanged;
            _pressTimer?.Dispose();
        }

        private void HandlePointerDown(Song song)
        {
            _longPressTriggered = false;
            _pressTimer?.Dispose();
            _pressTimer = new System.Timers.Timer(800); // 800ms long press
            _pressTimer.AutoReset = false;
            _pressTimer.Elapsed += (sender, e) => 
            {
                _longPressTriggered = true;
                _ = InvokeAsync(() =>
                {
                    _songToDelete = song;
                    _showDeleteModal = true;
                    StateHasChanged();
                });
            };
            _pressTimer.Start();
        }

        private void HandlePointerUpOrLeave()
        {
            if (_pressTimer != null)
            {
                _pressTimer.Stop();
                _pressTimer.Dispose();
                _pressTimer = null;
            }
        }

        private async Task ConfirmDelete()
        {
            if (_songToDelete != null)
            {
                await LibraryService.DeleteSongAsync(_songToDelete);
            }
            _showDeleteModal = false;
            _songToDelete = null;
        }

        private void CancelDelete()
        {
            _showDeleteModal = false;
            _songToDelete = null;
        }

        private void PlayAndExpand(Song song)
        {
            if (_longPressTriggered)
            {
                // It was a long press, do not play
                return;
            }
            
            _ = PlayerService.PlaySong(song);
            PlayerService.IsPlayerExpanded = true;
        }
    }
}

