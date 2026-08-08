using Trionine.TOST.Core.Steam;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Imports;
using System.Text.Json;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("TOST Linux CLI must be run on Linux.");
    return 2;
}

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
return command switch
{
    "status" => ShowStatus(),
    "config" => InspectConfig(),
    "backup-config" => BackupConfig(),
    "set-safe-mode" => SetSafeMode(args.Skip(1).ToArray()),
    "set" => SetBooleanSetting(args.Skip(1).ToArray()),
    "logs" => ShowLogs(args.Skip(1).ToArray()),
    "check-updates" => CheckUpdates(),
    "install-slssteam" => InstallSlsSteam(args.Skip(1).ToArray()),
    "configure-launch" => ConfigureLaunch(args.Skip(1).ToArray(), remove: false),
    "remove-launch" => ConfigureLaunch(args.Skip(1).ToArray(), remove: true),
    "launch-recovery" => ShowLaunchRecovery(),
    "restore-launch" => RestoreLaunch(args.Skip(1).ToArray()),
    "backups" => ShowBackups(),
    "restore-config" => RestoreConfig(args.Skip(1).ToArray()),
    "remove-slssteam" => RemoveSlsSteam(args.Skip(1).ToArray()),
    "slssteam-recovery" => ShowSlsSteamRecovery(),
    "restore-slssteam" => RestoreSlsSteam(args.Skip(1).ToArray()),
    "inspect-import" => InspectImport(args.Skip(1).ToArray()),
    "import" => ImportFiles(args.Skip(1).ToArray()),
    "help" or "--help" or "-h" => ShowHelp(),
    _ => UnknownCommand(command)
};

static int ShowStatus()
{
    var installations = LinuxSteamDiscovery.FindInstallations();
    Console.WriteLine("TOST Linux status");
    if (installations.Count == 0)
    {
        Console.WriteLine("No Steam installation was found.");
        Console.WriteLine("Set STEAM_DIR to use a custom Steam root.");
    }
    else
    {
        foreach (var installation in installations)
        {
            Console.WriteLine();
            Console.WriteLine($"Steam ({installation.Kind}): {installation.RootPath}");
            Console.WriteLine($"  steamapps: {(installation.HasSteamApps ? "found" : "missing")}");
            Console.WriteLine($"  config:    {(installation.HasConfig ? "found" : "missing")}");
        }
    }

    Console.WriteLine();
    var nativeStatus = GetProviderStatus(SlsSteamPaths.ForCurrentUser());
    PrintProviderStatus("Native", nativeStatus);
    var flatpakStatus = GetProviderStatus(SlsSteamPaths.ForFlatpakUser());
    if (flatpakStatus.Health != Trionine.TOST.Core.Integrations.IntegrationHealth.NotInstalled ||
        installations.Any(installation => installation.Kind == SteamInstallationKind.Flatpak))
    {
        Console.WriteLine();
        PrintProviderStatus("Flatpak", flatpakStatus);
    }

    var hooks = new SlsSteamLaunchHookService().FindHooks();
    if (hooks.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Detected SLSsteam launch hooks:");
        foreach (var hook in hooks) Console.WriteLine($"  {hook.Kind}: {hook.Path}");
    }

    Console.WriteLine();
    Console.WriteLine("Use install-slssteam to preview the checksum-pinned official installation.");
    return installations.Count == 0 ? 1 : 0;
}

static int ShowHelp()
{
    Console.WriteLine("TOST Linux CLI");
    Console.WriteLine("Usage: tost [status|config|backups|set|logs|check-updates|remove-slssteam|slssteam-recovery|restore-slssteam|inspect-import|help]");
    Console.WriteLine("  status  Detect Steam installations without changing files.");
    Console.WriteLine("  config  Inspect the SLSsteam configuration without changing it.");
    Console.WriteLine("  backup-config  Copy the configuration into TOST's recovery directory.");
    Console.WriteLine("  backups  List recoverable SLSsteam configuration backups.");
    Console.WriteLine("  restore-config <filename> [--apply]  Preview or restore a configuration backup.");
    Console.WriteLine("  set-safe-mode <true|false> [--apply]  Preview or safely apply SafeMode.");
    Console.WriteLine("  set <setting> <true|false> [--apply]  Preview or safely apply an allowed boolean setting.");
    Console.WriteLine("  logs [lines]  Show the latest bounded SLSsteam log tail (default: 50).");
    Console.WriteLine("  check-updates  Query SLSsteam's official latest GitHub release.");
    Console.WriteLine("  install-slssteam [--flatpak] [--apply]  Preview or install a verified official release.");
    Console.WriteLine("  configure-launch [--flatpak] [--apply]  Preview or configure SLSsteam launch injection.");
    Console.WriteLine("  remove-launch [--flatpak] [--apply]  Preview or remove unmodified TOST-managed hooks.");
    Console.WriteLine("  launch-recovery  List recoverable native and Flatpak launch hooks.");
    Console.WriteLine("  restore-launch <archive-id> [--flatpak] [--apply]  Preview or restore launch hooks.");
    Console.WriteLine("  remove-slssteam [--flatpak] [--apply]  Preview or archive known SLSsteam libraries.");
    Console.WriteLine("  slssteam-recovery  List recoverable SLSsteam library archives.");
    Console.WriteLine("  restore-slssteam <archive-id> [--flatpak] [--apply]  Preview or restore an archive.");
    Console.WriteLine("  inspect-import <files...>  Validate supported Lua and manifest inputs without copying them.");
    Console.WriteLine("  import [--flatpak] [--apply] <files...>  Preview or import new files without overwriting.");
    return 0;
}

static int InspectConfig()
{
    var paths = ResolveSlsSteamPaths();
    try
    {
        var inspection = new SlsSteamConfigService().Inspect(paths.ConfigPath);
        Console.WriteLine($"SLSsteam configuration: {inspection.Path}");
        Console.WriteLine($"Size: {inspection.SizeBytes} bytes");
        Console.WriteLine($"Top-level keys: {(inspection.TopLevelKeys.Count == 0 ? "none" : string.Join(", ", inspection.TopLevelKeys))}");
        if (inspection.Warnings.Count == 0)
        {
            Console.WriteLine("Structure check: OK");
            return 0;
        }

        Console.WriteLine("Structure warnings:");
        foreach (var warning in inspection.Warnings)
        {
            Console.WriteLine($"  - {warning}");
        }

        return 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not inspect SLSsteam configuration: {ex.Message}");
        return 1;
    }
}

static int BackupConfig()
{
    var paths = ResolveSlsSteamPaths();
    try
    {
        var backup = new SlsSteamConfigService().CreateBackup(paths.ConfigPath, GetSlsSteamBackupDirectory());
        Console.WriteLine($"Backed up {backup.SizeBytes} bytes to:");
        Console.WriteLine(backup.BackupPath);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not back up SLSsteam configuration: {ex.Message}");
        return 1;
    }
}

static int SetSafeMode(string[] arguments)
    => SetBooleanSetting(["SafeMode", .. arguments]);

static int SetBooleanSetting(string[] arguments)
{
    if (arguments.Length is < 2 or > 3 ||
        !bool.TryParse(arguments[1], out var enabled) ||
        (arguments.Length == 3 && !arguments[2].Equals("--apply", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Usage: tost set <setting> <true|false> [--apply]");
        Console.Error.WriteLine($"Allowed settings: {string.Join(", ", SlsSteamConfigService.SupportedBooleanSettings.Order())}");
        return 2;
    }

    var setting = arguments[0];
    var paths = ResolveSlsSteamPaths();
    var service = new SlsSteamConfigService();
    try
    {
        var preview = service.PreviewBooleanSetting(paths.ConfigPath, setting, enabled);
        var oldValue = preview.PreviousValue.HasValue
            ? preview.PreviousValue.Value.ToString().ToLowerInvariant()
            : "not set";
        Console.WriteLine($"{preview.Setting}: {oldValue} -> {enabled.ToString().ToLowerInvariant()}");
        if (!preview.ChangesFile)
        {
            Console.WriteLine("No change is required.");
            return 0;
        }

        if (arguments.Length == 2)
        {
            Console.WriteLine("Preview only. Add --apply to create a backup and update the file.");
            return 0;
        }

        var result = service.SetBooleanSetting(paths.ConfigPath, setting, enabled, GetSlsSteamBackupDirectory());
        Console.WriteLine($"Updated: {result.ConfigPath}");
        Console.WriteLine($"Backup: {result.Backup!.BackupPath}");
        Console.WriteLine("Restart Steam and confirm it starts correctly before leaving SteamOS Desktop Mode.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not update SLSsteam setting: {ex.Message}");
        return 1;
    }
}

static string GetSlsSteamBackupDirectory()
{
    var dataHome = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(dataHome))
    {
        dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    return Path.Combine(dataHome, "TOST", "backups", "SLSsteam");
}

static int InstallSlsSteam(string[] arguments)
{
    var flatpak = arguments.Any(argument => argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase));
    var apply = arguments.Any(argument => argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));
    if (arguments.Any(argument => !argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase) &&
                                  !argument.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Usage: tost install-slssteam [--flatpak] [--apply]");
        return 2;
    }

    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var release = new SlsSteamReleaseService(client).GetLatestAsync().GetAwaiter().GetResult();
        var paths = flatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
        var installer = new SlsSteamInstallerService(client);
        var preview = installer.Preview(release, paths);
        Console.WriteLine($"Pinned release: {preview.Tag}");
        Console.WriteLine($"Asset: {preview.Asset.Name} ({preview.Asset.SizeBytes} bytes)");
        Console.WriteLine($"SHA-256: {preview.Asset.Sha256}");
        foreach (var destination in preview.Destinations) Console.WriteLine($"  -> {destination}");
        if (!preview.CanInstall)
        {
            Console.Error.WriteLine(preview.BlockReason);
            return 1;
        }
        if (!apply)
        {
            Console.WriteLine("Preview only. Add --apply to download, verify, and install.");
            return 0;
        }
        var result = installer.InstallAsync(release, paths).GetAwaiter().GetResult();
        Console.WriteLine($"Installed SLSsteam {result.Tag}:");
        foreach (var file in result.InstalledFiles) Console.WriteLine($"  {file}");
        Console.WriteLine("Libraries are installed but Steam launch injection must still be configured.");
        return 0;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or
                                  UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not install SLSsteam: {ex.Message}");
        return 1;
    }
}

static int ConfigureLaunch(string[] arguments, bool remove)
{
    var flatpak = arguments.Any(argument => argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase));
    var apply = arguments.Any(argument => argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));
    if (arguments.Any(argument => !argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase) &&
                                  !argument.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"Usage: tost {(remove ? "remove-launch" : "configure-launch")} [--flatpak] [--apply]");
        return 2;
    }
    try
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = flatpak ? SlsSteamPaths.ForFlatpakUser(home) : SlsSteamPaths.ForCurrentUser(home);
        var service = new SlsSteamLaunchConfigurationService();
        var plan = flatpak
            ? service.PreviewFlatpak(paths, home)
            : service.PreviewNative(paths, home, FindSteamExecutables(paths.DataDirectory));
        Console.WriteLine($"{plan.Kind} launch injection:");
        foreach (var item in plan.Items) Console.WriteLine($"  {item.State}: {item.Path}{(item.Message is null ? "" : $" ({item.Message})")}");
        if (!plan.CanApply)
        {
            Console.Error.WriteLine("Launch configuration contains conflicts; no files were changed.");
            return 1;
        }
        if (!apply)
        {
            Console.WriteLine($"Preview only. Add --apply to {(remove ? "remove" : "configure")} these hooks.");
            return 0;
        }
        if (remove)
        {
            var archived = service.ArchiveManaged(plan, GetLaunchHookRecoveryDirectory());
            Console.WriteLine($"Archived {archived.Paths.Count} launch-hook files as {archived.ArchiveId}.");
        }
        else
        {
            var changed = service.Apply(plan);
            Console.WriteLine($"Created {changed.Count} launch-hook files.");
        }
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine($"Could not update launch injection: {ex.Message}");
        return 1;
    }
}

static int ShowLaunchRecovery()
{
    var entries = new SlsSteamLaunchConfigurationService().FindRecoveryEntries(GetLaunchHookRecoveryDirectory());
    if (entries.Count == 0) { Console.WriteLine("No launch-hook recovery archives found."); return 0; }
    foreach (var entry in entries)
        Console.WriteLine($"{entry.ArchiveId}  {entry.Kind}  {entry.RemovedUtc:u}  {entry.Paths.Count} files");
    return 0;
}

static int RestoreLaunch(string[] arguments)
{
    var flatpak = arguments.Any(argument => argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase));
    var apply = arguments.Any(argument => argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));
    var ids = arguments.Where(argument => !argument.StartsWith('-')).ToArray();
    if (ids.Length != 1 || arguments.Any(argument => argument.StartsWith('-') &&
        !argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase) && !argument.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Usage: tost restore-launch <archive-id> [--flatpak] [--apply]");
        return 2;
    }
    try
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = flatpak ? SlsSteamPaths.ForFlatpakUser(home) : SlsSteamPaths.ForCurrentUser(home);
        var service = new SlsSteamLaunchConfigurationService();
        var plan = flatpak ? service.PreviewFlatpak(paths, home) : service.PreviewNative(paths, home, FindSteamExecutables(paths.DataDirectory));
        var entry = service.FindRecoveryEntries(GetLaunchHookRecoveryDirectory()).SingleOrDefault(item => item.ArchiveId == ids[0])
            ?? throw new InvalidDataException("Launch-hook recovery archive was not found.");
        Console.WriteLine($"Restore {entry.Kind} archive {entry.ArchiveId} ({entry.Paths.Count} files).");
        foreach (var path in entry.Paths) Console.WriteLine($"  -> {path}");
        if (!apply) { Console.WriteLine("Preview only. Add --apply to restore."); return 0; }
        var restored = service.Restore(plan, GetLaunchHookRecoveryDirectory(), entry.ArchiveId);
        Console.WriteLine($"Restored {restored.Count} launch-hook files.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Could not restore launch hooks: {ex.Message}");
        return 1;
    }
}

static IReadOnlyDictionary<string, string> FindSteamExecutables(string slsDataDirectory)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    var wrapperDirectory = Path.Combine(Path.GetFullPath(slsDataDirectory), "path");
    var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var name in new[] { "steam", "steam-runtime", "steam-native" })
    {
        foreach (var directory in pathEntries)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, name));
            if (candidate.StartsWith(wrapperDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(candidate)) continue;
            result[name] = candidate;
            break;
        }
    }
    return result;
}

static int ShowLogs(string[] arguments)
{
    if (arguments.Length > 1 ||
        (arguments.Length == 1 && !int.TryParse(arguments[0], out _)))
    {
        Console.Error.WriteLine("Usage: tost logs [lines]");
        return 2;
    }

    var lineCount = arguments.Length == 0 ? 50 : int.Parse(arguments[0]);
    if (lineCount is < 1 or > SlsSteamDiagnosticsService.MaximumTailLines)
    {
        Console.Error.WriteLine($"Line count must be between 1 and {SlsSteamDiagnosticsService.MaximumTailLines}.");
        return 2;
    }

    try
    {
        var nativePaths = SlsSteamPaths.ForCurrentUser();
        var flatpakPaths = SlsSteamPaths.ForFlatpakUser();
        var paths = ResolveSlsSteamPaths();
        var diagnostics = new SlsSteamDiagnosticsService();
        var binary = diagnostics.InspectBinary(paths.MainLibraryPath);
        if (binary is not null)
        {
            Console.WriteLine($"SLSsteam binary: {binary.Path}");
            Console.WriteLine($"Modified: {binary.LastWriteUtc:u}");
            Console.WriteLine($"SHA-256: {binary.Sha256}");
            Console.WriteLine("Installed semantic version: unavailable (upstream does not install a version marker)");
            Console.WriteLine();
        }

        var log = diagnostics.ReadLatestLog(nativePaths.LogPaths.Concat(flatpakPaths.LogPaths).ToArray(), lineCount);
        if (log is null)
        {
            Console.WriteLine("No SLSsteam log was found.");
            return 1;
        }

        Console.WriteLine($"Log: {log.Path}");
        if (log.Truncated)
        {
            Console.WriteLine("[Earlier log content omitted]");
        }

        foreach (var line in log.Lines)
        {
            Console.WriteLine(line);
        }

        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"Could not read SLSsteam diagnostics: {ex.Message}");
        return 1;
    }
}

static int CheckUpdates()
{
    try
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var release = new SlsSteamReleaseService(httpClient).GetLatestAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Latest SLSsteam release: {release.Tag}");
        Console.WriteLine($"Published: {release.PublishedAt:u}");
        Console.WriteLine($"Release: {release.ReleaseUri}");
        foreach (var asset in release.Assets)
        {
            Console.WriteLine();
            Console.WriteLine($"Asset: {asset.Name} ({asset.SizeBytes} bytes)");
            Console.WriteLine($"SHA-256: {asset.Sha256 ?? "not published"}");
            Console.WriteLine($"Download: {asset.DownloadUri}");
        }

        Console.WriteLine();
        Console.WriteLine("Local comparison: unknown; SLSsteam does not install its release tag.");
        Console.WriteLine("TOST will not infer a version from file timestamps.");
        return 0;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
    {
        Console.Error.WriteLine($"Could not check SLSsteam releases: {ex.Message}");
        return 1;
    }
}

static int ShowBackups()
{
    try
    {
        var backups = new SlsSteamConfigService().FindBackups(GetSlsSteamBackupDirectory());
        if (backups.Count == 0)
        {
            Console.WriteLine("No SLSsteam configuration backups were found.");
            return 0;
        }

        foreach (var backup in backups)
        {
            Console.WriteLine($"{backup.FileName}  {backup.SizeBytes} bytes  {backup.LastWriteUtc:u}");
        }

        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not list SLSsteam backups: {ex.Message}");
        return 1;
    }
}

static int RestoreConfig(string[] arguments)
{
    if (arguments.Length is < 1 or > 2 ||
        (arguments.Length == 2 && !arguments[1].Equals("--apply", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Usage: tost restore-config <filename> [--apply]");
        return 2;
    }

    var service = new SlsSteamConfigService();
    var backupDirectory = GetSlsSteamBackupDirectory();
    try
    {
        var inspection = service.InspectBackup(backupDirectory, arguments[0]);
        Console.WriteLine($"Backup: {Path.GetFileName(inspection.Path)}");
        Console.WriteLine($"Size: {inspection.SizeBytes} bytes");
        Console.WriteLine($"Top-level keys: {string.Join(", ", inspection.TopLevelKeys)}");
        if (inspection.Warnings.Count > 0)
        {
            Console.Error.WriteLine("The backup has structural warnings and will not be restored:");
            foreach (var warning in inspection.Warnings)
            {
                Console.Error.WriteLine($"  - {warning}");
            }
            return 1;
        }

        if (arguments.Length == 1)
        {
            Console.WriteLine("Preview only. Add --apply to back up the current config and restore this file.");
            return 0;
        }

        var result = service.RestoreBackup(
            ResolveSlsSteamPaths().ConfigPath,
            backupDirectory,
            arguments[0]);
        if (!result.Changed)
        {
            Console.WriteLine("The selected backup already matches the current configuration.");
            return 0;
        }

        Console.WriteLine($"Restored: {result.ConfigPath}");
        Console.WriteLine($"Previous configuration backed up to: {result.Backup!.BackupPath}");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not restore SLSsteam configuration: {ex.Message}");
        return 1;
    }
}

static int RemoveSlsSteam(string[] arguments)
{
    if (!TryParseRecoveryFlags(arguments, allowArchiveId: false, out _, out var flatpak, out var apply))
    {
        Console.Error.WriteLine("Usage: tost remove-slssteam [--flatpak] [--apply]");
        return 2;
    }

    var paths = flatpak ? SlsSteamPaths.ForFlatpakUser() : ResolveSlsSteamPaths();
    var kind = flatpak || IsFlatpakPaths(paths) ? "Flatpak" : "Native";
    var service = new SlsSteamRecoveryService();
    try
    {
        var preview = service.PreviewRemoval(paths, kind);
        if (!preview.HasFiles)
        {
            Console.WriteLine($"No managed SLSsteam libraries were found for {kind}.");
            return 0;
        }

        Console.WriteLine($"SLSsteam {kind} libraries to archive:");
        foreach (var file in preview.Files) Console.WriteLine($"  {file}");
        Console.WriteLine("The SLSsteam configuration will be preserved.");
        if (!apply)
        {
            Console.WriteLine("Preview only. Add --apply to archive these files.");
            return 0;
        }

        var result = service.Remove(paths, kind, GetSlsSteamRecoveryDirectory());
        Console.WriteLine(result.Message);
        Console.WriteLine($"Recovery archive: {result.ArchiveId}");
        Console.WriteLine("Existing launch wrappers or Flatpak overrides are not removed in this phase.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Could not archive SLSsteam: {ex.Message}");
        return 1;
    }
}

static int ShowSlsSteamRecovery()
{
    try
    {
        var entries = new SlsSteamRecoveryService().FindRecoveryEntries(GetSlsSteamRecoveryDirectory());
        if (entries.Count == 0)
        {
            Console.WriteLine("No SLSsteam recovery archives were found.");
            return 0;
        }
        foreach (var entry in entries)
        {
            Console.WriteLine($"{entry.ArchiveId}  {entry.InstallationKind}  {entry.RemovedUtc:u}  {string.Join(", ", entry.FileNames)}");
        }
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not list SLSsteam recovery archives: {ex.Message}");
        return 1;
    }
}

static int RestoreSlsSteam(string[] arguments)
{
    if (!TryParseRecoveryFlags(arguments, allowArchiveId: true, out var archiveId, out var flatpak, out var apply))
    {
        Console.Error.WriteLine("Usage: tost restore-slssteam <archive-id> [--flatpak] [--apply]");
        return 2;
    }

    try
    {
        var service = new SlsSteamRecoveryService();
        var entry = service.GetRecoveryEntry(GetSlsSteamRecoveryDirectory(), archiveId!);
        var archiveIsFlatpak = entry.InstallationKind.Equals("Flatpak", StringComparison.OrdinalIgnoreCase);
        if (flatpak != archiveIsFlatpak && flatpak)
        {
            Console.Error.WriteLine($"Archive {archiveId} belongs to {entry.InstallationKind}, not Flatpak.");
            return 2;
        }

        var paths = archiveIsFlatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
        Console.WriteLine($"Archive: {entry.ArchiveId} ({entry.InstallationKind})");
        Console.WriteLine($"Files: {string.Join(", ", entry.FileNames)}");
        Console.WriteLine($"Destination: {paths.DataDirectory}");
        if (!apply)
        {
            Console.WriteLine("Preview only. Add --apply to restore without overwriting existing files.");
            return 0;
        }

        var result = service.Restore(
            paths,
            entry.InstallationKind,
            GetSlsSteamRecoveryDirectory(),
            archiveId!);
        Console.WriteLine(result.Message);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
    {
        Console.Error.WriteLine($"Could not restore SLSsteam: {ex.Message}");
        return 1;
    }
}

static bool TryParseRecoveryFlags(
    string[] arguments,
    bool allowArchiveId,
    out string? archiveId,
    out bool flatpak,
    out bool apply)
{
    archiveId = null;
    flatpak = false;
    apply = false;
    foreach (var argument in arguments)
    {
        if (argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase) && !flatpak) flatpak = true;
        else if (argument.Equals("--apply", StringComparison.OrdinalIgnoreCase) && !apply) apply = true;
        else if (allowArchiveId && archiveId is null && !argument.StartsWith('-')) archiveId = argument;
        else return false;
    }
    return allowArchiveId ? archiveId is not null : archiveId is null;
}

static string GetSlsSteamRecoveryDirectory() =>
    Path.Combine(Path.GetDirectoryName(GetSlsSteamBackupDirectory())!, "removed-slssteam");

static string GetSteamConfigBackupDirectory() =>
    Path.Combine(Path.GetDirectoryName(GetSlsSteamBackupDirectory())!, "Steam-config");

static string GetLaunchHookRecoveryDirectory() =>
    Path.Combine(Path.GetDirectoryName(GetSlsSteamBackupDirectory())!, "launch-hooks");

static bool IsFlatpakPaths(SlsSteamPaths paths) =>
    paths.DataDirectory.Contains(
        $"{Path.DirectorySeparatorChar}.var{Path.DirectorySeparatorChar}app{Path.DirectorySeparatorChar}com.valvesoftware.Steam{Path.DirectorySeparatorChar}",
        StringComparison.Ordinal);

static SlsSteamPaths ResolveSlsSteamPaths()
{
    var native = SlsSteamPaths.ForCurrentUser();
    if (GetProviderStatus(native).Health != Trionine.TOST.Core.Integrations.IntegrationHealth.NotInstalled)
    {
        return native;
    }

    var flatpak = SlsSteamPaths.ForFlatpakUser();
    return GetProviderStatus(flatpak).Health != Trionine.TOST.Core.Integrations.IntegrationHealth.NotInstalled
        ? flatpak
        : native;
}

static Trionine.TOST.Core.Integrations.IntegrationStatus GetProviderStatus(SlsSteamPaths paths) =>
    new SlsSteamProvider(paths).GetStatusAsync().GetAwaiter().GetResult();

static void PrintProviderStatus(
    string installationKind,
    Trionine.TOST.Core.Integrations.IntegrationStatus status)
{
    Console.WriteLine($"{status.DisplayName} ({installationKind}): {status.Health}");
    Console.WriteLine($"  {status.Summary}");
    foreach (var component in status.Components)
    {
        Console.WriteLine($"  {(component.Exists ? "found  " : "missing")} {component.Name}: {component.Path}");
    }
}

static int InspectImport(string[] paths)
{
    if (paths.Length == 0)
    {
        Console.Error.WriteLine("Usage: tost inspect-import <files...>");
        return 2;
    }

    var inspector = new SteamImportInspector();
    var inspections = new List<SteamImportInspection>();
    var failures = 0;
    foreach (var path in paths)
    {
        try
        {
            var result = inspector.Inspect(path);
            inspections.Add(result);
            Console.WriteLine($"{result.Kind}: {result.Path} ({result.SizeBytes} bytes)");
            if (result.AppIds.Count > 0) Console.WriteLine($"  App IDs: {string.Join(", ", result.AppIds)}");
            if (result.DepotIds.Count > 0) Console.WriteLine($"  Depot IDs: {string.Join(", ", result.DepotIds)}");
            if (result.ManifestIds.Count > 0) Console.WriteLine($"  Manifest IDs: {string.Join(", ", result.ManifestIds)}");
            foreach (var declaration in result.AppDeclarations.Where(item => item.DepotKey is not null))
                Console.WriteLine($"  Depot key: {declaration.AppId} ({declaration.DepotKey!.Length} hex characters)");
            foreach (var token in result.Tokens)
                Console.WriteLine($"  App token: {token.AppId} ({token.Token.Length} digits)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            failures++;
            Console.Error.WriteLine($"Rejected {path}: {ex.Message}");
        }
    }

    if (inspections.Any(item => item.Kind == SteamImportKind.Lua))
    {
        var conversion = new SlsSteamImportConversionService().CreatePlan(inspections);
        Console.WriteLine($"SLSsteam preview: {conversion.AdditionalApps.Count} apps, {conversion.AppTokens.Count} tokens, {conversion.ManifestIds.Count} manifest overrides.");
        foreach (var warning in conversion.Warnings) Console.WriteLine($"  Warning: {warning}");
    }
    Console.WriteLine("Preview only; no files or configurations were changed and no Lua was executed.");
    return failures == 0 ? 0 : 1;
}

static int ImportFiles(string[] arguments)
{
    var flatpak = arguments.Any(argument => argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase));
    var apply = arguments.Any(argument => argument.Equals("--apply", StringComparison.OrdinalIgnoreCase));
    var unknownFlags = arguments.Where(argument => argument.StartsWith('-') &&
        !argument.Equals("--flatpak", StringComparison.OrdinalIgnoreCase) &&
        !argument.Equals("--apply", StringComparison.OrdinalIgnoreCase)).ToArray();
    var inputs = arguments.Where(argument => !argument.StartsWith('-')).ToArray();
    if (inputs.Length == 0 || unknownFlags.Length > 0)
    {
        Console.Error.WriteLine("Usage: tost import [--flatpak] [--apply] <files...>");
        return 2;
    }

    try
    {
        var installations = LinuxSteamDiscovery.FindInstallations();
        var expectedKind = flatpak ? SteamInstallationKind.Flatpak : SteamInstallationKind.Native;
        var steam = installations.FirstOrDefault(installation => installation.Kind == expectedKind)
            ?? throw new DirectoryNotFoundException($"No {expectedKind} Steam installation was found.");
        var service = new SteamImportService();
        var plan = service.CreatePlan(steam, inputs);
        var conversion = new SlsSteamImportConversionService().CreatePlan(plan.Items.Select(item => item.Inspection));
        var slsPaths = flatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
        SlsSteamImportConfigPreview? configPreview = null;
        if (conversion.AdditionalApps.Count > 0)
            configPreview = new SlsSteamImportConfigService().Preview(slsPaths.ConfigPath, conversion);
        SteamDepotKeyPreview? depotKeyPreview = null;
        var steamConfigPath = Path.Combine(steam.ConfigPath, "config.vdf");
        if (conversion.DepotKeys.Count > 0)
            depotKeyPreview = new SteamDepotKeyService().Preview(steamConfigPath, conversion.DepotKeys);
        Console.WriteLine($"Steam destination: {steam.RootPath} ({steam.Kind})");
        foreach (var item in plan.Items)
        {
            Console.WriteLine($"{item.State}: {item.Inspection.Path}");
            Console.WriteLine($"  -> {item.DestinationPath}");
            if (item.Message is not null) Console.WriteLine($"  {item.Message}");
        }
        if (configPreview is not null)
        {
            Console.WriteLine($"SLSsteam config: {slsPaths.ConfigPath}");
            Console.WriteLine(configPreview.ChangesFile
                ? $"  Will update: {string.Join(", ", configPreview.ChangedSections)}"
                : "  Already contains the supported imported metadata.");
            foreach (var warning in conversion.Warnings) Console.WriteLine($"  Warning: {warning}");
        }
        if (depotKeyPreview is not null)
        {
            Console.WriteLine($"Steam depot keys: {steamConfigPath}");
            Console.WriteLine($"  New keys: {(depotKeyPreview.AddedDepotIds.Count == 0 ? "none" : string.Join(", ", depotKeyPreview.AddedDepotIds))}");
            foreach (var conflict in depotKeyPreview.Conflicts) Console.WriteLine($"  Conflict: {conflict}");
        }

        if (!plan.CanApply || depotKeyPreview?.Conflicts.Count > 0)
        {
            Console.Error.WriteLine("Import has conflicts. No files were copied.");
            return 1;
        }
        if (!apply)
        {
            Console.WriteLine("Preview only. Add --apply to copy these new files.");
            return 0;
        }

        var result = service.ApplyNewFiles(steam, inputs);
        Console.WriteLine(result.Message);
        if (result.Success)
        {
            if (configPreview?.ChangesFile == true)
            {
                var configResult = new SlsSteamImportConfigService().Apply(
                    slsPaths.ConfigPath, conversion, GetSlsSteamBackupDirectory());
                Console.WriteLine($"Updated SLSsteam config; backup: {configResult.Backup?.BackupPath}");
            }
            if (depotKeyPreview?.ChangesFile == true)
            {
                var keyResult = new SteamDepotKeyService().Apply(
                    steamConfigPath, conversion.DepotKeys, GetSteamConfigBackupDirectory());
                Console.WriteLine($"Registered Steam depot keys; backup: {keyResult.BackupPath}");
            }
            Console.WriteLine("Supported Lua metadata and depot keys were translated for Linux.");
            Console.WriteLine("Download, launch, multiplayer, and anti-cheat compatibility are not guaranteed.");
        }
        return result.Success ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
    {
        Console.Error.WriteLine($"Could not prepare import: {ex.Message}");
        return 1;
    }
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 2;
}
