using System.Text.Json.Serialization;

namespace MusicPlayer.Models;

public class Song
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string ThumbnailDataUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string ArtworkSource => !string.IsNullOrWhiteSpace(ThumbnailDataUrl) ? ThumbnailDataUrl : ThumbnailUrl;
}
