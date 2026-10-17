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

Playback checks use the production PlayerService with simulated native audio and preferences.
They cover switching collections while playing or paused, next/previous/track-end boundaries,
queue updates after membership edits or downloads, restoring a collection after restarting,
ignoring stale song preferences, shuffle, repeat, and empty collections. Home initializes the
player without replacing its queue; choosing a Home track or Home shuffle selects All downloads.

Notification command checks cover Previous/Next in the active collection, repeated explicit
Play/Pause requests, metadata after skips or paused collection changes, clearing an empty queue,
and resuming the saved collection/position without opening a page.

On an Android device, expand the notification and check Previous, Pause/Play, and Next with
a custom collection and with All downloads. Repeat with the app in the background and the
screen locked. A paused notification should remain available for resuming, and emptying the
active collection should remove its notification.

SQLite checks use the actual native database, not an in-memory database substitute. They cover
one-time migration with legacy recovery copies, YouTube ID preservation and filename recovery,
transaction rollback and restart, blocked writes without blocking the UI, deletion recovery,
cover-serving boundaries, and synthetic catalogs of 100, 500 and 1,000 tracks. Image bytes are
excluded from persisted records and reloaded catalog objects. The tests use a small desktop
image processor substitute; Android builds exercise the native resizing code.

Storage and manual device verification are described in [LibraryStorage.md](../../docs/LibraryStorage.md).
