using System;
using System.Collections.Generic;
using System.Text;
using MusicPlayer.Services;
using MusicPlayer.Models;
using Microsoft.AspNetCore.Components;

namespace MusicPlayer.Components.Pages
{
    partial class Home
    {
        [Inject] private PlayerService PlayerService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private List<Song> downloadedSongs = new();

        protected override void OnInitialized()
        {
            RefreshLibrary();
        }

        private void RefreshLibrary()
        {
            var path = FileSystem.AppDataDirectory;
            var files = Directory.EnumerateFiles(path)
                                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".m4a") || f.EndsWith(".webm"))
                                .ToList();

            downloadedSongs = files.Select(f => {
                var fileName = Path.GetFileNameWithoutExtension(f);
                var thumbPath = Path.Combine(path, fileName + ".jpg");
                var song = new Song {
                    Title = fileName,
                    Author = "Unknown Artist",
                    FilePath = f,
                    ThumbnailPath = File.Exists(thumbPath) ? thumbPath : ""
                };
                
                // Pre-calculate data URL safely to prevent UI rendering crashes
                if (!string.IsNullOrEmpty(song.ThumbnailPath))
                {
                    try {
                        byte[] bytes = File.ReadAllBytes(song.ThumbnailPath);
                        song.ThumbnailDataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
                    } catch { /* Handle potential IO issues gracefully */ }
                }

                return song;
            }).ToList();

            PlayerService.Playlist = downloadedSongs;
        }



        private void DeleteSong(Song song)
        {
            if (File.Exists(song.FilePath))
            {
                File.Delete(song.FilePath);
                
                // Also delete thumbnail if exists
                if (!string.IsNullOrEmpty(song.ThumbnailPath) && File.Exists(song.ThumbnailPath))
                {
                    File.Delete(song.ThumbnailPath);
                }

                RefreshLibrary();
            }
        }
    }
}
