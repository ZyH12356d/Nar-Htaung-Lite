using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace MusicPlayer.Platforms.Android;

[Service(Enabled = true, Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public class PlaybackService : Service
{
    private static PlaybackService? _instance;
    private static Notification? _notification;
    private static bool _isPlaying;
    private const int NotificationId = MediaNotificationService.NotificationId;

    public static void Publish(Notification notification, bool isPlaying)
    {
        _notification = notification;
        _isPlaying = isPlaying;
        var context = global::Microsoft.Maui.ApplicationModel.Platform.AppContext;
        if (_instance is not null)
            _instance.UpdateNotification(notification, isPlaying);
        else
        {
            (context.GetSystemService(Context.NotificationService) as NotificationManager)?.Notify(NotificationId, notification);
            if (!isPlaying) return;
            var intent = new Intent(context, typeof(PlaybackService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
            else context.StartService(intent);
        }
    }

    private void Promote(Notification notification)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeMediaPlayback);
        else StartForeground(NotificationId, notification);
    }

    private void UpdateNotification(Notification notification, bool isPlaying)
    {
        if (isPlaying) Promote(notification);
        else
        {
            // Keep the paused notification available for resuming without keeping a foreground service.
            StopForeground(StopForegroundFlags.Detach);
            (GetSystemService(Context.NotificationService) as NotificationManager)?.Notify(NotificationId, notification);
        }
    }

    public override IBinder? OnBind(Intent? intent) => null;
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        _instance = this;
        // A rapid pause/clear can arrive before Android finishes starting this service.
        var notification = _notification ?? CreateFallbackNotification();
        Promote(notification);
        if (_notification is null)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf(startId);
        }
        else if (!_isPlaying) UpdateNotification(notification, false);
        return StartCommandResult.NotSticky;
    }

    public static void ClearNotification()
    {
        _notification = null; _isPlaying = false;
        var context = global::Microsoft.Maui.ApplicationModel.Platform.AppContext;
        if (_instance is { } service)
        {
            service.StopForeground(StopForegroundFlags.Remove);
            service.StopSelf();
        }
        (context.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(NotificationId);
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_instance, this)) _instance = null;
        base.OnDestroy();
    }

    private Notification CreateFallbackNotification()
    {
        MediaNotificationService.EnsureChannel(this);
        using var builder = new NotificationCompat.Builder(this, MediaNotificationService.ChannelId);
        builder.SetContentTitle("Nar Htaung Lite");
        builder.SetContentText("Preparing playback");
        builder.SetSmallIcon(Resource.Drawable.ic_stat_music);
        builder.SetOnlyAlertOnce(true);
        builder.SetOngoing(true);
        return builder.Build() ?? throw new InvalidOperationException("Could not create the playback service notification.");
    }
}
