<div align="center">
  <img src="docs/logo.png" alt="TOST Logo" width="130"/>
  <h1>TOST (Linux)</h1>
  <p>
    <img src="https://img.shields.io/badge/C%23-10.0%2B-239120?logo=csharp&logoColor=white" alt="C#">
    <img src="https://img.shields.io/badge/Avalonia-11.0%2B-purple?logo=avalonia&logoColor=white" alt="Avalonia UI">
    <img src="https://img.shields.io/badge/Platform-Linux%20(Native%20%26%20Flatpak)-blue" alt="Platform">
  </p>
  <p><b>An open-source Linux Steam tool, Game Manager, and CDN Downloader.</b></p>
</div>

---

## What is TOST?

**TOST** is a dedicated open-source utility and Game Manager for Steam on Linux. It brings a sleek floating desktop drop widget, full manifest and Lua sideloading, automated **[SLSsteam](https://github.com/AceSLS/SLSsteam)** engine integration, and a built-in Steam CDN multi-depot download manager.

* **One-Click SLSsteam Engine Setup:** Right-click the floating icon → **Apply SLSsteam** to automatically fetch, verify, and configure the latest official `libSLSsteam.so` library and launch hooks for both **Native Steam** and **Flatpak Steam**.
* **Drag-and-Drop Sideloading:** Drop `.zip`, `.lua`, or `.manifest` files directly onto the floating widget to unpack and install them into your Steam directories safely.
* **Steam-Style Game Manager:** Browse your managed games with official Steam artwork, installation state detection, one-click `▶ Play` launch, installation folder browsing, and clean uninstallation.
* **Built-in Steam CDN Downloader:** Direct multi-depot game downloads powered by `DepotDownloaderMod`. Features a full Steam-style **Downloads** page with live network/disk speeds, dual progress bars, ETA, and queue management.
* **Headless Linux CLI (`tost`):** Complete terminal and script automation support.

---

## Installation

### One-Line Install Script
```bash
curl -sSL https://raw.githubusercontent.com/sadabx/TOST/linux-support/install.sh | bash
```

### Manual / From Source
Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/sadabx/TOST.git -b linux-support
cd TOST
dotnet publish desktop/TOST.Desktop.csproj -c Release -r linux-x64 --self-contained false -o ~/.local/share/TOST/bin/
dotnet publish cli/TOST.Linux.csproj -c Release -r linux-x64 --self-contained false -o ~/.local/share/TOST/bin/

mkdir -p ~/.local/bin
ln -sf ~/.local/share/TOST/bin/TOST.Desktop ~/.local/bin/tost-desktop
ln -sf ~/.local/share/TOST/bin/tost ~/.local/bin/tost
```

### Downloader Backend (`DepotDownloaderMod`)
For downloading game files from Steam CDN, place the `DepotDownloaderMod` executable in:
```bash
mkdir -p ~/.local/share/TOST/tools/
cp /path/to/DepotDownloaderMod ~/.local/share/TOST/tools/
chmod +x ~/.local/share/TOST/tools/DepotDownloaderMod
```
*(Or ensure `DepotDownloaderMod` is available in your system `$PATH`).*

---

## How to Use

1. **Launch TOST Desktop:**
   Run `tost-desktop` from your terminal or application launcher. A small floating icon will appear on your desktop.
2. **Apply SLSsteam:**
   Right-click the floating icon and select **Apply SLSsteam**. TOST will download the latest verified release and configure launch injection for Native or Flatpak Steam.
3. **Import Games:**
   Drag and drop `.zip`, `.lua`, or `.manifest` files onto the floating icon (or use `tost import <files...>`). TOST automatically unpacks archives, routes manifests to `depotcache/`, Lua files to `config/stplug-in/`, and registers depot decryption keys in `config.vdf`.
4. **Manage & Download:**
   Right-click the icon → **Manage Games** (or run `tost games`):
   - Click **Download** to start downloading game depots directly from Steam CDN. The view switches to the dedicated **Downloads** page with real-time speed, dual progress bars, and ETA.
   - When finished, restart Steam and click **Play**!

---

## CLI Usage (`tost`)

TOST includes a standalone Linux CLI:

```bash
# Check Steam installations and status
tost status

# Install / update SLSsteam integration
tost install-slssteam [--flatpak] [--apply]

# Import games from .zip, .lua, or .manifest files
tost import <file.zip> [--flatpak] [--apply]

# List managed games and their install status
tost games [--flatpak]

# Download game depots from Steam CDN
tost download <appId> [--flatpak]

# Configure or remove Steam launch injection
tost configure-launch [--flatpak] [--apply]
tost remove-launch [--flatpak] [--apply]

# Tail SLSsteam diagnostic logs
tost logs [lines]
```

---

## Building from Source

```bash
# Build the entire solution
dotnet build TOST.sln

# Run unit tests
dotnet run --project tests/core/TOST.Core.Tests.csproj

# Run Desktop GUI in development
dotnet run --project desktop/TOST.Desktop.csproj
```

*(Architecture and technical details are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).)*

---

## Credits

- **[SLSsteam](https://github.com/AceSLS/SLSsteam)** by **[AceSLS](https://github.com/AceSLS)** — Linux Steam client modding & backend.
- **[DepotDownloaderMod](https://github.com)** — Headless Steam depot downloader backend.
- **[Avalonia UI](https://avaloniaui.net/)** — Cross-platform .NET UI framework.

### Disclaimer
This project is for research and educational purposes only. TOST is not affiliated with, endorsed by, or connected to Valve, Steam, or SLSsteam.

---

Distributed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for details.
