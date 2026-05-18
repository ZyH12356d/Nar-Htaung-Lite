namespace MusicPlayer.Services
{
    public class StubMediaNotificationService : IMediaNotificationService
    {
        public void UpdateMetadata(string title, string author, string thumbnailUrl) { }
        public void UpdatePlaybackStatus(bool isPlaying) { }
    }
}
