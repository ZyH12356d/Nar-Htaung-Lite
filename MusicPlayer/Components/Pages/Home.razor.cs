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

            var songs = new List<Song>();

            foreach (var f in files)
            {
                try
                {
                    using (var tfile = TagLib.File.Create(f))
                    {
                        var song = new Song
                        {
                            Title = !string.IsNullOrEmpty(tfile.Tag.Title) ? tfile.Tag.Title : Path.GetFileNameWithoutExtension(f),
                            Author = tfile.Tag.FirstPerformer ?? tfile.Tag.FirstAlbumArtist ?? "Unknown Artist",
                            FilePath = f
                        };

                        if (tfile.Tag.Pictures != null && tfile.Tag.Pictures.Length > 0)
                        {
                            var bin = tfile.Tag.Pictures[0].Data.Data;
                            var mimeType = tfile.Tag.Pictures[0].MimeType ?? "image/jpeg";
                            song.ThumbnailDataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(bin)}";
                        }

                        songs.Add(song);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading tags for {f}: {ex.Message}");
                    songs.Add(new Song
                    {
                        Title = Path.GetFileNameWithoutExtension(f),
                        Author = "Unknown Artist",
                        FilePath = f
                    });
                }
            }

            downloadedSongs = songs;
            PlayerService.Playlist = downloadedSongs;
        }



        private void DeleteSong(Song song)
        {
            if (File.Exists(song.FilePath))
            {
                File.Delete(song.FilePath);
                RefreshLibrary();
            }
        }
    }
}
