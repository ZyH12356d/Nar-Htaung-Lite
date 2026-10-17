# Local library storage

The app stores its catalog in `FileSystem.AppDataDirectory/library.db` using Microsoft.Data.Sqlite.
Database reads, writes, tag scans, image decoding, and migration run on workers; UI state is
published on the main thread after successful writes.

## Records and source identity

- `Songs`: stable ID, title, author, unique local file path, YouTube video ID, added time,
  remote cover URL, cached cover path and revision.
- `Collections`: stable ID, name and normalized unique name.
- `CollectionSongs`: collection/song pairs with foreign keys and cascade cleanup.
- `Metadata`: completed migration markers. `PRAGMA user_version` versions the schema.

All downloads is the full Songs catalog. A custom collection references those same songs;
membership does not copy audio. VideoId is indexed and stored separately from local paths.
Older downloads recover IDs from the existing `title-videoId-guid` filename when possible.
Unknown source IDs remain empty rather than being guessed.

This supports future sharing of source IDs. No cloud, collection publishing, import, or streaming
feature is introduced here.

## First launch after upgrading

The importer reads `songs_cache.json` one record at a time, writes covers to files,
and commits metadata and its completion marker in a single transaction. A failed transaction
leaves no partial song catalog and is retried on the next launch. Existing order and IDs
are retained, and missing audio files are excluded. A malformed songs cache falls back
to scanning audio tags. The old JSON files remain untouched as recovery copies.

Collections are imported in a separate transaction after songs. Damaged collection JSON
leaves downloaded songs usable and collection editing disabled until the saved data is repaired.
After successful migration the old JSON is not read again, so removed songs and memberships
cannot return from a stale backup.

Normal edits change affected rows. They do not serialize or rewrite the complete catalog.
WAL and foreign keys are enabled. The native SQLite bundle is explicitly pinned to avoid
the outdated native dependency inherited from the provider.

## Artwork and lists

Production Song objects contain lightweight metadata and paths, with no retained Base64 covers.
Covers are cached atomically under `artwork/`; Android samples images before decoding and
caps the longest thumbnail dimension at 512 pixels. The WebView serves only hashed cover
filenames through a restricted file provider, never arbitrary files from app data.
Revisioned URLs refresh a cover that was previously missing.

Home tracks, collection details, the song picker, and the player queue use Blazor Virtualize.
The catalog is read in batches of 256 lightweight records; the metadata snapshot and indexed
collection membership remain in memory for playback and filtering. Image bytes load only for
requested covers. The Home collection shelf displays the first 12 collections, with See all
leading to the full collection page.

Artwork notifications refresh visible covers and current notification metadata. Queue changes
are emitted separately for song or membership edits. Skipping uses an index instead of scanning
or copying the queue, and queue rebuilding does not check every file on the UI thread.

## Deleting tracks

Audio is renamed to a temporary deletion path before its database row is removed.
If the database write fails, the original file is restored and UI state is retained.
Startup also restores a staged file if an interruption occurred before the row was deleted.
The database cascades the song's collection memberships.

## Verification

Run `dotnet run --project tests/DownloadLogic.Tests` for real SQLite migration, restart,
rollback, concurrent edits, source-ID persistence, offline cover serving, queue and notification
regressions. Synthetic 100, 500 and 1,000-song fixtures report desktop migration/reload times;
these are not phone performance measurements.

Run the UI preview and open `/preview/library/1000` for virtualized list checks. Verify the
last track, filtering, large collection navigation, song selection, and the full player queue.

On a physical Android device, update the existing installation without clearing app data.
Check the first migration, offline restart, covers, collection membership, new downloads,
screen-locked playback and notification controls. Native image resizing and device memory
behavior require this device check.
