namespace MusicPlayer.Services
{
    public class StubMediaNotificationService : IMediaNotificationService
    {
        public void UpdateMetadata(string title, string author, string thumbnailUrl, double durationSeconds) { }
        public void UpdatePlaybackStatus(bool isPlaying, double positionSeconds) { }
    }
}
