# Cross-platform architecture

- [ ] Extract platform-neutral game, archive, logging, and configuration logic into `Core`.
  - [x] Move managed-game discovery, removal archives, and guarded restoration into the shared core.
- [x] Define an integration-provider interface shared by OpenSteamTool and SLSsteam.
- [x] Move Windows-specific OpenSteamTool behavior behind the shared desktop frontend.
- [x] Use the same compact Avalonia floating UI on Windows/OpenSteamTool and Linux/SLSsteam.

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

# Linux Avalonia floating UI

- [x] Stabilize the shared core enough to connect both platform backends to Avalonia.
- [x] Create the Linux `Desktop` Avalonia floating frontend.
- [x] Add the initial dark navigation shell and cross-platform integration overview.
- [x] Connect Avalonia Linux import preview/apply and verified SLSsteam installation screens.
- [x] Connect Avalonia Linux recovery archives and bounded diagnostics/log screens.
- [x] Connect guarded native/Flatpak launch-hook configuration and archival controls.
- [x] Rebuild the floating icon, menus, settings, dialogs, Game Manager, and recovery UI in Avalonia.
  - [x] Add the Linux Game Manager with native/Flatpak selection and one-click archive/restore actions.
  - [x] Add local Avalonia preferences for the default Steam target, updates, and diagnostic limits.
  - [x] Add a reusable dark confirmation dialog for Avalonia file-changing actions.
- [x] Add platform services for startup registration, tray/floating-window behavior, opening folders, and restarting Steam.
  - [x] Add guarded native folder-opening actions for TOST data and detected Steam roots.
  - [x] Restart native or Flatpak Steam through its normal shutdown command without killing processes.
  - [x] Add a cross-platform tray menu with saved close-to-tray behavior and explicit exit.
  - [x] Add a circular draggable Avalonia floating icon with saved visibility and always-on-top behavior.
  - [x] Keep Linux as a floating-menu application; open only task-specific windows on demand.
  - [x] Connect tray and floating-icon menus to migrated pages, hide, and explicit exit actions.
  - [x] Add guarded Linux desktop autostart for packaged executables without overwriting unmanaged entries.
- [x] Add drag-and-drop import support to both the Windows WinForms and Linux Avalonia frontends.
  - [x] Connect drag-and-drop selection to the migrated Linux importer.
- [x] Match the existing compact TOST dark styling without depending on native WinForms rendering.
- [ ] Verify Linux floating-menu behavior on a real Linux desktop and Windows behavior on a clean Windows account.
- [x] Publish the Avalonia frontend for Windows/OpenSteamTool releases; retain WinForms source only as a reference.

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
- [x] Switch Windows release packaging to the shared Avalonia/OpenSteamTool application.
- [ ] Evaluate Flatpak after the Avalonia application is stable; AppImage packaging is connected.


# Online-fix investigation

- [ ] Define the legal, licensing, security, and technical scope before implementation.

# Third-party launcher workarounds

- [x] Rockstar (`socialclub64.dll`)
- [x] EA (`Activation64.dll`)
- [x] Ubisoft (`uplay_r1_loader64.dll`)
