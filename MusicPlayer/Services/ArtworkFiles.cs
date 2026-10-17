using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MusicPlayer.Models;

namespace MusicPlayer.Services;

public static partial class ArtworkFiles
{
    public static string DirectoryPath(string root) => Path.Combine(root, "artwork");

    public static bool IsManagedPath(string root, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(DirectoryPath(root)), StringComparison.OrdinalIgnoreCase)
                && FileNamePattern().IsMatch(Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static async Task<string> WriteAsync(string root, Song song, CoverArt cover)
    {
        // Decoding and resizing may perform synchronous native work.
        return await Task.Run(() =>
        {
            var thumbnail = CoverThumbnail.Create(cover);
            var directory = DirectoryPath(root);
            Directory.CreateDirectory(directory);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(song.FilePath)));
            var extension = thumbnail.MimeType switch { "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", _ => ".jpg" };
            var path = Path.Combine(directory, key + extension);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, thumbnail.Bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            song.ThumbnailRevision = DateTime.UtcNow.Ticks;
            return path;
        });
    }

    public static async Task PrepareLegacyAsync(string root, Song song)
    {
        if (!CoverArtService.IsVideoId(song.VideoId))
        {
            var match = VideoIdPattern().Match(Path.GetFileNameWithoutExtension(song.FilePath));
            if (match.Success) song.VideoId = match.Groups[1].Value;
        }

        if (IsManagedPath(root, song.ThumbnailPath) && File.Exists(song.ThumbnailPath))
        {
            if (song.ThumbnailRevision > 0) return;
            // Normalize older cached covers once during migration; new covers are already bounded.
            var cached = await Task.Run(() => new FileInfo(song.ThumbnailPath).Length <= 4 * 1024 * 1024
                ? CoverArtService.FromBytes(File.ReadAllBytes(song.ThumbnailPath)) : null);
            if (cached is not null)
            {
                try { song.ThumbnailPath = await WriteAsync(root, song, cached); return; }
                catch (InvalidDataException) { /* Recover from embedded or remote artwork below. */ }
            }
        }

        song.ThumbnailPath = string.Empty;
        if (song.ThumbnailDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var comma = song.ThumbnailDataUrl.IndexOf(',');
                if (comma >= 0 && CoverArtService.FromBytes(Convert.FromBase64String(song.ThumbnailDataUrl[(comma + 1)..])) is { } cover)
                    song.ThumbnailPath = await WriteAsync(root, song, cover);
            }
            catch (FormatException) { /* Invalid old artwork can be repaired separately. */ }
        }
    }

    public static bool IsFileName(string name) => FileNamePattern().IsMatch(name);

    [GeneratedRegex(@"^[A-Fa-f0-9]{64}\.(jpg|png|webp|gif)\z", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"-([A-Za-z0-9_-]{11})-[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();
}
