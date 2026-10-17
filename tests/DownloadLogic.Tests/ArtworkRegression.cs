using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;

internal static class ArtworkRegression
{
    public const string DataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7xkAAAAASUVORK5CYII=";
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7xkAAAAASUVORK5CYII=");

    public static async Task FallbackAsync()
    {
        var calls = new List<string>();
        using var http = Client(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return calls.Count == 1 ? new(HttpStatusCode.NotFound) : Image();
        });
        var cover = await Service(http).GetAsync("jNQXAC9IVRw", new[] { "https://i.ytimg.com/vi/jNQXAC9IVRw/maxresdefault.jpg" });
        Check(cover?.MimeType == "image/png" && calls.Count == 2 && calls.Last().EndsWith("hqdefault.jpg"), "The fallback image was not used.");
    }

    public static async Task ContentTypeAsync()
    {
        using var http = Client(_ => Image("application/octet-stream"));
        var cover = await Service(http).GetAsync("jNQXAC9IVRw", Array.Empty<string>());
        Check(cover?.DataUrl.StartsWith("data:image/png;base64,") == true, "Valid image bytes with the wrong header were discarded.");
    }

    public static async Task ManyVariantsAsync()
    {
        var calls = 0;
        using var http = Client(request =>
        {
            calls++;
            return request.RequestUri!.AbsolutePath.EndsWith("hqdefault.jpg") ? Image() : new(HttpStatusCode.NotFound);
        });
        var variants = Enumerable.Range(0, 8).Select(i => $"https://i.ytimg.com/variant-{i}.jpg");
        var cover = await Service(http).GetAsync("jNQXAC9IVRw", variants);
        Check(cover is not null && calls == 4, "Standard fallback URLs were excluded by the search variants.");
    }

    public static async Task InvalidImageAsync()
    {
        using var http = Client(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>blocked</html>", System.Text.Encoding.UTF8, "image/jpeg") });
        Check(await Service(http).GetAsync("jNQXAC9IVRw", Array.Empty<string>()) is null, "An HTML response was saved as an image.");
    }

    public static async Task OversizedAsync()
    {
        using var http = Client(_ =>
        {
            var response = Image();
            response.Content.Headers.ContentLength = 5 * 1024 * 1024;
            return response;
        });
        Check(await Service(http).GetAsync("jNQXAC9IVRw", Array.Empty<string>()) is null, "Oversized artwork was accepted.");
    }

    public static async Task CacheAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var http = Client(_ => throw new Exception("Cached artwork should not require the network."));
        var service = Service(http);
        var library = new LibraryService(service);
        var path = Path.Combine(files.Root, "track.m4a");
        await File.WriteAllTextAsync(path, "Audio with no embedded artwork");
        var song = new Song { Title = "A track", FilePath = path };
        await library.CacheArtworkAsync(song, new CoverArt(Png, "image/png"));
        await MainThread.InvokeOnMainThreadAsync(() => library.AddSongAsync(song));
        var reloaded = new LibraryService(service);
        await reloaded.LoadLibraryAsync();
        await reloaded.RefreshArtworkAsync();
        Check(reloaded.Songs.Single().ThumbnailDataUrl.Length == 0 && reloaded.Songs.Single().ThumbnailPath == song.ThumbnailPath && File.Exists(song.ThumbnailPath), "Artwork was lost when the library reloaded.");
    }

    public static async Task RepairAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var http = Client(_ => Image());
        var path = Path.Combine(files.Root, $"old-track-jNQXAC9IVRw-{Guid.NewGuid():N}.m4a");
        await File.WriteAllTextAsync(path, "Audio without tags");
        await File.WriteAllTextAsync(Path.Combine(files.Root, "songs_cache.json"), JsonSerializer.Serialize(new[] { new Song { Title = "Old track", FilePath = path } }));
        var library = new LibraryService(Service(http));
        await library.LoadLibraryAsync();
        await library.RefreshArtworkAsync();
        var song = library.Songs.Single();
        Check(song.VideoId == "jNQXAC9IVRw" && song.ThumbnailDataUrl.Length == 0 && File.Exists(song.ThumbnailPath), "An older download's artwork was not repaired.");
        var reloaded = new LibraryService(Service(http));
        await reloaded.LoadLibraryAsync();
        await reloaded.RefreshArtworkAsync();
        Check(reloaded.Songs.Single().ThumbnailPath == song.ThumbnailPath, "Repaired artwork was not persisted in SQLite.");
    }

    private static CoverArtService Service(HttpClient http) => new(http, NullLogger<CoverArtService>.Instance);
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> response) => new(new Handler(response));
    private static HttpResponseMessage Image(string contentType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
