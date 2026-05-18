using Android.App;
using Android.Content;
using Android.OS;
using Android.Media;
using Android.Media.Session;
using AndroidX.Core.App;
using MusicPlayer.Services;
using MusicPlayer.Models;

namespace MusicPlayer.Platforms.Android
{
    public class MediaNotificationService : IMediaNotificationService
    {
        private const string ChannelId = "music_player_channel";
        private const int NotificationId = 1001;
        private MediaSession _mediaSession;
        private NotificationManager _notificationManager;

        public MediaNotificationService()
        {
            var context = Platform.CurrentActivity;
            _notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Music Player", NotificationImportance.Low);
                _notificationManager.CreateNotificationChannel(channel);
            }

            _mediaSession = new MediaSession(context, "NarHtaungLiteMediaSession");
            _mediaSession.Active = true;
        }

        public void UpdateMetadata(string title, string author, string thumbnailUrl, double durationSeconds)
        {
            var context = Platform.CurrentActivity;

            var metadataBuilder = new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, title)
                .PutString(MediaMetadata.MetadataKeyArtist, author)
                .PutLong(MediaMetadata.MetadataKeyDuration, (long)(durationSeconds * 1000));

            _mediaSession.SetMetadata(metadataBuilder.Build());

            var notificationBuilder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetContentTitle(title)
                .SetContentText(author)
                .SetVisibility((int)NotificationVisibility.Public)
                .SetOngoing(true)
                .SetStyle(new AndroidX.Media.App.NotificationCompat.MediaStyle()
                    .SetMediaSession(global::Android.Support.V4.Media.Session.MediaSessionCompat.Token.FromToken(_mediaSession.SessionToken)))
                .SetContentIntent(PendingIntent.GetActivity(context, 0, new Intent(context, typeof(MainActivity)), PendingIntentFlags.Immutable));

            _notificationManager.Notify(NotificationId, notificationBuilder.Build());
        }

        public void UpdatePlaybackStatus(bool isPlaying, double positionSeconds)
        {
            var state = isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            var speed = isPlaying ? 1.0f : 0.0f;

            _mediaSession.SetPlaybackState(new PlaybackState.Builder()
                .SetState(state, (long)(positionSeconds * 1000), speed)
                .Build());
        }
    }
}
