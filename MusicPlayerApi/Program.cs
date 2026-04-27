using YoutubeExplode;
using YoutubeExplode.Common;
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

        // 1. Get the manifest
        var manifest = await youtube.Videos.Streams.GetManifestAsync(id);

        // 2. Select the highest bitrate audio stream
        var audioStreamInfo = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();

        if (audioStreamInfo == null)
            return Results.NotFound("No compatible audio stream found.");

        // 3. Get the actual stream from YouTube
        var stream = await youtube.Videos.Streams.GetAsync(audioStreamInfo);

        // 4. Return the stream directly to the client
        return Results.Stream(
            stream,
            contentType: $"audio/{audioStreamInfo.Container.Name}",
            fileDownloadName: $"{id}.{audioStreamInfo.Container.Name}"
        );
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

