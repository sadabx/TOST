# TOST
**trionine open-source installer for OpenSteamTool**

TOST is an independent Windows utility created and maintained by
[sadabx](https://github.com/sadabx). It provides a
floating desktop icon for routing supported OpenSteamTool files into the
correct Steam directories.

TOST is built around the separately maintained
[OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool) project. It is
not the closed-source SteamTools application, and it is not owned, maintained,
or endorsed by OpenSteamTool, Valve, or Steam.

## Preview

![TOST floating installer menu](Assets/TOST.png)

## Features

- Floating icon and system tray controls
- Drag-and-drop or local package installation
- Automatic Steam detection, file routing, and backups
- Import notifications, logs, and useful shortcuts
- Installed and portable builds with update support

TOST does not bundle or silently download third-party OpenSteamTool files.
Download payloads only from sources you trust, then drag them onto TOST or use
`Install / Repair OpenSteamTool`.

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

- Drag supported files, folders, or ZIP packages onto the floating icon.
- Use `Install / Repair OpenSteamTool` to select a local ZIP, DLL, or TOML file.
- Right-click the icon for the menu; double-click it to restart Steam.
- Double-click the system tray icon to restore a hidden floating icon.


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
- Third-party payloads require explicit local user selection.

Only install files that you trust and have permission to use or redistribute.

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
