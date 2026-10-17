namespace MusicPlayer.Services;

// Both Android media-session callbacks and notification intents use these same commands.
public static class MediaPlaybackActions
{
    public const string Previous = "MusicPlayer.Playback.Previous";
    public const string Next = "MusicPlayer.Playback.Next";
    public const string Play = "MusicPlayer.Playback.Play";
    public const string Pause = "MusicPlayer.Playback.Pause";
    public const string Toggle = "MusicPlayer.Playback.Toggle";

    public static bool IsSupported(string? action) => action is Previous or Next or Play or Pause or Toggle;
}
