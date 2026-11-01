# TOST Architecture & Technical Details

This document outlines the internal architecture, design principles, and integration mechanics of **TOST**.

---

## 1. System Overview

TOST is designed as a decoupled, multi-frontend application powered by a shared core library:

```
┌────────────────────────────────────────────────────────┐
│                      Frontends                         │
│  ┌────────────────────────┐  ┌───────────────────────┐ │
│  │   desktop/ (Avalonia)  │  │      cli/ (Linux)     │ │
│  │  - Floating Drop Icon  │  │  - Headless/Terminal  │ │
│  │  - Game Manager View   │  │  - Script Automation  │ │
│  │  - Settings & Logs     │  │                       │ │
│  └───────────┬────────────┘  └───────────┬───────────┘ │
└──────────────┼───────────────────────────┼─────────────┘
               │                           │
               ▼                           ▼
┌────────────────────────────────────────────────────────┐
│                     core/ Engine                       │
│  ├── Imports/         (Manifest, Lua & ZIP routing)    │
│  ├── GameManagement/  (Game discovery & uninstalls)    │
│  ├── Steam/           (Process & path discovery)       │
│  ├── Configuration/   (Preferences & atomic backups)   │
│  └── Integrations/    (Backend installer services)     │
└──────────────────────────┬─────────────────────────────┘
                           │
             ┌─────────────┴─────────────┐
             ▼                           ▼
  ┌──────────────────────┐    ┌──────────────────────┐
  │   Windows Backend    │    │    Linux Backend     │
  │   BetterSteamTools   │    │       SLSsteam       │
  │  (dwmapi.dll proxy)  │    │  (libSLSsteam.so)    │
  └──────────────────────┘    └──────────────────────┘
```

---

## 2. Project Structure

* **`core/`**: Shared business logic containing no UI dependencies. Houses manifest parsers, VDF decoders, Steam installation detectors, and integration installers.
* **`desktop/`**: Cross-platform desktop application built on **Avalonia UI**. Implements the frameless, always-on-top floating drop widget, dialogs, and game manager.
* **`cli/`**: Lightweight command-line interface for Linux headless and terminal usage.
* **`tests/`**: Unit test suite covering VDF key merging, manifest routing, archive limits, and TOML configuration.
* **`packaging/`**: Build scripts and metadata for Linux distributions (AppImage, Deb, Arch PKGBUILD).
* **`docs/`**: Architecture documentation, logos, and screenshots.

---

## 3. Windows Integration Architecture (BetterSteamTools)

On Windows, TOST automates and manages the **[BetterSteamTools](https://github.com/madoiscool/BetterSteamTools)** hook engine.

### Hook Mechanism
* **DLL Proxying**: BetterSteamTools deploys as proxy libraries (`dwmapi.dll` / `xinput1_4.dll`) alongside `steam.exe`.
* When Steam starts, Windows resolves the local proxy DLL before the system library, allowing the hook to intercept Steam's IPC and manifest handlers.

### Auto-Configured Pattern Tracker & Endpoints
To prevent Steam client updates from breaking hooks (which previously caused "Unsupported Steam Version" or "IPC spec missing" errors), TOST automatically inspects and configures `opensteamtool.toml`:
1. **Pattern Tracker**: Sets `[remote] url_template` to the active community monitor on jsDelivr:
   ```toml
   [remote]
   url_template = "https://cdn.jsdelivr.net/gh/madoiscool/steam-monitor@{channel}/{component}/{sha256}.toml"
   ```
2. **Manifest Endpoint**: Automatically upgrades outdated `opensteamtool` endpoints to `steamrun`:
   ```toml
   [manifest]
   url = "steamrun"
   ```

### File Routing (Windows)
When files are dropped onto TOST, they are routed to their designated destinations:
* `*.manifest` → `<Steam>/depotcache/`
* `*.lua` → `<Steam>/config/stplug-in/`
* `appmanifest_*.acf` → `<Steam>/steamapps/`
* Core DLLs & config → `<Steam>/` root

---

## 4. Linux Integration Architecture (SLSsteam)

Linux support targets both standard native Steam and containerized Flatpak Steam via **[SLSsteam](https://github.com/AceSLS/SLSsteam)**.

### Static Lua Parsing (No Untrusted Code Execution)
TOST never evaluates untrusted Lua code directly. Instead, it statically tokenizes and extracts metadata from Lua scripts:
* Scans for App IDs, DLC entries, and depot overrides.
* Translates extracted declarations into SLSsteam's `config.yaml`.
* Merges depot decryption keys into `config.vdf` safely without modifying unrelated user settings.

### Launch Hooking & Sandboxing
* **Native Steam**: Generates guarded shell wrappers in `~/.local/share/Steam/` ensuring `libSLSsteam.so` is cleanly preloaded before Steam initialization.
* **Flatpak Steam**: Manages user-level overrides via `flatpak override --user com.valvesoftware.Steam` to expose necessary paths and environment variables across sandbox boundaries.

---

## 5. Security & Safety Guards

* **Archive Bomb Protection**: ZIP extractions strictly enforce safety thresholds (`MaximumArchiveEntries = 10,000`, `MaximumPayloadBytes = 512 MB`, `MaximumEntryBytes = 256 MB`).
* **Atomic Backups**: Any file overwritten by TOST (such as `config.vdf` or `opensteamtool.toml`) is backed up to a byte-exact `.backup` file first.
* **Safe Uninstalls**: The `ManagedGameService` tracks manifest ownership, ensuring deleting a game only removes unshared manifests and leaves official Steam game files intact.
* **Single-Instance Enforcement**: A named mutex (`Local\Trionine.TOST.Avalonia.Instance`) prevents duplicate application instances from conflicting over Steam directories.
