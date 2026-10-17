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
            var context = Platform.AppContext;
            using var artwork = DecodeArtwork(thumbnailUrl);

            using var metadataBuilder = new MediaMetadata.Builder();
            metadataBuilder.PutString(MediaMetadata.MetadataKeyTitle, title);
            metadataBuilder.PutString(MediaMetadata.MetadataKeyArtist, author);
            metadataBuilder.PutLong(MediaMetadata.MetadataKeyDuration, (long)(durationSeconds * 1000));

            if (artwork is not null)
            {
                metadataBuilder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, artwork);
                metadataBuilder.PutBitmap(MediaMetadata.MetadataKeyArt, artwork);
            }
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

            if (artwork is not null)
                notificationBuilder.SetLargeIcon(artwork);
            var notification = notificationBuilder.Build();
            PlaybackService.CurrentNotification = notification;
            _notificationManager.Notify(NotificationId, notification);
        }

        private static global::Android.Graphics.Bitmap? DecodeArtwork(string source)
        {
            if (string.IsNullOrEmpty(source) || !source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                var separator = source.IndexOf(',');
                if (separator < 0) return null;
                var bytes = Convert.FromBase64String(source[(separator + 1)..]);
                using var image = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (image is null) return null;
                var scale = Math.Min(1, 256.0 / Math.Max(image.Width, image.Height));
                // Bound bitmap size before putting it in the Android notification parcel.
                using var scaled = global::Android.Graphics.Bitmap.CreateScaledBitmap(image,
                    Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), true);
                return scaled?.Copy(global::Android.Graphics.Bitmap.Config.Argb8888!, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not decode notification artwork: {ex.Message}");
                return null;
            }
        }

        public void UpdatePlaybackStatus(bool isPlaying, double positionSeconds)
        {
            var state = isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
            var speed = isPlaying ? 1.0f : 0.0f;

            _mediaSession.SetPlaybackState(new PlaybackState.Builder()
                .SetState(state, (long)(positionSeconds * 1000), speed)
                .Build());

            var context = Platform.AppContext;
            var intent = new Intent(context, typeof(PlaybackService));

            if (isPlaying)
            {
                if (PlaybackService.CurrentNotification != null)
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    {
                        context.StartForegroundService(intent);
                    }
                    else
                    {
                        context.StartService(intent);
                    }
                }
            }
            else
            {
                context.StopService(intent);
            }
        }
    }
}
