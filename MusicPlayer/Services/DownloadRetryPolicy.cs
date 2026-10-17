using System.Net;

namespace MusicPlayer.Services;

internal static class DownloadRetryPolicy
{
    internal const int MaxAttempts = 3;

    public static async Task ExecuteAsync(
        Func<Task> operation,
        Action<int, TimeSpan, Exception> onRetry,
        Func<TimeSpan, Task>? delay = null)
    {
        delay ??= duration => Task.Delay(duration);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex))
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                onRetry(attempt + 1, wait, ex);
                await delay(wait);
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        // HttpClient signals its own timeouts with TaskCanceledException.
        TaskCanceledException { InnerException: TimeoutException } => true,
        TimeoutException => true,
        HttpIOException => true,
        HttpRequestException request => request.StatusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout,
        // Access denials, rate limits, unavailable videos and local IO errors
        // need user action; repeating them immediately will not help.
        _ => false
    };
}
