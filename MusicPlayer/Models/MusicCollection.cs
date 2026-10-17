namespace MusicPlayer.Models;

public class MusicCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> SongPaths { get; set; } = new();
}
