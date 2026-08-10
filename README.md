<div align="center">
  <img src="Assets/TOST.png" alt="TOST Logo" width="150"/>
  <h1>TOST</h1>
  <p><b>Trionine Open Steam Tool</b></p>
  <p>
    <img src="https://img.shields.io/badge/C%23-10.0%2B-239120?logo=csharp&logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/Avalonia-11.0%2B-purple?logo=avalonia&logoColor=white" alt="Avalonia UI">
    <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux-blue" alt="Cross Platform">
  </p>
</div>

## Index

1. [What is TOST?](#what-is-tost)
2. [Features](#features)
3. [Getting Started](#getting-started)
4. [Screenshots](#screenshots)
5. [Building from Source](#building-from-source)
6. [Support](#support)
7. [Credits](#credits)

## What is TOST?

TOST is a modern, cross-platform floating desktop companion for Steam. It simplifies the installation and management of game manifests, Lua plugins, and Steam backend tools. 

On **Windows**, TOST seamlessly manages [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool). 
On **Linux**, it manages [SLSsteam](https://github.com/AceSLS/SLSsteam). 

Instead of manually digging through Steam folders to paste `.manifest` and `.lua` files, TOST lets you drag and drop them right onto its floating icon. It handles all the file routing, backups, and configurations for you automatically.

## Features

### Core Management
- **Floating Desktop Icon**: A sleek, unobtrusive floating widget that sits on your screen for quick drag-and-drop installations.
- **One-Click Tool Installation**: Automatically download, install, or repair OpenSteamTool (Windows) or SLSsteam (Linux) with a single click.
- **Drag-and-Drop Installation**: Drop local `.zip`, `.lua`, or `.manifest` packages onto TOST, and it will route everything to the correct Steam directories.
- **Built-in Game Manager**: Easily view, remove, or restore managed games and their associated files without breaking your Steam installation.

### Safety and Compatibility
- **Safe Operations**: TOST automatically backs up existing files before replacing them and refuses to execute unverified Lua scripts.
- **Full Linux Support**: Native support for both standard Steam and Flatpak Steam on Linux.

## Getting Started

### Installation
Head over to the [TOST Releases Page](https://github.com/sadabx/TOST/releases) and download the latest version for your operating system.

- **Windows Users**: Download `*-Setup.exe` for a standard installation (recommended), or `*-Portable.zip` if you prefer a standalone directory.
- **Linux Users**: Download the `.AppImage`, `.deb` (Debian/Ubuntu), `.pkg.tar.zst` (Arch), or the portable `.tar.gz`.

### Basic Usage
1. Launch TOST. A floating "T" icon will appear on your screen.
2. Right-click the icon to open the main menu.
3. Select **Install / Repair OpenSteamTool** (Windows) or **Install / Repair SLSsteam** (Linux) to initialize the backend engine.
4. Drag and drop game packages (ZIPs, manifests, lua files) directly onto the floating icon to install them into Steam.
5. Open the **Manage Games** menu to review installed modifications or to safely remove them.

## Screenshots

<details>
<summary>Click to expand screenshots</summary>

### The Floating Menu
![TOST menu](Assets/ss/TOST.png)

### Game Manager
![TOST Game Manager](Assets/ss/game-manager.png)

### Settings & Imports
![TOST file import](Assets/ss/files-dropped.png)
</details>

## Building from Source

TOST is built with C# and [Avalonia UI](https://avaloniaui.net/) for a shared, native cross-platform experience.

### Project Structure
- `Core/` - Shared business logic, Steam detection, and file routing.
- `Desktop/` - The modern Avalonia UI (Windows & Linux).
- `CLI/` - Linux terminal interface.
- `Tests/` - Automated unit test suite.
- `Legacy/` - The old, retired WinForms reference application.

### Requirements
- .NET 8.0 SDK

### Quick Build
**Run the Desktop App:**
```bash
dotnet run --project Desktop/TOST.Desktop.csproj
```

**Run the CLI (Linux):**
```bash
dotnet run --project CLI/Linux/TOST.Linux.csproj -- help
```

### Linux Architecture Details
Linux support (CLI and Desktop) natively supports Steam and Flatpak Steam. TOST safely parses Lua declarations without executing them, translates App IDs and overrides into `config.yaml`, and registers depot keys in `config.vdf`. It also generates guarded native Steam wrappers or per-user Flatpak environment overrides (`configure-launch`).

## Support

Please ensure you are running the latest release of TOST before reporting issues. Bug reports and feature requests should be submitted to the [GitHub Issue Tracker](https://github.com/sadabx/TOST/issues).

## Credits

### Contributors
- Developed and maintained by [sadabx](https://github.com/sadabx) under [TRIONINE](https://trionine.com/).

### Upstream Projects
- **OpenSteamTool**: TOST uses the supported file layout and logo assets of [OpenSteamTool](https://github.com/OpenSteam001/OpenSteamTool), which remains owned and maintained by its own contributors.
- **SLSsteam**: TOST integrates with [SLSsteam](https://github.com/AceSLS/SLSsteam) for Linux support, owned and maintained by its respective contributors.

### Disclaimer
This project is provided for research and educational purposes only. TOST is an independent open-source manager and is not affiliated with, maintained, or endorsed by OpenSteamTool, SLSsteam, Valve, or Steam. You are responsible for complying with local laws, platform terms of service, and software licenses.

---
Distributed under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for details.
