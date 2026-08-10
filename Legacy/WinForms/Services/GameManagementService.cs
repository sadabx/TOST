using System.Text.Json;
using System.Text.RegularExpressions;

namespace Trionine.TOST;

internal static class GameManagementService
{
    private const long MaxLuaBytes = 8L * 1024 * 1024;
    private static readonly Regex AddAppIdRegex = new(
        @"(?im)\baddappid\s*\(\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AppManifestNameRegex = new(
        "(?im)^\\s*\"name\"\\s+\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<ManagedGame> FindManagedGames(InstallerSettings settings, InstallerLogger logger)
    {
        var games = new List<ManagedGame>();
        if (!Directory.Exists(settings.LuaPath))
        {
            return games;
        }

        var manifestsByDepot = FindManifestsByDepot(settings.SteamAppsPath, logger);
        IEnumerable<string> luaFiles;
        try
        {
            luaFiles = Directory.EnumerateFiles(settings.LuaPath, "*.lua", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex)
        {
            logger.Error($"Could not scan managed game Lua files: {ex}");
            throw;
        }

        foreach (var luaPath in luaFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fileInfo = new FileInfo(luaPath);
                var content = fileInfo.Length <= MaxLuaBytes ? File.ReadAllText(luaPath) : string.Empty;
                var ids = AddAppIdRegex.Matches(content)
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var fileStem = Path.GetFileNameWithoutExtension(luaPath);
                var appId = fileStem.All(char.IsDigit) && fileStem.Length > 0
                    ? fileStem
                    : ids.FirstOrDefault() ?? fileStem;
                if (ids.Count == 0 && appId.All(char.IsDigit))
                {
                    ids.Add(appId);
                }

                var manifestPaths = ids
                    .Where(manifestsByDepot.ContainsKey)
                    .SelectMany(id => manifestsByDepot[id])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                games.Add(new ManagedGame(
                    appId,
                    TryReadGameName(settings.SteamAppsPath, appId, logger),
                    luaPath,
                    ids,
                    manifestPaths));
            }
            catch (Exception ex)
            {
                logger.Error($"Could not inspect managed game Lua file {luaPath}: {ex}");
            }
        }

        return games;
    }

    public static List<RemovedGameArchive> FindRemovedGames(InstallerLogger logger)
    {
        var archives = new List<RemovedGameArchive>();
        if (!Directory.Exists(AppPaths.RemovedGamesDirectory))
        {
            return archives;
        }

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.RemovedGamesDirectory))
        {
            var metadataPath = Path.Combine(directory, "removal.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var archive = JsonSerializer.Deserialize<RemovedGameArchive>(File.ReadAllText(metadataPath));
                if (archive is null || archive.Files.Count == 0)
                {
                    continue;
                }

                archive.ArchiveDirectory = directory;
                archives.Add(archive);
            }
            catch (Exception ex)
            {
                logger.Error($"Could not read removed game archive {directory}: {ex}");
            }
        }

        return archives.OrderByDescending(archive => archive.RemovedUtc).ToList();
    }

    public static GameManagementResult RemoveGames(
        IReadOnlyCollection<ManagedGame> selectedGames,
        IReadOnlyCollection<ManagedGame> allGames,
        InstallerSettings settings,
        InstallerLogger logger)
    {
        if (selectedGames.Count == 0)
        {
            return new GameManagementResult(false, "Select at least one game to remove.");
        }

        var selectedLuaPaths = selectedGames
            .Select(game => Path.GetFullPath(game.LuaPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestsUsedByOtherGames = allGames
            .Where(game => !selectedLuaPaths.Contains(Path.GetFullPath(game.LuaPath)))
            .SelectMany(game => game.ManifestPaths)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedManifests = selectedGames
            .SelectMany(game => game.ManifestPaths)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifestsToMove = selectedManifests
            .Where(path => !manifestsUsedByOtherGames.Contains(path))
            .ToList();
        var sharedManifestCount = selectedManifests.Count - manifestsToMove.Count;

        var archiveId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archiveDirectory = Path.Combine(AppPaths.RemovedGamesDirectory, archiveId);
        var archive = new RemovedGameArchive
        {
            ArchiveId = archiveId,
            RemovedUtc = DateTime.UtcNow,
            ArchiveDirectory = archiveDirectory,
            Games = selectedGames.Select(game => new RemovedGameEntry
            {
                AppId = game.AppId,
                DisplayName = game.DisplayName,
                LuaFileName = Path.GetFileName(game.LuaPath)
            }).ToList()
        };

        var movedFiles = new List<(string Source, string ArchivePath)>();
        try
        {
            foreach (var game in selectedGames)
            {
                AddArchiveFile(archive, "Lua", game.LuaPath, settings.LuaPath);
            }

            foreach (var manifestPath in manifestsToMove)
            {
                AddArchiveFile(archive, "Manifest", manifestPath, settings.SteamAppsPath);
            }

            Directory.CreateDirectory(archiveDirectory);
            foreach (var file in archive.Files)
            {
                var sourceRoot = file.Kind == "Lua" ? settings.LuaPath : settings.SteamAppsPath;
                var source = Path.Combine(sourceRoot, file.FileName);
                var archivePath = Path.Combine(archiveDirectory, file.ArchiveRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
                File.Move(source, archivePath);
                movedFiles.Add((source, archivePath));
                logger.Info($"Archived managed game file {source} -> {archivePath}");
            }

            var metadataPath = Path.Combine(archiveDirectory, "removal.json");
            File.WriteAllText(
                metadataPath,
                JsonSerializer.Serialize(archive, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            RollBackRemoval(movedFiles, logger);
            TryDeleteArchiveDirectory(archiveDirectory, logger);
            logger.Error($"Managed game removal failed: {ex}");
            return new GameManagementResult(false, $"Could not remove the selected games.\n\n{ex.Message}");
        }

        var message = $"Moved {archive.Files.Count} file{(archive.Files.Count == 1 ? string.Empty : "s")} to TOST's recovery folder.";
        if (sharedManifestCount > 0)
        {
            message += $"\n\nKept {sharedManifestCount} shared manifest{(sharedManifestCount == 1 ? string.Empty : "s")} still used by another Lua file.";
        }

        message += "\n\nRestart Steam for the change to take effect.";
        return new GameManagementResult(true, message);
    }

    public static GameManagementResult RestoreArchive(
        RemovedGameArchive archive,
        InstallerSettings settings,
        InstallerLogger logger)
    {
        if (!IsPathInside(archive.ArchiveDirectory, AppPaths.RemovedGamesDirectory))
        {
            return new GameManagementResult(false, "The recovery archive path is invalid.");
        }

        var moves = new List<(string ArchivePath, string Destination)>();
        foreach (var file in archive.Files)
        {
            if (!IsValidArchiveFile(file))
            {
                return new GameManagementResult(false, $"The recovery entry for {file.FileName} is invalid.");
            }

            var archivePath = Path.GetFullPath(Path.Combine(archive.ArchiveDirectory, file.ArchiveRelativePath));
            var destinationRoot = file.Kind == "Lua" ? settings.LuaPath : settings.SteamAppsPath;
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, file.FileName));
            if (!IsPathInside(archivePath, archive.ArchiveDirectory) ||
                !IsPathInside(destination, destinationRoot) ||
                !File.Exists(archivePath))
            {
                return new GameManagementResult(false, $"The recovery file {file.FileName} is missing or invalid.");
            }

            if (File.Exists(destination))
            {
                return new GameManagementResult(false, $"Cannot restore {file.FileName} because a file with that name already exists.");
            }

            moves.Add((archivePath, destination));
        }

        var completedMoves = new List<(string ArchivePath, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                File.Move(move.ArchivePath, move.Destination);
                completedMoves.Add(move);
                logger.Info($"Restored managed game file {move.ArchivePath} -> {move.Destination}");
            }

            Directory.Delete(archive.ArchiveDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            foreach (var move in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(move.Destination) && !File.Exists(move.ArchivePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(move.ArchivePath)!);
                        File.Move(move.Destination, move.ArchivePath);
                    }
                }
                catch (Exception rollbackException)
                {
                    logger.Error($"Could not roll back restored file {move.Destination}: {rollbackException}");
                }
            }

            logger.Error($"Managed game restore failed: {ex}");
            return new GameManagementResult(false, $"Could not restore the selected games.\n\n{ex.Message}");
        }

        return new GameManagementResult(
            true,
            $"Restored {moves.Count} file{(moves.Count == 1 ? string.Empty : "s")}.\n\nRestart Steam for the change to take effect.");
    }

    private static Dictionary<string, List<string>> FindManifestsByDepot(string steamAppsPath, InstallerLogger logger)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(steamAppsPath))
        {
            return result;
        }

        try
        {
            foreach (var manifestPath in Directory.EnumerateFiles(steamAppsPath, "*.manifest", SearchOption.TopDirectoryOnly))
            {
                var fileStem = Path.GetFileNameWithoutExtension(manifestPath);
                var separatorIndex = fileStem.IndexOf('_');
                var depotId = separatorIndex >= 0 ? fileStem[..separatorIndex] : fileStem;
                if (depotId.Length == 0 || !depotId.All(char.IsDigit))
                {
                    continue;
                }

                if (!result.TryGetValue(depotId, out var paths))
                {
                    paths = [];
                    result[depotId] = paths;
                }

                paths.Add(manifestPath);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Could not scan Steam manifest files: {ex}");
            throw;
        }

        return result;
    }

    private static string? TryReadGameName(string steamAppsPath, string appId, InstallerLogger logger)
    {
        if (appId.Length == 0 || !appId.All(char.IsDigit))
        {
            return null;
        }

        var appManifestPath = Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
        if (!File.Exists(appManifestPath))
        {
            return null;
        }

        try
        {
            var match = AppManifestNameRegex.Match(File.ReadAllText(appManifestPath));
            return match.Success ? match.Groups["name"].Value : null;
        }
        catch (Exception ex)
        {
            logger.Error($"Could not read game name from {appManifestPath}: {ex}");
            return null;
        }
    }

    private static void AddArchiveFile(RemovedGameArchive archive, string kind, string sourcePath, string allowedRoot)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!IsPathInside(fullSourcePath, allowedRoot) || !File.Exists(fullSourcePath))
        {
            throw new InvalidDataException($"The managed file path is invalid: {sourcePath}");
        }

        var fileName = Path.GetFileName(fullSourcePath);
        if (archive.Files.Any(file =>
                file.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                file.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var folder = kind == "Lua" ? "lua" : "steamapps";
        archive.Files.Add(new RemovedFileEntry
        {
            Kind = kind,
            FileName = fileName,
            ArchiveRelativePath = Path.Combine("files", folder, fileName)
        });
    }

    private static bool IsValidArchiveFile(RemovedFileEntry file)
    {
        if (string.IsNullOrWhiteSpace(file.FileName) ||
            !file.FileName.Equals(Path.GetFileName(file.FileName), StringComparison.Ordinal))
        {
            return false;
        }

        return file.Kind switch
        {
            "Lua" => Path.GetExtension(file.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase),
            "Manifest" => Path.GetExtension(file.FileName).Equals(".manifest", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsPathInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void RollBackRemoval(
        IEnumerable<(string Source, string ArchivePath)> movedFiles,
        InstallerLogger logger)
    {
        foreach (var movedFile in movedFiles.Reverse())
        {
            try
            {
                if (File.Exists(movedFile.ArchivePath) && !File.Exists(movedFile.Source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(movedFile.Source)!);
                    File.Move(movedFile.ArchivePath, movedFile.Source);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Could not roll back removed file {movedFile.Source}: {ex}");
            }
        }
    }

    private static void TryDeleteArchiveDirectory(string archiveDirectory, InstallerLogger logger)
    {
        try
        {
            if (Directory.Exists(archiveDirectory) && IsPathInside(archiveDirectory, AppPaths.RemovedGamesDirectory))
            {
                Directory.Delete(archiveDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Could not clean failed removal archive {archiveDirectory}: {ex}");
        }
    }
}

