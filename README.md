# TOST
**Trionine open-source installer for OpenSteamTool**

TOST is an independent Windows utility created and maintained by
[sadabx](https://github.com/sadabx). It downloads and installs the latest
official OpenSteamTool release, and provides a floating desktop icon for
routing supported local files into the correct Steam directories.

TOST is built around the separately maintained
[OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) project. It is
not the closed-source SteamTools application, and it is not owned, maintained,
or endorsed by OpenSteamTool, Valve, or Steam.

## Preview

![TOST floating installer menu](Assets/TOST.png)

## Features

- Floating icon and system tray controls
- Automatic OpenSteamTool installation and repair from its official release
- Drag-and-drop installation for local packages
- Automatic Steam detection, file routing, and backups
- Import notifications, logs, and useful shortcuts
- Installed and portable builds with update support

TOST does not bundle OpenSteamTool files. Selecting
`Install / Repair OpenSteamTool` explicitly downloads the latest release ZIP
from the official OpenSteamTool GitHub repository and installs its supported
files. Local packages can still be imported by dragging them onto TOST.

## Requirements

- Windows 10 or newer
- 64-bit Windows
- An existing Steam installation

## Download

The recommended download is the `*-Setup.exe` asset on the
[TOST Releases](https://github.com/sadabx/TOST/releases) page. It installs TOST
for the current Windows user and enables in-place updates.

The `*-Portable.zip` asset is for users who prefer no installation. Extract the
complete archive to a writable folder and run `TOST.exe`. Keep every extracted
file together; the portable package is a directory-based application, not a
single standalone executable.

## Usage

- Use `Install / Repair OpenSteamTool` to download and apply the latest official
  OpenSteamTool release automatically.
- Drag supported files, folders, or ZIP packages onto the floating icon to
  import local packages.
- Right-click the icon for the menu; double-click it to restart Steam.
- Double-click the system tray icon to restore a hidden floating icon.

### Automatic installation

When `Install / Repair OpenSteamTool` is selected, TOST:

1. Resolves the latest release from the official OpenSteamTool GitHub repository.
2. Downloads the non-debug release ZIP to a temporary location.
3. Validates the archive and copies supported files into the Steam directory.
4. Removes the temporary download when installation finishes.

Existing files are backed up before replacement when backups are enabled.
Restart Steam after installation so the new files take effect.

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
- `View OpenSteamTool Releases`
- `Open ManifestHub`
- `Open Steam Folder`
- `TOST Settings`
- `Check for Updates`
- `Open Logs`
- `Hide Floating Icon`
- `Exit`

## Settings and updates

Installed builds store settings and logs under:

```text
%LocalAppData%\TOST\data
```

Portable builds store them beside `TOST.exe`. On first launch, TOST copies
compatible settings and logs from the previous OST locations when possible.

Installed builds check
[TOST GitHub Releases](https://github.com/sadabx/TOST/releases) at most once
every 24 hours. Automatic checks can be disabled in Settings. Portable and raw
development builds can check the release page but do not modify themselves in
place.

## Safety

- Only recognized filenames and extensions are copied.
- Existing files can be backed up before replacement.
- ZIP packages with duplicate supported filenames are rejected.
- Oversized ZIP entries and payloads are rejected.
- Automatic downloads use the official OpenSteamTool GitHub release.
- Local third-party payloads require explicit drag-and-drop.

Only install files that you trust and have permission to use or redistribute.

## Project structure

```text
TOST/
|-- .config/
|   `-- dotnet-tools.json     Pinned Velopack CLI
|-- Assets/
|   |-- TOST.png              README preview
|   |-- logo-128.png          Embedded floating-window logo
|   |-- logo-512.png          High-resolution logo
|   `-- opensteamtool.ico     Application and installer icon
|-- .gitignore
|-- LICENSE
|-- Program.cs                WinForms application
|-- README.md
|-- TOST.csproj               .NET project
|-- build-release.ps1         Windows build and packaging script
|-- publish-release.ps1       GitHub release publishing script
`-- release-notes.md          Packaged release notes
```

Generated `bin/`, `obj/`, `artifacts/`, and `Releases/` directories are not
committed.

## Credits

### TOST

Created and maintained by [sadabx](https://github.com/sadabx) under
[Trionine](https://trionine.com/).

### OpenSteamTool

TOST uses the supported file layout and logo assets of the
[OpenSteamTool project](https://github.com/OpenSteam001/OpenSteamTool), which
remains owned and maintained by its contributors. This attribution does not
imply that OpenSteamTool created, owns, maintains, or endorses TOST.

## License

TOST is distributed under the GNU General Public License v3.0. See
[LICENSE](LICENSE).
