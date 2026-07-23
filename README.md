# OST  Installer

Small Windows helper app for installing expected OpenSteamTool files through a
floating desktop icon.

This project is a standalone helper. It is not the original OpenSteamTool
project and is not the closed-source SteamTools app.

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

Install the .NET 8 SDK, then run:

```powershell
dotnet publish .\OpenSteamTool.FloatingInstaller.csproj -c Release -r win-x64 --self-contained true
```

The executable is published as `OST.exe`.

For a release zip, run from this folder:

```powershell
.\build-release.ps1
```

The script creates `OST-Floating-Installer.zip`. Put redistributable payload
files in `files/` before running the script if you want `Install / Repair
OpenSteamTool` to copy them automatically.

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

Do not include `.manifest` or `.lua` files in public releases unless you have
permission to redistribute them. Users can drag those files onto the floating
icon themselves.

## Icon

The app icon/logo assets are derived from the OpenSteamTool project assets.

## Credits

Thanks to the OpenSteamTool project for the original tool and visual assets:

https://github.com/OpenSteam001/OpenSteamTool

This repository only provides a floating installer/helper around expected
OpenSteamTool file placement.

## License

GPL-3.0. See `LICENSE`.
