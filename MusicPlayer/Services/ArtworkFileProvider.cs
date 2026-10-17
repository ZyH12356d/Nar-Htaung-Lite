using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace MusicPlayer.Services;

// Expose only managed covers to Blazor, never the database or downloaded audio directory.
public sealed class ArtworkFileProvider(string root) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.TrimStart('/');
        const string prefix = "artwork/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return new NotFoundFileInfo(path);
        var name = path[prefix.Length..];
        return ArtworkFiles.IsFileName(name)
            ? new PhysicalFileInfo(new FileInfo(Path.Combine(ArtworkFiles.DirectoryPath(root), name)))
            : new NotFoundFileInfo(path);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}
