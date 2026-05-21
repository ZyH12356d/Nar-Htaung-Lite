using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace MusicPlayer.Platforms.Android
{
    [Service(Enabled = true, Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
    public class PlaybackService : Service
    {
        private const int NotificationId = 1001;
        private const string ChannelId = "music_player_channel";

        public static Notification? CurrentNotification { get; set; }

        public override IBinder? OnBind(Intent intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            var notification = CurrentNotification ?? CreateFallbackNotification();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            return StartCommandResult.Sticky;
        }

        private Notification CreateFallbackNotification()
        {
            var context = global::Microsoft.Maui.ApplicationModel.Platform.AppContext;
            
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(ChannelId, "Music Player", NotificationImportance.Low);
                var manager = (NotificationManager)context.GetSystemService(Context.NotificationService);
                manager?.CreateNotificationChannel(channel);
            }

            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetContentTitle("Music Player")
                .SetContentText("Playing audio")
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetOngoing(true)
                .SetVisibility((int)NotificationVisibility.Public);
                
            return builder.Build();
        }
    }
}
