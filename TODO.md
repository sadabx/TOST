# Third-party launcher workarounds

- [ ] Rockstar (`socialclub64.dll`)
- [ ] EA (`Activation64.dll`)
- [ ] Ubisoft (`uplay_r1_loader64.dll`)

# Cross-platform architecture

- [ ] Extract platform-neutral game, archive, logging, and configuration logic into `TOST.Core`.
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
  - [x] Add the preview-first `import` command to `TOST.Linux`.
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
- [x] Add dependency-free checks for Lua parsing, path routing, configuration backup, and restoration.
- [x] Add integration checks using temporary fake Steam and SLSsteam installations; never modify a developer's real Steam installation.
- [ ] Add tests for YAML generation and failure-injection rollback paths.
- [x] Package Linux CLI builds as portable, AppImage, and Arch Linux artifacts with SHA-256 checksums.
- [x] Replace legacy Windows and Linux package artwork with the current TOST logo.
- [x] Configure the Linux CLI for self-contained single-file `linux-x64` publishing.
- [ ] Package the completed Avalonia application for Windows and Linux.
- [ ] Evaluate AppImage and Flatpak after the Avalonia application is stable.

# Online-fix investigation

- [ ] Define the legal, licensing, security, and technical scope before implementation.
