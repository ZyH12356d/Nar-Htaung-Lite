using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/video-id", (string url) =>
{
    var videoId = VideoId.Parse(url);
    return Results.Ok(new { VideoId = videoId.Value });
});


app.MapGet("/search", async (string q) =>
{
    var youtube = new YoutubeClient();

    var results = new List<object>();

    await foreach (var video in youtube.Search.GetVideosAsync(q))
    {
        results.Add(new
        {
            id = video.Id.Value,
            title = video.Title,
            author = video.Author.ChannelTitle,
            duration = video.Duration,
            thumbnail = video.Thumbnails.GetWithHighestResolution().Url,

            // Your backend streaming endpoint
            streamUrl = $"/audio?id={video.Id}"
        });

        if (results.Count >= 10)
            break;
    }

    return Results.Ok(results);
});

//app.MapGet("/audio", async (string url) =>
//{
//    var youtube = new YoutubeClient();


//    var video = await youtube.Videos.GetAsync(url);
//    var manifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

//    var audioStreamInfo = manifest
//        .GetAudioOnlyStreams()
//        .GetWithHighestBitrate();

//    var stream = await youtube.Videos.Streams.GetAsync(audioStreamInfo);

//    return Results.Stream(
//        stream,
//        contentType: "audio/webm",
//        fileDownloadName: $"{video.Id}.webm"
//    );
//});
app.MapGet("/audio", async (string id) =>
{
    try
    {
        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(id);

        var tempFilePath = Path.GetTempFileName() + ".mp3";

        // Download and convert to MP3 using FFmpeg
        await youtube.Videos.DownloadAsync(id, tempFilePath, builder => builder.SetContainer("mp3").SetPreset(YoutubeExplode.Converter.ConversionPreset.UltraFast));

        // Download the thumbnail for Cover Art
        byte[]? thumbBytes = null;
        var thumb = video.Thumbnails.GetWithHighestResolution();
        if (thumb != null)
        {
            using var httpClient = new HttpClient();
            thumbBytes = await httpClient.GetByteArrayAsync(thumb.Url);
        }

        // Embed ID3 Tags using TagLibSharp
        using (var tfile = TagLib.File.Create(tempFilePath))
        {
            tfile.Tag.Title = video.Title;
            tfile.Tag.Performers = new[] { video.Author.ChannelTitle };

            if (thumbBytes != null)
            {
                var picture = new TagLib.Picture(new TagLib.ByteVector(thumbBytes))
                {
                    Type = TagLib.PictureType.FrontCover,
                    Description = "Cover",
                    MimeType = "image/jpeg"
                };
                tfile.Tag.Pictures = new TagLib.IPicture[] { picture };
            }

            tfile.Save();
        }

        // Return the modified file and delete it after sending
        var fs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        
        // Clean up title for file download name
        var safeTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
        
        return Results.File(fs, contentType: "audio/mpeg", fileDownloadName: $"{safeTitle}.mp3");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// Download FFmpeg locally if not found
if (!System.IO.File.Exists("ffmpeg.exe")) 
{
    Console.WriteLine("Downloading FFmpeg...");
    await Xabe.FFmpeg.Downloader.FFmpegDownloader.GetLatestVersion(Xabe.FFmpeg.Downloader.FFmpegVersion.Official);
    Console.WriteLine("FFmpeg downloaded locally.");
}

app.Run();

