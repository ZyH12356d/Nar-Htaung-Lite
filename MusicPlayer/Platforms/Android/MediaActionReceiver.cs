using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Services;

namespace MusicPlayer.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public class MediaActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var action = intent?.Action;
        if (!MediaPlaybackActions.IsSupported(action)) return;
        var pending = GoAsync();
        _ = ExecuteAsync(action!, pending);
    }

    private static async Task ExecuteAsync(string action, PendingResult? pending)
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            if (services is not null)
                await services.GetRequiredService<PlayerService>().HandleMediaActionAsync(action);
        }
        catch (Exception ex) { Console.WriteLine($"Could not handle notification action: {ex.Message}"); }
        finally { pending?.Finish(); }
    }
}
