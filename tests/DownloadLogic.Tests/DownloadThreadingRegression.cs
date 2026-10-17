using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Models;
using MusicPlayer.Services;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Search;

internal static class DownloadThreadingRegression
{
    public static async Task RunAsync()
    {
        using var files = new DownloadCase();
        FileSystem.AppDataDirectory = files.Root;
        using var handler = new ThreadCheckingHandler();
        using var http = new HttpClient(handler);
        using var youtube = new YoutubeClient(http);
        var covers = new CoverArtService(http, NullLogger<CoverArtService>.Instance);
        var service = new DownloadService(youtube, new LibraryService(covers), NullLogger<DownloadService>.Instance, covers);
        var stateUpdatedOffUi = false;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        service.Downloads.CollectionChanged += (_, args) =>
        {
            stateUpdatedOffUi |= !MainThread.IsMainThread;
            if (args.NewItems is null)
                return;
            foreach (DownloadItem item in args.NewItems)
                item.PropertyChanged += (_, _) => stateUpdatedOffUi |= !MainThread.IsMainThread;
        };
        service.DownloadsChanged += () =>
        {
            stateUpdatedOffUi |= !MainThread.IsMainThread;
            if (service.Downloads.Count == 2 && service.Downloads.All(d => d.Status == DownloadStatus.Failed))
                finished.TrySetResult();
        };

        var author = new Author("UC_x5XG1OV2P6uZZ5FSM9Ttw", "Test author");
        var first = new VideoSearchResult("jNQXAC9IVRw", "First", author, null, Array.Empty<Thumbnail>());
        var second = new VideoSearchResult("aqz-KE-bpKQ", "Second", author, null, Array.Empty<Thumbnail>());
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            service.Enqueue(first, new[] { "night" });
            service.Enqueue(first); // Repeated clicks must not start a second copy.
            service.Enqueue(second);
        });

        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Synchronous work in the fake HTTP handler must not block this dispatch.
            await MainThread.InvokeOnMainThreadAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            handler.Release.Set();
        }

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (handler.CalledOnUiThread)
                throw new Exception("YouTube performed network work on the UI thread.");
            if (stateUpdatedOffUi)
                throw new Exception("Download state was updated outside the UI thread.");
            if (handler.MaxConcurrentRequests != 1 || service.Downloads.Count != 2)
                throw new Exception("The download queue allowed overlapping or duplicate work.");
            if (service.Downloads.Any(d => !d.ErrorMessage.Contains("Simulated unavailable video")))
                throw new Exception("The actual extraction error was not retained.");
            if (!service.Downloads.Single(d => d.VideoId == first.Id.Value).CollectionIds.SequenceEqual(new[] { "night" }))
                throw new Exception("A queued download lost its selected collection after failure.");
        });
        files.AssertClean();
    }

    private sealed class ThreadCheckingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public bool CalledOnUiThread { get; private set; }
        public int MaxConcurrentRequests { get; private set; }
        private int _activeRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "i.ytimg.com")
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            CalledOnUiThread |= MainThread.IsMainThread;
            var active = Interlocked.Increment(ref _activeRequests);
            MaxConcurrentRequests = Math.Max(MaxConcurrentRequests, active);
            Started.TrySetResult();
            try
            {
                if (MainThread.IsMainThread)
                    throw new InvalidOperationException("NetworkOnMainThreadException");
                if (!Release.Wait(TimeSpan.FromSeconds(10), cancellationToken))
                    throw new TimeoutException("The UI was blocked by synchronous network work.");
                return Task.FromException<HttpResponseMessage>(new VideoUnavailableException("Simulated unavailable video"));
            }
            finally
            {
                Interlocked.Decrement(ref _activeRequests);
            }
        }
    }
}

// A dedicated UI synchronization context replaces MAUI only in these desktop tests.
// The production DownloadService and YoutubeClient execute unchanged above it.
internal static class MainThread
{
    private static readonly TestUiContext Context = new();
    public static bool IsMainThread => Environment.CurrentManagedThreadId == Context.ThreadId;

    public static void BeginInvokeOnMainThread(Action action)
    {
        if (IsMainThread)
            action();
        else
            Context.Post(_ => action(), null);
    }

    public static Task InvokeOnMainThreadAsync(Action action) => InvokeOnMainThreadAsync(() =>
    {
        action();
        return Task.CompletedTask;
    });

    public static async Task<T> InvokeOnMainThreadAsync<T>(Func<T> action)
    {
        T result = default!;
        await InvokeOnMainThreadAsync(() => { result = action(); });
        return result;
    }

    public static Task InvokeOnMainThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvokeOnMainThread(async () =>
        {
            try { await action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }

    private sealed class TestUiContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _work = new();
        public int ThreadId { get; }

        public TestUiContext()
        {
            var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            new Thread(() =>
            {
                SetSynchronizationContext(this);
                started.SetResult(Environment.CurrentManagedThreadId);
                foreach (var item in _work.GetConsumingEnumerable())
                    item.Callback(item.State);
            }) { IsBackground = true }.Start();
            ThreadId = started.Task.GetAwaiter().GetResult();
        }

        public override void Post(SendOrPostCallback callback, object? state) => _work.Add((callback, state));
    }
}

internal static class FileSystem
{
    public static string AppDataDirectory { get; set; } = string.Empty;
}

internal static class DeviceInfo
{
    public static string Platform => "Android";
}

internal static class DevicePlatform
{
    public const string WinUI = "Windows";
}
