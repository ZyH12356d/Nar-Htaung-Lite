using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.FileProviders;
using MusicPlayer.Services;

namespace MusicPlayer;

public class LibraryBlazorWebView : BlazorWebView
{
    public override IFileProvider CreateFileProvider(string contentRootDir) => new CompositeFileProvider(
        new ArtworkFileProvider(FileSystem.AppDataDirectory), base.CreateFileProvider(contentRootDir));
}
