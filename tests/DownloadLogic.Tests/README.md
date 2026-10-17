# Download regression checks

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project tests/DownloadLogic.Tests
```

These checks exercise the production retry, file handling, and download queue code with
simulated network, disk, and tagging failures. The threading check uses the real YoutubeClient
with an in-memory HTTP handler and a dedicated simulated UI thread. It checks that synchronous
network work leaves the UI responsive, status updates return to the UI thread, and repeated
clicks cannot start overlapping downloads. No phone or live YouTube requests are required.

Artwork checks cover missing high-resolution images, incorrect HTTP content types,
HTML error responses, oversized files, cover cache persistence, and recovery of older downloads.

Collection checks cover existing downloads, saved song selections after restarting, adding songs
to multiple collections, concurrent additions, removing membership without deleting audio,
cleanup when a song is deleted, invalid names, failed writes, and damaged saved data.
The queue check also verifies that a failed download keeps its collection selection for retry.

On Android, also check a normal download, unavailable content, two queued songs,
and an interrupted connection. A successful download should enter the library;
an unavailable video should show the original failure reason without claiming bot detection.
Create a collection, download a song into it, and reopen the app to verify its membership.
Every downloaded song should also appear in the default All downloads collection.
