using Android.App;
using Android.Content;
using Android.OS;
using Android.Media;
using Android.Media.Session;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Services;

namespace MusicPlayer.Platforms.Android;

public sealed class MediaNotificationService : IMediaNotificationService, IDisposable
{
    internal const string ChannelId = "music_player_channel";
    internal const int NotificationId = 1001;
    private readonly Context _context = Platform.AppContext;
    private readonly IServiceProvider _services;
    private readonly MediaSession _mediaSession;
    private readonly SessionCallback _callback;
    private readonly PendingIntent _openApp;
    private global::Android.Graphics.Bitmap? _artwork;
    private string _title = "", _author = "";
    private bool _hasTrack, _isPlaying, _disposed;

    public MediaNotificationService(IServiceProvider services)
    {
        _services = services;
        EnsureChannel(_context);
        _openApp = PendingIntent.GetActivity(_context, 0,
            new Intent(_context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        _callback = new SessionCallback(Dispatch);
        _mediaSession = new MediaSession(_context, "NarHtaungLiteMediaSession");
        _mediaSession.SetCallback(_callback, new Handler(Looper.MainLooper!));
        _mediaSession.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
        _mediaSession.SetSessionActivity(_openApp);
    }

    internal static void EnsureChannel(Context context)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            using var channel = new NotificationChannel(ChannelId, "Music Player", NotificationImportance.Low);
            (context.GetSystemService(Context.NotificationService) as NotificationManager)?.CreateNotificationChannel(channel);
        }
    }

    public void UpdateMetadata(string title, string author, string thumbnailUrl, double durationSeconds)
    {
        if (_disposed) return;
        _title = title; _author = author; _hasTrack = true;
        _artwork?.Dispose();
        _artwork = DecodeArtwork(thumbnailUrl);
        using var builder = new MediaMetadata.Builder();
        builder.PutString(MediaMetadata.MetadataKeyTitle, title);
        builder.PutString(MediaMetadata.MetadataKeyArtist, author);
        builder.PutLong(MediaMetadata.MetadataKeyDuration, (long)(durationSeconds * 1000));
        if (_artwork is not null)
        {
            builder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _artwork);
            builder.PutBitmap(MediaMetadata.MetadataKeyArt, _artwork);
        }
        _mediaSession.SetMetadata(builder.Build());
    }

    public void UpdatePlaybackStatus(bool isPlaying, double positionSeconds)
    {
        if (_disposed || !_hasTrack) return;
        _isPlaying = isPlaying;
        _mediaSession.Active = true;
        using var state = new PlaybackState.Builder();
        state.SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause |
            PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSkipToNext);
        state.SetState(isPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused,
            (long)(Math.Max(0, positionSeconds) * 1000), isPlaying ? 1f : 0f, SystemClock.ElapsedRealtime());
        _mediaSession.SetPlaybackState(state.Build());
        PublishNotification();
    }

    private void PublishNotification()
    {
        using var style = new AndroidX.Media.App.NotificationCompat.MediaStyle();
        style.SetMediaSession(global::Android.Support.V4.Media.Session.MediaSessionCompat.Token.FromToken(_mediaSession.SessionToken));
        style.SetShowActionsInCompactView(0, 1, 2);
        using var builder = new NotificationCompat.Builder(_context, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_stat_music);
        builder.SetContentTitle(_title);
        builder.SetContentText(_author);
        builder.SetVisibility((int)NotificationVisibility.Public);
        builder.SetOnlyAlertOnce(true);
        builder.SetShowWhen(false);
        builder.SetOngoing(_isPlaying);
        builder.SetContentIntent(_openApp);
        builder.AddAction(global::Android.Resource.Drawable.IcMediaPrevious, "Previous", ActionIntent(MediaPlaybackActions.Previous, 1));
        builder.AddAction(_isPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay,
            _isPlaying ? "Pause" : "Play", ActionIntent(_isPlaying ? MediaPlaybackActions.Pause : MediaPlaybackActions.Play, 2));
        builder.AddAction(global::Android.Resource.Drawable.IcMediaNext, "Next", ActionIntent(MediaPlaybackActions.Next, 3));
        builder.SetStyle(style);
        if (_artwork is not null) builder.SetLargeIcon(_artwork);
        PlaybackService.Publish(builder.Build() ?? throw new InvalidOperationException("Could not create the media notification."), _isPlaying);
    }

    private PendingIntent ActionIntent(string action, int requestCode) => PendingIntent.GetBroadcast(_context,
        requestCode, new Intent(_context, typeof(MediaActionReceiver)).SetAction(action),
        PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    private void Dispatch(string action) => _ = DispatchAsync(action);
    private async Task DispatchAsync(string action)
    {
        try
        {
            // Resolve on user action, after PlayerService construction, to avoid a DI cycle.
            await _services.GetRequiredService<PlayerService>().HandleMediaActionAsync(action);
        }
        catch (Exception ex) { Console.WriteLine($"Could not handle media control: {ex.Message}"); }
    }

    public void Clear()
    {
        if (_disposed) return;
        _hasTrack = _isPlaying = false;
        using var state = new PlaybackState.Builder();
        state.SetActions(0);
        state.SetState(PlaybackStateCode.Stopped, 0, 0);
        _mediaSession.SetPlaybackState(state.Build());
        _mediaSession.Active = false;
        _mediaSession.SetMetadata(null);
        _artwork?.Dispose(); _artwork = null;
        PlaybackService.ClearNotification();
    }

    private static global::Android.Graphics.Bitmap? DecodeArtwork(string source)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        try
        {
            byte[] bytes;
            if (ArtworkFiles.IsManagedPath(FileSystem.AppDataDirectory, source))
                bytes = File.ReadAllBytes(source);
            else if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && source.IndexOf(',') is var separator && separator >= 0)
                bytes = Convert.FromBase64String(source[(separator + 1)..]);
            else return null;
            using var image = global::Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (image is null) return null;
            var scale = Math.Min(1, 256.0 / Math.Max(image.Width, image.Height));
            using var scaled = global::Android.Graphics.Bitmap.CreateScaledBitmap(image,
                Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), true);
            return scaled?.Copy(global::Android.Graphics.Bitmap.Config.Argb8888!, false);
        }
        catch (Exception ex) { Console.WriteLine($"Could not decode notification artwork: {ex.Message}"); return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        _mediaSession.Release();
        _mediaSession.Dispose();
        _callback.Dispose();
        _openApp.Dispose();
    }

    private sealed class SessionCallback(Action<string> dispatch) : MediaSession.Callback
    {
        public override void OnPlay() => dispatch(MediaPlaybackActions.Play);
        public override void OnPause() => dispatch(MediaPlaybackActions.Pause);
        public override void OnSkipToPrevious() => dispatch(MediaPlaybackActions.Previous);
        public override void OnSkipToNext() => dispatch(MediaPlaybackActions.Next);
        public override void OnStop() => dispatch(MediaPlaybackActions.Pause);
    }
}
