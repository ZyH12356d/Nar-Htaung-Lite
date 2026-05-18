namespace MusicPlayer.Services
{
    public interface IMediaNotificationService
    {
        void UpdateMetadata(string title, string author, string thumbnailUrl);
        void UpdatePlaybackStatus(bool isPlaying);
    }
}
