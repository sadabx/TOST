# TOST Linux Architecture & Technical Details

This document outlines the internal architecture, design principles, and integration mechanics of **TOST** on Linux.

---

## 1. System Overview

TOST on Linux is structured as a decoupled application powered by a shared core library and specialized backends:

```
┌────────────────────────────────────────────────────────────────────────┐
│                              Frontends                                 │
│  ┌──────────────────────────────────────┐  ┌─────────────────────────┐ │
│  │         desktop/ (Avalonia UI)       │  │       cli/ (Linux)      │ │
│  │  - Floating Drop Widget              │  │  - Headless CLI (`tost`)│ │
│  │  - Steam-Style Game Manager          │  │  - Automated Pipeline   │ │
│  │  - Real-Time Downloads View          │  │  - Script Integration   │ │
│  └───────────────────┬──────────────────┘  └────────────┬────────────┘ │
└──────────────────────┼──────────────────────────────────┼──────────────┘
                       │                                  │
                       ▼                                  ▼
┌────────────────────────────────────────────────────────────────────────┐
│                             core/ Engine                               │
│  ├── Downloads/       (DepotDownloaderMod runner, plan & status)       │
│  ├── Imports/         (ZIP unpacking, manifest & Lua static routing)   │
│  ├── GameManagement/  (Game discovery, metadata, uninstalls)           │
│  ├── Steam/           (Process guard, discovery, restart lifecycle)    │
│  ├── Configuration/   (Preferences & atomic settings)                  │
│  └── Integrations/    (SLSsteam installer & launch plan factory)       │
└──────────────────────────────────────┬─────────────────────────────────┘
                                       │
                 ┌─────────────────────┴─────────────────────┐
                 ▼                                           ▼
      ┌──────────────────────┐                    ┌──────────────────────┐
      │   Sideload Backend   │                    │   Download Backend   │
      │       SLSsteam       │                    │  DepotDownloaderMod  │
      │  (libSLSsteam.so)    │                    │ (Steam CDN Runner)   │
      └──────────────────────┘                    └──────────────────────┘
```

---

## 2. Project Structure

* **`core/`**: Shared Linux business logic containing no UI dependencies. Houses manifest parsers, VDF key decoders/mergers, Steam installation discovery, integration installers, and the download planning subsystem.
* **`desktop/`**: Desktop application built on **Avalonia UI**:
  - `FloatingIconWindow`: Frameless, draggable, always-on-top desktop drop target.
  - `GameManagerView`: Steam-style library with header images from Steam CDN, installation tracking, and action controls.
  - `DownloadsView`: Dedicated Steam-style downloads dashboard with live network/disk metrics, dual progress bars, ETA, and queue management.
  - `DownloadManager`: UI background coordinator managing active tasks, cancellation, and download state.
* **`cli/`**: Lightweight command-line interface (`tost`) providing headless control over imports, status, installation, and downloads.
* **`tests/`**: Unit test suite covering Lua tokenization, VDF depot key merging, manifest routing, archive security bounds, download planning, and installation state checks.
* **`packaging/`**: Build scripts and metadata for Linux packaging (AppImage, deb, PKGBUILD, desktop entry).
* **`docs/`**: Documentation, architecture guides, and assets.

---

## 3. Linux Integration Architecture (SLSsteam)

TOST targets both standard native Steam and containerized Flatpak Steam via **[SLSsteam](https://github.com/AceSLS/SLSsteam)**.

### Static Lua Parsing (No Untrusted Code Execution)
TOST never evaluates untrusted Lua code directly. Instead, `SteamImportInspector` statically tokenizes declarations:
* Identifies App IDs, DLC entries, and depot manifest IDs via regex token matching.
* Extracts depot decryption keys from `addappid` and `setdepotkey` statements.
* Merges keys safely into Steam's `config.vdf` (`Software.Valve.Steam.Depots.<depotId>.DecryptionKey`).
* Updates SLSsteam's `config.yaml` with managed App IDs.

### Launch Injection
* **Native Steam** (`~/.local/share/Steam`): Generates a guarded shell wrapper script in `~/.local/share/SLSsteam/path/steam` that exports `LD_PRELOAD` pointing to `libSLSsteam.so` before executing the system Steam binary.
* **Flatpak Steam** (`com.valvesoftware.Steam`): Applies user-level filesystem and environment overrides via `flatpak override --user` to expose data directories across container boundaries.

---

## 4. Download Subsystem (`core/Downloads/`)

The download engine automates game file acquisition from Valve's Akamai CDN using `DepotDownloaderMod`:

```
                       ┌─────────────────────────┐
                       │    Managed Game / Lua   │
                       └────────────┬────────────┘
                                    │
                                    ▼
                       ┌─────────────────────────┐
                       │   GameDownloadPlanner   │
                       │ - Maps .manifest files  │
                       │ - Extracts depot keys   │
                       │ - Filters active depots │
                       └────────────┬────────────┘
                                    │
                                    ▼
                       ┌─────────────────────────┐
                       │   DepotDownloadRunner   │
                       │ - Headless process exec │
                       │ - Per-chunk stdout scan │
                       │ - Rate & bytes tracking │
                       └────────────┬────────────┘
                                    │
                         ┌──────────┴──────────┐
                         ▼                     ▼
             ┌──────────────────────┐ ┌──────────────────────┐
             │ steamapps/common/    │ │ steamapps/           │
             │ <Game Install Dir>   │ │ appmanifest_<id>.acf │
             └──────────────────────┘ └──────────────────────┘
```

### Components
1. **`DepotDownloaderLocator`**: Searches for `DepotDownloaderMod` in `~/.local/share/TOST/tools/` and system `$PATH`.
2. **`DepotKeyExtractor`**: Reads depot decryption keys from imported Lua scripts and Steam's `config.vdf`. Passes keys via an ephemeral file argument (`-depotkeys <temp_file>`) during download.
3. **`GameDownloadPlan`**: Determines which depots to acquire. Only includes depots that have corresponding local `.manifest` files in `depotcache/`, preventing access denied (401) errors on unowned platform depots.
4. **`DepotDownloadRunner`**:
   - Executes `DepotDownloaderMod` with redirected streams.
   - Parses regex progress lines (`(?<percent>\d+)%\s+(?<file>.*)`).
   - Monitors filesystem byte deltas to calculate instantaneous network speed, peak speed, and time remaining.
   - Upon completion, calculates directory disk usage and writes a complete `appmanifest_<appId>.acf` (`StateFlags 4`) so Steam detects the game as installed without manual verification.
5. **`GameInstallStatus`**: Verifies Steam installation state. Requires an existing `appmanifest_<appId>.acf` with `StateFlags == 4` and non-empty installation files. Orphaned directories without an ACF manifest are accurately classified as uninstalled.

---

## 5. Security & Safety Guards

* **Archive Bomb Protection**: ZIP extractions strictly enforce safety thresholds (`MaximumArchiveEntries = 10,000`, `MaximumPayloadBytes = 512 MB`, `MaximumEntryBytes = 256 MB`).
* **Path Traversal Guards**: Extracted files and manifest paths are strictly verified to ensure they reside within their designated Steam directories (`depotcache/`, `config/stplug-in/`, `steamapps/`).
* **Atomic Backups**: Any configuration file modified by TOST (`config.yaml`, `config.vdf`) is backed up to a byte-exact `.backup` file before writing.
* **Safe Uninstalls**: Removing a game cleanly deletes its Lua script, unshared manifests, `appmanifest_<appId>.acf`, and downloaded files in `steamapps/common/` while preserving shared manifests used by other titles.
