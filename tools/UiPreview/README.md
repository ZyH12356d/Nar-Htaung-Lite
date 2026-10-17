# UI preview

This development-only host compiles the app's actual Razor screens and stylesheet.
It uses local artwork and fixture services; it never downloads music or contacts YouTube.
Nothing in this project is packaged in the Android app.

From the repository root:

~~~powershell
dotnet run --project tools/UiPreview --urls http://localhost:5180
~~~

Open the home, search, collection, downloads, and player screens. Playback controls simulate
state changes without audio. The /preview/empty route clears only the preview's fixture library.
Use a fresh browser session to restore fixtures. Check widths of 320, 390, 768 and 1280 pixels,
both themes, long Burmese and English titles, player expansion/collapse, seeking, queue selection,
download filters, and delete confirmation.
The collection fixtures include a Late night collection. Check creating a collection with
existing songs, filtering and adding more songs, removing membership, an empty collection,
the optional download destination dialog, and Previous in the mini player.
Collection changes in this preview are held in memory; the offline regression checks exercise
the production collection persistence code.

The /preview/search-results route renders fixture search results, and /preview/review
shows three phone screens together for visual review.
