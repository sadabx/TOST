# Third-party launcher workarounds

- [ ] Rockstar (`socialclub64.dll`)
- [ ] EA (`Activation64.dll`)
- [ ] Ubisoft (`uplay_r1_loader64.dll`)

# Cross-platform architecture

- [ ] Extract platform-neutral game, archive, logging, and configuration logic into `TOST.Core`.
- [ ] Define an integration-provider interface shared by OpenSteamTool and SLSsteam.
- [ ] Move Windows-specific OpenSteamTool behavior into `TOST.Windows`.
- [ ] Keep the existing WinForms application operational until Avalonia reaches feature parity.

# Linux implementation

- [x] Create an initial Linux CLI before committing to a cross-platform GUI framework.
- [x] Add Linux Steam discovery for native Steam, Flatpak, SteamOS, and Bazzite installations.
- [ ] Add an SLSsteam provider for installation status, version checks, configuration, logs, and safe removal.
- [ ] Download pinned upstream releases and verify published checksums; do not execute unpinned curl-to-shell installers.
- [ ] Preserve upstream licenses, attribution, and source links in the Linux package.
- [ ] Add import support for user-provided Lua and manifest files.
- [ ] Parse only supported Lua data declarations; do not execute imported Lua scripts.
- [ ] Convert supported AppID, depot, token, manifest, and DLC metadata into SLSsteam YAML.
- [ ] Validate imported paths, identifiers, duplicate entries, file sizes, and manifest filenames.
- [ ] Back up SLSsteam configuration and affected manifest files before every change.
- [ ] Add one-click removal and restoration using the same recovery model as Windows TOST.
- [ ] Add an Install button that delegates authorized AppID installation to the official Steam client.
- [ ] Clearly report entitlement, missing-key, missing-token, and CDN authorization failures from Steam.
- [ ] Never claim that appearing in the library guarantees download, launch, multiplayer, or anti-cheat compatibility.
- [ ] Add safe Steam restart handling without terminating unrelated Wine/Proton processes.
- [ ] Add compatibility warnings when Steam client updates invalidate the installed SLSsteam version.

# Avalonia UI migration

- [ ] Stabilize and test the shared core and Linux/SLSsteam backend before starting the UI migration.
- [ ] Create a shared `TOST.Desktop` Avalonia project targeting Windows and Linux.
- [ ] Rebuild the floating icon, menus, settings, dialogs, Game Manager, and recovery UI in Avalonia.
- [ ] Add platform services for startup registration, tray/floating-window behavior, opening folders, and restarting Steam.
- [ ] Add drag-and-drop import support on both Windows and Linux.
- [ ] Match the existing TOST dark styling without depending on native WinForms rendering.
- [ ] Verify feature parity with the WinForms application before retiring it.
- [ ] Remove the WinForms frontend only after Windows and Linux packages pass release testing.

# Testing and packaging

- [ ] Test native Steam and Flatpak Steam separately on a standard desktop distribution.
- [ ] Test SteamOS/Bazzite filesystem and sandbox behavior without writing to immutable system paths.
- [ ] Add unit tests for Lua parsing, YAML generation, path validation, backup, removal, and restoration.
- [ ] Add integration tests using temporary fake Steam libraries; never modify a developer's real Steam installation.
- [ ] Package Linux CLI builds as a portable archive while the backend is under development.
- [ ] Package the completed Avalonia application for Windows and Linux.
- [ ] Evaluate AppImage and Flatpak after the Avalonia application is stable.

# Online-fix investigation

- [ ] Define the legal, licensing, security, and technical scope before implementation.
