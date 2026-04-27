using System;
using System.Collections.Generic;
using System.Text;

namespace MusicPlayer.Components.Pages
{
    partial class Player
    {
        protected override void OnInitialized()
        {
            PlayerService.PropertyChanged += HandlePropertyChanged;
        }

        private void HandlePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            StateHasChanged();
        }

        private void GoHome()
        {
            NavigationManager.NavigateTo("/");
        }

        public void Dispose()
        {
            PlayerService.PropertyChanged -= HandlePropertyChanged;
        }
    }
}
