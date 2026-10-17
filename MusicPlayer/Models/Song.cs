using System.Text.Json.Serialization;

namespace MusicPlayer.Models;

public class Song
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    // Keep the original source identity independent of local filenames and collection membership.
    public string VideoId { get; set; } = string.Empty;
    public long AddedAtUtcTicks { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public long ThumbnailRevision { get; set; }

    // Read only for migration of older JSON libraries; never stored in SQLite.
    public string ThumbnailDataUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string ArtworkSource => !string.IsNullOrWhiteSpace(ThumbnailPath)
        ? "artwork/" + Path.GetFileName(ThumbnailPath) + "?v=" + ThumbnailRevision
        : !string.IsNullOrWhiteSpace(ThumbnailDataUrl) ? ThumbnailDataUrl : ThumbnailUrl;

    [JsonIgnore]
    public string NotificationArtwork => !string.IsNullOrWhiteSpace(ThumbnailPath) ? ThumbnailPath : ThumbnailDataUrl;
}
