# PhotoQuickSelector

[日本語 (Japanese)](README.md)

A Windows app for browsing and culling photos at high speed.

Ever come back from a trip with 10,000 photos, or from an air show with 20,000? And yet you still want to go through them with your own eyes. This is a photo viewer and culling tool for people like that.

<!--
Review photos in a local folder with keyboard-centric controls, and cull them with ratings, flags, and color labels. Evaluations never modify the original files — they are stored in a per-folder SQLite database.
-->

![Preview screen](docs/images/screenshot-preview.png)

<!-- Sample screenshot. To replace or add images, edit the files under docs/images/ and this reference. -->

## Features

### Main features
- **Fast display backed by a cache**
  - Image files are prefetched into a memory cache and drawn with DirectX (via Win2D).
  - With the default settings it uses about 5 GB of memory, and in exchange switching between images is fast.
  - I used to cull with Lightroom, but when working through more than 10,000 photos I got fed up with how slow switching images was even after generating 1:1 previews — so I built this...
- **Rating and culling**
  - Rate image files with a star rating (★1–5), an accept/reject flag, and color labels (5 colors).
  - The initial rating is read from Exif. Color labels are designed so that a single file can carry multiple colors.
  - Evaluations are stored in a per-folder `PhotoQuickSelector.sqlite3`; the original files are never modified.
- **Focus-point magnification from Exif analysis**
  - The focus point is read from the Exif data and magnified in the upper-right pane, to help you pick the shots that are actually in focus. (Supports Sony and OM SYSTEM cameras.)
- **Keyboard-centric operation**
  - Rating, navigation, zoom, multi-select, bulk rating, and more from the keyboard. Press `F1` for the list of shortcuts.
- **Copy and move via batch files**
  - Copy or move the photos you selected — along with RAW files of the same name — in one go.
  - A preview of the batch file is shown first, so you can proceed with confidence.
  - Deleting outright is scary, so there is no file deletion feature. Move the files to another folder first, then delete them manually.

### Other details
- **JPEG only**
  - Only JPEG files are handled (a RAW+JPEG workflow is assumed).
  - As noted above, copy/move after culling can also include RAW files of the same name.
- **Sorted by capture time**
  - Milliseconds and time zones are taken into account. Even with burst shooting or a time-zone change during a trip, photos are sorted correctly by capture time (as long as it is recorded in the Exif data).
- **Verified with Sony and OM cameras**
  - So far, it has been verified with photos taken on Sony and OM SYSTEM cameras.
- **No installation**
  - Extract the files from the zip and put them anywhere you like. Run the EXE file to start the app.
  - The .NET runtime and the required libraries are all crammed into a single EXE, so the executable is large — around 300 MB...
- **Japanese / English UI**
  - Follows the OS display language by default; switchable in Settings (applied after a restart).

### About the development
- **Made with Claude Code**
  - I had it rebuild an app I had originally written myself, and the result was better than expected, so I rounded out the features and published it.
  - The only thing written by hand is this README file — not a single line of the rest.
  - It said it would write CLAUDE.md itself too, so I let Claude Code write that as well. If you fork this, watch out for that part and rewrite it to your own taste.
  - Everything below this point in this README was also produced by Claude Code.

<!--
- **Single window with a split layout** — left: folder tree (favorites / recent folders), right: browsing (thumbnail grid ⇄ large preview).
- **Comfortable even with thousands of photos** — parallel metadata loading, prefetch caches for thumbnails and pixel data.
- **Evaluations that never touch your files** — ratings (0–5), accept/reject flags, and color labels (5 colors) are stored in a per-folder `PhotoQuickSelector.sqlite3`.
- **A serious preview** — zoom / pan / loupe (100% inspection) / navigator, with EXIF, AF frame, and composition grid overlays.
- **Filters and exports to finish the cull** — condition filter + file name list copy, move unrated photos to a Reject folder, copy with rename.
- **Fast, keyboard-centric operation** — rating, navigation, zoom, multi-select, and bulk rating via shortcuts (press `F1` for the full list).
- **No installation** — a self-contained EXE bundling the .NET / Windows App SDK runtimes.
- **Japanese / English UI** — follows the OS display language by default; switchable in Settings.
-->

## Requirements

- Windows 10 / 11 (x64)
- The runtimes are bundled, so no prior installation of .NET or the Windows App SDK is required.

## Install / Run

1. Download the latest zip from [Releases](https://github.com/kobat/PhotoQuickSelector-rebuild/releases).
2. Extract the zip anywhere you like (`PhotoQuickSelector.App.exe`, `LICENSE`, and `THIRD-PARTY-NOTICES.txt`).
3. Run `PhotoQuickSelector.App.exe`.

> **Note:** The distribution is unsigned, so Windows SmartScreen may warn you on first launch.
> Choose "More info" → "Run anyway" to start the app.

## Quick start

1. **Open a folder** — select a folder in the left tree and press the "Load" button (double-click expands/collapses tree nodes). Favorites and recent folders load with a single click.
   - Right-click a tree node to add frequently used folders to **Favorites**.
2. **Rate** — select a thumbnail and press `0`–`5` (rating), `6`–`9` / `P` (color labels), or `Ctrl+↑` / `Ctrl+↓` (accept/reject flag).
   - Evaluations are saved automatically to that folder's SQLite database (you are asked once before the file is first created).
3. **Inspect** — double-click a thumbnail to enter the large preview. `←` / `→` to move, `Z` to zoom, wheel or `+` / `-` for stepped zoom.
4. **Filter** — toggle the filter with `Ctrl+L`. Narrow down by rating, flags, and colors.
5. **Export** — copy the filtered file name list, move unrated photos to the Reject folder, copy with rename, and more.

![Thumbnail grid (ratings, flags, and color labels)](docs/images/screenshot-grid.png)

## Shortcuts

Only the essentials are listed here. **Press `F1` in the app for all shortcuts**, or see **[docs/SHORTCUTS.en.md](docs/SHORTCUTS.en.md)**.

| Keys | Description |
|---|---|
| `0` – `5` | Rating |
| `6` / `7` / `8` / `9` / `P` | Color label (red / yellow / green / blue / purple) |
| `Ctrl+↑` / `Ctrl+↓` | Accept / reject flag |
| `←` / `→` | Previous / next photo |
| `Z` / `Shift+Z` | Toggle zoom / 100% |
| `Ctrl+L` | Filter on/off |
| `F11` / `Shift+F` | Full screen / full screen (image only) |
| `F1` | Show keyboard shortcuts |

> The source of truth for the shortcut list is [`shortcuts.json`](shortcuts.json) (shared by the in-app `F1` view and the generated `docs/SHORTCUTS*.md`).

## Building (for developers)

- Prerequisites: a .NET SDK that can build `net10.0-windows`, the Windows App SDK, and Developer Mode enabled.
- Run:
  ```powershell
  cd src\PhotoQuickSelector.App
  dotnet run
  ```
- Test:
  ```powershell
  dotnet test
  ```
- Publish for distribution (self-contained, single file):
  ```powershell
  dotnet publish src\PhotoQuickSelector.App -c Release -p:Platform=x64 -p:PublishProfile=win-x64-singlefile
  ```

See [CLAUDE.md](CLAUDE.md) for development notes and [SPEC.md](SPEC.md) for the specification (both in Japanese).

## License

Distributed under the [MIT License](LICENSE) (Copyright © 2026 KOBAT).
For the licenses of bundled third-party libraries, see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
