using Microsoft.Extensions.Logging;

namespace MusicPlayer
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(_ => new HttpClient());
            // Let YoutubeExplode manage the headers required by its supported clients.
            builder.Services.AddSingleton(_ => new YoutubeExplode.YoutubeClient());
            builder.Services.AddSingleton<MusicPlayer.Services.PlayerService>();
            builder.Services.AddSingleton<MusicPlayer.Services.DownloadService>();
            builder.Services.AddSingleton<MusicPlayer.Services.CoverArtService>();
            builder.Services.AddSingleton<MusicPlayer.Services.NavigationService>();
            builder.Services.AddSingleton<MusicPlayer.Services.LibraryService>();
            
#if ANDROID
            builder.Services.AddSingleton<MusicPlayer.Services.IMediaNotificationService, MusicPlayer.Platforms.Android.MediaNotificationService>();
#else
            builder.Services.AddSingleton<MusicPlayer.Services.IMediaNotificationService, MusicPlayer.Services.StubMediaNotificationService>();
#endif

            builder.Services.AddSingleton<App>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton(Plugin.Maui.Audio.AudioManager.Current);

            return builder.Build();
        }
    }
}
