using System.Collections.Concurrent;

// Desktop substitutes for the two platform dependencies used by the production player.
// Android compilation separately checks the actual Plugin.Maui.Audio and MAUI interfaces.
namespace Microsoft.Maui.Storage
{
    internal static class Preferences
    {
        private static readonly ConcurrentDictionary<string, object> Values = new();
        public static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var value) && value is T result ? result : fallback;
        public static void Set<T>(string key, T value) where T : notnull => Values[key] = value;
        public static void Clear() => Values.Clear();
    }
}
namespace Plugin.Maui.Audio
{
    public interface IAudioManager { IAudioPlayer CreatePlayer(Stream stream); }
    public interface IAudioPlayer : IDisposable
    {
        event EventHandler? PlaybackEnded;
        double Duration { get; }
        double CurrentPosition { get; }
        bool IsPlaying { get; }
        void Play();
        void Pause();
        void Stop();
        void Seek(double position);
    }
}
