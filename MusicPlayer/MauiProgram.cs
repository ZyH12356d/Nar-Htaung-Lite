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
            builder.Services.AddSingleton(sp => 
            {
                var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.6367.113 Mobile Safari/537.36");
                return client;
            });
            builder.Services.AddSingleton(sp => 
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                return new YoutubeExplode.YoutubeClient(httpClient);
            });
            builder.Services.AddSingleton<MusicPlayer.Services.PlayerService>();
            builder.Services.AddSingleton<MusicPlayer.Services.DownloadService>();
            builder.Services.AddSingleton(Plugin.Maui.Audio.AudioManager.Current);

            return builder.Build();
        }
    }
}
