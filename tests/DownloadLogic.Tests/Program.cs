using System.Net;
using MusicPlayer.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Successful audio is published only after tagging", SuccessfulDownload),
    ("Failed tagging preserves the original audio", FailedTagging),
    ("Interrupted transfer restarts without partial bytes", InterruptedTransfer),
    ("Failed stream resolution is retried", FailedResolution),
    ("Exhausted retries leave no audio or temporary files", ExhaustedRetries),
    ("Access denial is reported without retry", () => PermanentFailure(new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden))),
    ("Rate limiting is reported without retry", () => PermanentFailure(new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests))),
    ("Unavailable content keeps its original reason", () => PermanentFailure(new InvalidOperationException("Video is private"))),
    ("Disk failures are not retried", () => PermanentFailure(new IOException("Disk full"))),
    ("Cancellation is not retried", () => PermanentFailure(new TaskCanceledException("Cancelled"))),
    ("Empty audio is rejected", EmptyDownload),
    ("An existing song cannot be overwritten", ExistingDestination),
    ("Concurrent saves use independent temporary files", ConcurrentDownloads),
    ("HTTP timeouts can recover", HttpTimeout),
    ("Network work leaves the UI thread while queue updates stay on it", DownloadThreadingRegression.RunAsync),
    ("Artwork falls back after a missing high-resolution image", ArtworkRegression.FallbackAsync),
    ("Many thumbnail variants cannot crowd out standard fallbacks", ArtworkRegression.ManyVariantsAsync),
    ("Artwork is accepted despite an incorrect HTTP content type", ArtworkRegression.ContentTypeAsync),
    ("HTML responses cannot become cover artwork", ArtworkRegression.InvalidImageAsync),
    ("Oversized artwork is rejected", ArtworkRegression.OversizedAsync),
    ("Artwork persists separately from audio tags", ArtworkRegression.CacheAsync),
    ("Older cached tracks recover artwork from their video IDs", ArtworkRegression.RepairAsync),
    ("Collections keep existing songs and persist selected tracks", CollectionRegression.PersistenceAsync),
    ("Downloads and collection edits preserve default and shared membership", CollectionRegression.MembershipAsync),
    ("Invalid names and failed saves do not publish collections", CollectionRegression.ValidationAsync),
    ("Damaged collection data leaves downloads usable and is preserved", CollectionRegression.CorruptFileAsync)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL: {test.Name}: {ex}");
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} download regression checks passed.");
return failed == 0 ? 0 : 1;

static async Task SuccessfulDownload()
{
    using var test = new DownloadCase();
    string? transferPath = null;
    await test.Save(async path =>
    {
        transferPath = path;
        Check(!File.Exists(test.Destination), "Audio was published before transfer completed.");
        Check(Path.GetExtension(path) == ".m4a", "Staging must retain the container extension.");
        await File.WriteAllTextAsync(path, "audio");
    }, async path =>
    {
        Check(path != transferPath, "Tags must be written to a separate copy.");
        Check(!File.Exists(test.Destination), "Audio was published before tagging completed.");
        await File.WriteAllTextAsync(path, "tagged audio");
    });
    Check(await File.ReadAllTextAsync(test.Destination) == "tagged audio", "Missing tagged audio.");
    test.AssertClean();
}

static async Task FailedTagging()
{
    using var test = new DownloadCase();
    await test.Save(path => File.WriteAllTextAsync(path, "original audio"), async path =>
    {
        await File.WriteAllTextAsync(path, "damaged tags");
        throw new IOException("Tag writer failed halfway through.");
    });
    Check(await File.ReadAllTextAsync(test.Destination) == "original audio", "A tag failure damaged the downloaded audio.");
    Check(test.MetadataErrors.Count == 1 && test.Retries.Count == 0, "Tag failure should not retry the download.");
    test.AssertClean();
}

static async Task InterruptedTransfer()
{
    using var test = new DownloadCase();
    var attempts = 0;
    await test.Save(async path =>
    {
        Check(!File.Exists(path), "A previous attempt's bytes were left behind.");
        if (++attempts == 1)
        {
            await File.WriteAllTextAsync(path, "partial");
            throw new HttpRequestException("Server temporarily unavailable", null, HttpStatusCode.ServiceUnavailable);
        }
        await File.WriteAllTextAsync(path, "complete audio");
    });
    Check(attempts == 2 && test.Retries.SequenceEqual(new[] { TimeSpan.FromSeconds(2) }), "Unexpected retry count or delay.");
    Check(await File.ReadAllTextAsync(test.Destination) == "complete audio", "Partial data leaked into the result.");
    test.AssertClean();
}

static async Task FailedResolution()
{
    using var test = new DownloadCase();
    var resolutions = 0;
    await test.Save(path => ++resolutions == 1
        ? Task.FromException(new HttpRequestException("Connection lost while getting streams"))
        : File.WriteAllTextAsync(path, "audio from fresh stream"));
    Check(resolutions == 2 && File.Exists(test.Destination), "Stream resolution was not repeated.");
    test.AssertClean();
}

static async Task ExhaustedRetries()
{
    using var test = new DownloadCase();
    var attempts = 0;
    var error = await Capture(() => test.Save(async path =>
    {
        attempts++;
        await File.WriteAllTextAsync(path, "partial");
        throw new HttpRequestException("Connection reset");
    }));
    Check(error is HttpRequestException && attempts == 3, "Expected three attempts and the original failure.");
    Check(test.Retries.SequenceEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }), "Expected bounded exponential backoff.");
    Check(!File.Exists(test.Destination), "Failed audio entered the library directory.");
    test.AssertClean();
}

static async Task PermanentFailure(Exception failure)
{
    using var test = new DownloadCase();
    var attempts = 0;
    var error = await Capture(() => test.Save(async path =>
    {
        attempts++;
        await File.WriteAllTextAsync(path, "partial");
        throw failure;
    }));
    Check(ReferenceEquals(error, failure), "The original error was replaced or mislabelled.");
    Check(attempts == 1 && test.Retries.Count == 0, "A permanent failure was retried.");
    Check(!File.Exists(test.Destination), "A failed download was published.");
    test.AssertClean();
}

static async Task EmptyDownload()
{
    using var test = new DownloadCase();
    var error = await Capture(() => test.Save(path => File.WriteAllBytesAsync(path, [])));
    Check(error is InvalidDataException && !File.Exists(test.Destination), "Empty audio was accepted.");
    Check(test.Retries.Count == 0, "Empty output should not be retried as a network error.");
    test.AssertClean();
}

static async Task ExistingDestination()
{
    using var test = new DownloadCase();
    await File.WriteAllTextAsync(test.Destination, "existing song");
    var error = await Capture(() => test.Save(path => File.WriteAllTextAsync(path, "new song")));
    Check(error is IOException, "Expected an explicit destination conflict.");
    Check(await File.ReadAllTextAsync(test.Destination) == "existing song", "An existing song was overwritten.");
    test.AssertClean();
}

static async Task ConcurrentDownloads()
{
    using var test = new DownloadCase();
    var paths = new HashSet<string>();
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    async Task Transfer(string path)
    {
        Check(paths.Add(path), "Two downloads share a staging file.");
        if (paths.Count == 2)
            ready.SetResult();
        await ready.Task;
        await File.WriteAllTextAsync(path, "audio");
    }
    await Task.WhenAll(test.Save(Transfer), test.Save(Transfer, destination: Path.Combine(test.Root, "other.m4a")));
    Check(paths.Count == 2 && File.Exists(test.Destination), "Concurrent downloads did not finish independently.");
    test.AssertClean();
}

static async Task HttpTimeout()
{
    using var test = new DownloadCase();
    var attempts = 0;
    await test.Save(path => ++attempts == 1
        ? Task.FromException(new TaskCanceledException("HTTP timeout", new TimeoutException()))
        : File.WriteAllTextAsync(path, "audio"));
    Check(attempts == 2 && File.Exists(test.Destination), "An HTTP timeout did not recover.");
    test.AssertClean();
}

static async Task<Exception?> Capture(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception ex) { return ex; }
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

sealed class DownloadCase : IDisposable
{
    private readonly string _basePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NarHtaungDownloadTests"));
    public string Root { get; }
    public string Destination => Path.Combine(Root, "song.m4a");
    public List<TimeSpan> Retries { get; } = new();
    public List<Exception> MetadataErrors { get; } = new();
    private readonly List<Exception> _cleanupErrors = new();

    public DownloadCase()
    {
        Root = Path.GetFullPath(Path.Combine(_basePath, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    public Task Save(Func<string, Task> transfer, Func<string, Task>? metadata = null, string? destination = null) =>
        DownloadFile.SaveAsync(destination ?? Destination, transfer, metadata ?? (_ => Task.CompletedTask),
            (_, _, _) => { }, MetadataErrors.Add, _cleanupErrors.Add,
            wait => { Retries.Add(wait); return Task.CompletedTask; });

    public void AssertClean()
    {
        if (_cleanupErrors.Count != 0 || Directory.EnumerateFiles(Path.Combine(Root, ".downloads")).Any())
            throw new Exception("Temporary audio files were not cleaned up.");
    }

    public void Dispose()
    {
        // Delete only this run's resolved, dedicated test directory.
        if (!Path.GetFullPath(Root).StartsWith(_basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean a path outside the test directory.");
        Directory.Delete(Root, recursive: true);
    }
}
