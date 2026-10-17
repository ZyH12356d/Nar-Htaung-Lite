using Microsoft.Extensions.FileProviders;
using MusicPlayer.Components;
using MusicPlayer.Services;
using YoutubeExplode;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<DownloadService>();
builder.Services.AddScoped<NavigationService>();
builder.Services.AddScoped(_ => new YoutubeClient(new HttpClient(new PreviewHttpHandler())));
var app = builder.Build();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../MusicPlayer/wwwroot"));
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(root) });
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<PreviewApp>().AddInteractiveServerRenderMode();
app.Run();

internal sealed class PreviewHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromException<HttpResponseMessage>(new InvalidOperationException("This visual preview does not contact YouTube."));
}

namespace MusicPlayer { public class MauiProgram { } }
