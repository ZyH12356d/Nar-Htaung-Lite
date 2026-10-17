using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

public sealed record CoverArt(byte[] Bytes, string MimeType)
{
    public string DataUrl => $"data:{MimeType};base64,{Convert.ToBase64String(Bytes)}";
}

public class CoverArtService(HttpClient httpClient, ILogger<CoverArtService> logger)
{
    private const int MaximumBytes = 4 * 1024 * 1024;

    // Some Android HTTP handlers do synchronous setup before their first await.
    public Task<CoverArt?> GetAsync(string videoId, IEnumerable<string> thumbnailUrls) =>
        Task.Run(() => GetCoreAsync(videoId, thumbnailUrls.ToArray()));

    private async Task<CoverArt?> GetCoreAsync(string videoId, IEnumerable<string> thumbnailUrls)
    {
        // Reserve room for standard image fallbacks even when search returns many variants.
        var candidates = thumbnailUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().Take(3).ToList();
        if (IsVideoId(videoId))
        {
            candidates.Add($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
            candidates.Add($"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg");
        }

        foreach (var candidate in candidates.Distinct().Take(5))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                continue;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogDebug("Artwork returned HTTP {StatusCode} for {Url}.", (int)response.StatusCode, uri);
                    continue;
                }
                if (response.Content.Headers.ContentLength > MaximumBytes)
                    continue;

                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream();
                var buffer = new byte[16384];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    if (output.Length + read > MaximumBytes)
                        break;
                    output.Write(buffer, 0, read);
                }
                if (read != 0)
                    continue;

                // CDN responses can omit Content-Type or label an image octet-stream.
                // Verify the actual file signature instead of discarding valid images.
                if (FromBytes(output.ToArray()) is { } cover)
                    return cover;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                logger.LogDebug(ex, "Could not fetch artwork from {Url}; trying another image.", uri);
            }
        }

        logger.LogWarning("No usable cover image was available for {VideoId}.", videoId);
        return null;
    }

    public static CoverArt? FromBytes(byte[] bytes)
    {
        string? mime = null;
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            mime = "image/jpeg";
        else if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            mime = "image/png";
        else if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
            mime = "image/webp";
        else if (bytes.Length >= 6 && (Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a"))
            mime = "image/gif";
        return mime is null || bytes.Length > MaximumBytes ? null : new CoverArt(bytes, mime);
    }

    public static bool IsVideoId(string? id) =>
        id?.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
