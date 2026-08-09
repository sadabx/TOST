# Cross-platform architecture

- [ ] Extract platform-neutral game, archive, logging, and configuration logic into `Core`.
  - [x] Move managed-game discovery, removal archives, and guarded restoration into the shared core.
- [x] Define an integration-provider interface shared by OpenSteamTool and SLSsteam.
- [ ] Move Windows-specific OpenSteamTool behavior into `TOST.Windows`.
- [ ] Keep the existing WinForms application operational until Avalonia reaches feature parity.

# Linux implementation

- [x] Create an initial Linux CLI before committing to a cross-platform GUI framework.
- [x] Add Linux Steam discovery for native Steam, Flatpak, SteamOS, and Bazzite installations.
- [x] Add SLSsteam installation and required-file status detection.
- [x] Add bounded, read-only SLSsteam configuration inspection and explicit backups.
- [x] Add guarded SafeMode editing with dry-run preview, mandatory backup, and atomic replacement.
- [x] Add bounded native/Flatpak log viewing and installed-binary fingerprints.
- [x] Add validated remote SLSsteam release and published-checksum lookup.
- [x] Add allowlisted boolean configuration editing with preview and atomic backup-first writes.
- [x] Add configuration backup discovery, validation, preview, and safe restoration.
- [x] Detect native and Flatpak SLSsteam installations independently.
- [x] Add preview-first SLSsteam library removal, recovery discovery, and guarded restoration.
- [x] Configure and safely remove unmodified TOST-managed native wrappers and Flatpak overrides.
- [x] Archive, list, and restore removed native launch wrappers and Flatpak overrides.
- [x] Detect native wrappers, desktop/fish launch hooks, and Flatpak override files without executing them.
- [x] Download pinned upstream portable releases, verify GitHub SHA-256 digests, and safely extract only required libraries.
- [ ] Preserve upstream licenses, attribution, and source links in the Linux package.
- [x] Add import support for user-provided Lua and manifest files.
  - [x] Add the preview-first `import` command to `CLI/Linux`.
  - [x] Safely parse `addappid` and `setManifestid` declarations without executing Lua.
  - [x] Parse and validate depot keys and app tokens; keep DLC classification explicit.
  - [x] Parse the contents of `appmanifest_*.acf` files.
- [x] Apply supported AppID, token, and manifest override metadata to SLSsteam's official `config.yaml` schema with preview, backup, and atomic writes.
- [ ] Add explicit parent-app/DLC mapping before writing `DlcData`; Lua alone does not identify this relationship.
- [x] Register validated depot keys through Steam's Linux `config/config.vdf` with preview, conflict rejection, backup, and atomic writes.
- [x] Validate imported paths, identifiers, duplicate destinations, file sizes, symlinks, and manifest filenames.
- [x] Route Linux Lua, depot manifest, and app manifest files to their correct Steam directories.
- [x] Reject duplicate destinations and roll back newly copied files after partial import failures.
- [x] Never claim that appearing in the library guarantees download, launch, multiplayer, or anti-cheat compatibility.
- [x] Add safe Steam restart handling without terminating unrelated Wine/Proton processes.
- [ ] Add compatibility warnings when Steam client updates invalidate the installed SLSsteam version.

# Avalonia UI migration

- [ ] Stabilize and test the shared core and Linux/SLSsteam backend before starting the UI migration.
- [x] Create a shared `Desktop` Avalonia project targeting Windows and Linux.
- [x] Add the initial dark navigation shell and cross-platform integration overview.
- [x] Connect Avalonia Linux import preview/apply and verified SLSsteam installation screens.
- [x] Connect Avalonia Linux recovery archives and bounded diagnostics/log screens.
- [x] Connect guarded native/Flatpak launch-hook configuration and archival controls.
- [ ] Rebuild the floating icon, menus, settings, dialogs, Game Manager, and recovery UI in Avalonia.
  - [x] Add the Linux Game Manager with native/Flatpak selection and one-click archive/restore actions.
  - [x] Add local Avalonia preferences for the default Steam target, updates, and diagnostic limits.
  - [x] Add a reusable dark confirmation dialog for Avalonia file-changing actions.
- [ ] Add platform services for startup registration, tray/floating-window behavior, opening folders, and restarting Steam.
  - [x] Add guarded native folder-opening actions for TOST data and detected Steam roots.
  - [x] Restart native or Flatpak Steam through its normal shutdown command without killing processes.
  - [x] Add a cross-platform tray menu with saved close-to-tray behavior and explicit exit.
  - [x] Add a circular draggable Avalonia floating icon with saved visibility and always-on-top behavior.
  - [x] Connect tray and floating-icon menus to migrated pages, hide, and explicit exit actions.
  - [x] Add guarded Linux desktop autostart for packaged executables without overwriting unmanaged entries.
- [ ] Add drag-and-drop import support on both Windows and Linux.
  - [x] Connect drag-and-drop selection to the migrated Linux importer.
- [ ] Match the existing TOST dark styling without depending on native WinForms rendering.
- [ ] Verify feature parity with the WinForms application before retiring it.
- [ ] Remove the WinForms frontend only after Windows and Linux packages pass release testing.

# Testing and packaging

- [ ] Test native Steam and Flatpak Steam separately on a standard desktop distribution.
- [ ] Test SteamOS/Bazzite filesystem and sandbox behavior without writing to immutable system paths.
- [x] Add dependency-free checks for Lua parsing, path routing, configuration backup, and restoration.
- [x] Add integration checks using temporary fake Steam and SLSsteam installations; never modify a developer's real Steam installation.
- [ ] Add tests for YAML generation and failure-injection rollback paths.
- [x] Package Linux CLI builds as portable, AppImage, and Arch Linux artifacts with SHA-256 checksums.
- [x] Replace legacy Windows and Linux package artwork with the current TOST logo.
- [x] Configure the Linux CLI for self-contained single-file `linux-x64` publishing.
- [x] Package the Avalonia application for Linux alongside the optional CLI.
- [ ] Switch the Windows release package from WinForms to Avalonia after feature-parity testing.
- [ ] Evaluate Flatpak after the Avalonia application is stable; AppImage packaging is connected.


# Online-fix investigation

- [ ] Define the legal, licensing, security, and technical scope before implementation.

# Third-party launcher workarounds

- [ ] Rockstar (`socialclub64.dll`)
- [ ] EA (`Activation64.dll`)
- [ ] Ubisoft (`uplay_r1_loader64.dll`)
