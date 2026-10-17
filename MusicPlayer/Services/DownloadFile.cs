namespace MusicPlayer.Services;

internal static class DownloadFile
{
    public static async Task SaveAsync(
        string destinationPath,
        Func<string, Task> transfer,
        Func<string, Task> writeMetadata,
        Action<int, TimeSpan, Exception> onRetry,
        Action<Exception> onMetadataError,
        Action<Exception> onCleanupError,
        Func<TimeSpan, Task>? retryDelay = null)
    {
        // Keep incomplete files out of the library scan. Preserve the extension
        // so the converter and TagLib can identify the media format.
        var stagingDirectory = Path.Combine(Path.GetDirectoryName(destinationPath)!, ".downloads");
        Directory.CreateDirectory(stagingDirectory);
        var extension = Path.GetExtension(destinationPath);
        var temporaryPath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}{extension}");
        var metadataPath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}{extension}");

        try
        {
            await DownloadRetryPolicy.ExecuteAsync(async () =>
            {
                File.Delete(temporaryPath);
                await transfer(temporaryPath);
                if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                    throw new InvalidDataException("The downloaded audio file is empty.");
            }, onRetry, retryDelay);

            try
            {
                // Tag a copy so a failed tag write cannot corrupt good audio.
                File.Copy(temporaryPath, metadataPath);
                await writeMetadata(metadataPath);
                File.Move(metadataPath, temporaryPath, overwrite: true);
            }
            catch (Exception ex)
            {
                onMetadataError(ex);
            }

            // Never overwrite another song, including an existing download.
            File.Move(temporaryPath, destinationPath);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath, onCleanupError);
            DeleteTemporaryFile(metadataPath, onCleanupError);
        }
    }

    private static void DeleteTemporaryFile(string path, Action<Exception> onError)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
