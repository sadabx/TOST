# TOST

**A Trionine floating installer for OpenSteamTool.**

TOST is an independent Windows utility created and maintained by
[Sadabx](https://github.com/sadabx) under the Trionine name. It provides a
floating desktop icon for routing supported OpenSteamTool files into the
correct Steam directories.

TOST is built around the separately maintained
[OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) project. It is
not the closed-source SteamTools application, and it is not owned, maintained,
or endorsed by OpenSteamTool, Valve, or Steam.

## Features

- Compact, always-on-top floating icon with a system tray fallback
- Drag-and-drop installation for supported files and folders
- Local ZIP, DLL, and TOML package selection
- Automatic Steam directory detection and folder creation
- Timestamped backups before overwriting existing files
- Desktop notifications summarizing imported files
- Shortcuts for Steam, OpenSteamTool releases, and ManifestHub
- Installed and portable Windows packages
- Automatic update checks for the installed build
- Migration of existing OST settings and logs

TOST does not bundle or silently download third-party OpenSteamTool files.
Download payloads only from sources you trust, then drag them onto TOST or use
`Select Local Package`.

## Requirements

- Windows 10 or newer
- 64-bit Windows
- An existing Steam installation

Both distributions are self-contained. Users do not need to install .NET.

## Download

The recommended download is the `*-Setup.exe` asset on the
[TOST Releases](https://github.com/sadabx/OST/releases) page. It installs TOST
for the current Windows user and enables in-place updates.

The `*-Portable.zip` asset is for users who prefer no installation. Extract the
complete archive to a writable folder and run `TOST.exe`. Keep every extracted
file together; the portable package is a directory-based application, not a
single standalone executable.

## Usage

Right-click the floating icon to open its menu. Double-click it to launch Steam.
If the floating window is hidden, double-click the TOST system tray icon to
restore it.

### Install or repair

`Install / Repair OpenSteamTool` and `Select Local Package` open a local package
picker. TOST accepts ZIP, DLL, and TOML files that you downloaded separately.
ZIP archives are inspected without broadly extracting their contents, and only
recognized files are copied.

### Drag and drop

Drag supported files or folders onto the floating icon. TOST routes recognized
files to their destinations and shows a summary notification. Unsupported files
are skipped and recorded in the log.

### File routing

| File | Destination |
| --- | --- |
| `OpenSteamTool.dll` | Steam root |
| `dwmapi.dll` | Steam root |
| `xinput1_4.dll` | Steam root |
| `opensteamtool.toml` | Steam root |
| `*.lua` | `<Steam>\config\lua` |
| `appmanifest_*.acf` | `<Steam>\steamapps` |
| `*.manifest` | `<Steam>\steamapps` |

Steam is detected from
`HKCU\Software\Valve\Steam\SteamPath`. If unavailable, TOST falls back to
`C:\Program Files (x86)\Steam`.

## Menu

- `Launch Steam`
- `Restart Steam`
- `Install / Repair OpenSteamTool`
- `Select Local Package`
- `Open Official Releases`
- `Open ManifestHub`
- `Check for TOST Updates`
- `Open Steam Folder`
- `Open Logs`
- `Floating Window Settings`
- `Hide Floating Window`
- `Exit`

## Settings and updates

Installed builds store settings and logs under:

```text
%LocalAppData%\TOST\data
```

Portable builds store them beside `TOST.exe`. On first launch, TOST copies
compatible settings and logs from the previous OST locations when possible.

Installed builds check
[TOST GitHub Releases](https://github.com/sadabx/OST/releases) at most once
every 24 hours. Automatic checks can be disabled in Settings. Portable and raw
development builds can check the release page but do not modify themselves in
place.

When `Start with Windows` is enabled, TOST registers its stable installed
launcher. The old `OpenSteamToolFloatingInstaller` startup entry is removed
during migration.

## Safety

- Only recognized filenames and extensions are copied.
- Existing files can be backed up before replacement.
- ZIP packages with duplicate supported filenames are rejected.
- Oversized ZIP entries and payloads are rejected.
- Third-party payloads require explicit local user selection.

Only install files that you trust and have permission to use or redistribute.

## Build

Use Windows with the .NET 8 SDK. The repository pins Velopack `vpk` as a local
.NET tool.

Create setup, portable, package, and update-feed assets:

```powershell
.\build-release.ps1 -Version 1.0.0
```

For the first Velopack release, or when no previous feed exists:

```powershell
.\build-release.ps1 -Version 1.0.0 -SkipPreviousRelease
```

Output is written to `Releases\`. Upload every generated file together; the
`releases.win.json` and package files are required for automatic updates.

To create and publish a stable GitHub release automatically:

```powershell
$env:GITHUB_TOKEN = "token-with-releases-write-access"
.\publish-release.ps1 -Version 1.0.0
```

For later versions, run `build-release.ps1` without
`-SkipPreviousRelease`. It downloads the prior package before packing so
Velopack can generate a smaller delta update.

## Project structure

```text
.config/dotnet-tools.json  Pinned Velopack CLI
Assets/                    Application icon and attributed logo assets
Program.cs                 WinForms application
TOST.csproj                .NET project
build-release.ps1          Windows build and packaging script
publish-release.ps1        GitHub release publishing script
release-notes.md           Packaged release notes
```

Do not commit extracted proprietary applications, third-party payloads, local
settings, logs, or generated build output.

## Credits

### TOST

Created and maintained by [Sadabx](https://github.com/sadabx) under
[Trionine](https://trionine.com/).

### OpenSteamTool

TOST uses the supported file layout and logo assets of the
[OpenSteamTool project](https://github.com/OpenSteam001/OpenSteamTool), which
remains owned and maintained by its contributors. This attribution does not
imply that OpenSteamTool created, owns, maintains, or endorses TOST.

## License

TOST is distributed under the GNU General Public License v3.0. See
[LICENSE](LICENSE).
