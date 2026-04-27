namespace MusicPlayer.Models
{
    public class Song
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ThumbnailPath { get; set; } = string.Empty;
        public string ThumbnailDataUrl { get; set; } = string.Empty;
    }
}
