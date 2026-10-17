using MusicPlayer.Services;
using YoutubeExplode.Common;
using YoutubeExplode.Search;

namespace MusicPlayer.Components.Pages.Search;

public partial class Search
{
    public void ShowPreviewResults()
    {
        searchQuery = "Late night sessions";
        _hasSearched = true;
        var author = new Author("UC_x5XG1OV2P6uZZ5FSM9Ttw", "The Quiet Hours");
        var ids = new[] { "jNQXAC9IVRw", "aqz-KE-bpKQ", "dQw4w9WgXcQ", "9bZkp7q19f0", "M7lc1UVf-VE" };
        results = Fixtures.Songs().Take(5).Select((song, i) => new VideoSearchResult(ids[i],
            song.Title, new Author(author.ChannelId, song.Author), TimeSpan.FromSeconds(220 + i * 29),
            new[] { new Thumbnail(song.ArtworkSource, new Resolution(500, 500)) })).ToList();
        StateHasChanged();
    }
}
