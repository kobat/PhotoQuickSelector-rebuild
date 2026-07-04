# PhotoQuickSelector

[日本語 (Japanese)](README.md)

A Windows desktop app for browsing and culling photos at high speed.
Review photos in a local folder with keyboard-centric controls, and cull them with ratings, flags, and color labels. Evaluations never modify the original files — they are stored in a per-folder SQLite database.

![Preview screen](docs/images/screenshot-preview.png)

<!-- Sample screenshot. To replace or add images, edit the files under docs/images/ and this reference. -->

## Features

- **Single window with a split layout** — left: folder tree (favorites / recent folders), right: browsing (thumbnail grid ⇄ large preview).
- **Comfortable even with thousands of photos** — parallel metadata loading, prefetch caches for thumbnails and pixel data.
- **Evaluations that never touch your files** — ratings (0–5), accept/reject flags, and color labels (5 colors) are stored in a per-folder `PhotoQuickSelector.sqlite3`.
- **A serious preview** — zoom / pan / loupe (100% inspection) / navigator, with EXIF, AF frame, and composition grid overlays.
- **Filters and exports to finish the cull** — condition filter + file name list copy, move unrated photos to a Reject folder, copy with rename.
- **Fast, keyboard-centric operation** — rating, navigation, zoom, multi-select, and bulk rating via shortcuts (press `F1` for the full list).
- **No installation** — a self-contained EXE bundling the .NET / Windows App SDK runtimes.
- **Japanese / English UI** — follows the OS display language by default; switchable in Settings.

## Requirements

- Windows 10 / 11 (x64)
- The runtimes are bundled, so no prior installation of .NET or the Windows App SDK is required.

## Install / Run

1. Download the latest EXE from [Releases](https://github.com/kobat/PhotoQuickSelector-rebuild/releases).
2. Run the downloaded EXE.

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
