# OpenSteamTool Floating Installer

Small Windows helper app for installing expected OpenSteamTool files through a
floating desktop icon.

## Behavior

- `Install / Repair OpenSteamTool` copies known files from `files/` beside the
  executable.
- Dragging a file or folder onto the floating icon routes all known files.
- `.lua` files copy to `<Steam>/config/lua/`
- `opensteamtool.toml` copies to `<Steam>/`
- `OpenSteamTool.dll`, `dwmapi.dll`, and `xinput1_4.dll` copy to `<Steam>/`
- `appmanifest_*.acf` and `.manifest` files copy to `<Steam>/steamapps/`
- Existing files are backed up with a timestamped `.bak-*` suffix before
  overwrite.
- Copy/install activity is written to `logs/install.log`.

The app detects Steam from `HKCU\Software\Valve\Steam\SteamPath`, then falls
back to `C:\Program Files (x86)\Steam`.

## Settings

Right-click the floating icon and choose `Settings` to configure:

- Steam folder
- overwrite behavior
- backup behavior
- start with Windows
- always on top

## Build

Install the .NET 8 SDK on Windows, then run:

```powershell
dotnet publish .\tools\FloatingInstaller\OpenSteamTool.FloatingInstaller.csproj -c Release -r win-x64 --self-contained true
```

The executable is published as `OST.exe`.

For a release zip, run from this folder:

```powershell
.\build-release.ps1
```

The script creates `OST-Floating-Installer.zip`. If OST DLLs already exist at
`build\src\Release`, they are copied into the release `files/` folder
automatically. `opensteamtool.example.toml` is copied as `opensteamtool.toml`.

## Release layout

```text
OST-Floating-Installer.zip
  OST.exe
  Assets/
  files/
    OpenSteamTool.dll
    dwmapi.dll
    xinput1_4.dll
    opensteamtool.toml
  logs/
```

## Icon

The app icon is generated from `docs/logo-1024.png` and stored at
`tools/FloatingInstaller/Assets/opensteamtool.ico`.
