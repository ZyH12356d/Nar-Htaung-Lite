namespace MusicPlayer.Services
{
    public interface IMediaNotificationService
    {
        void UpdateMetadata(string title, string author, string thumbnailUrl, double durationSeconds);
        void UpdatePlaybackStatus(bool isPlaying, double positionSeconds);
    }
}
