using System.Text.Json;
using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.GameManagement;

public sealed record ManagedGame(
    string AppId,
    string? Name,
    string LuaPath,
    IReadOnlyList<string> DepotIds,
    IReadOnlyList<string> ManifestPaths)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"App {AppId}" : Name;
}

public sealed record RemovedGameArchive(
    string ArchiveId,
    DateTime RemovedUtc,
    IReadOnlyList<RemovedGameEntry> Games,
    IReadOnlyList<RemovedFileEntry> Files,
    string ArchiveDirectory);

public sealed record RemovedGameEntry(string AppId, string DisplayName, string LuaFileName);
public sealed record RemovedFileEntry(string Kind, string FileName, string ArchiveRelativePath);
public sealed record GameManagementResult(bool Success, string Message, string? ArchiveId = null);

public sealed class ManagedGameService
{
    private const string MetadataFileName = "removal.json";
    private readonly SteamImportInspector inspector = new();
    private readonly SteamAppManifestParser appManifestParser = new();

    public IReadOnlyList<ManagedGame> FindManagedGames(SteamInstallation installation)
    {
        var luaRoot = Path.GetFullPath(installation.SlsPluginPath);
        var manifestRoot = Path.GetFullPath(installation.DepotCachePath);
        if (!Directory.Exists(luaRoot)) return [];

        var manifests = FindDepotManifests(manifestRoot);
        var games = new List<ManagedGame>();
        foreach (var path in Directory.EnumerateFiles(luaRoot, "*.lua", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null) continue;
            try
            {
                var parsed = inspector.Inspect(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                var appId = stem.Length > 0 && stem.All(char.IsDigit) ? stem : parsed.AppIds[0];
                var depotIds = parsed.DepotIds.Count > 0
                    ? parsed.DepotIds
                    : parsed.AppIds.Where(id => !id.Equals(appId, StringComparison.Ordinal)).ToArray();
                var paths = depotIds.Where(manifests.ContainsKey).SelectMany(id => manifests[id])
                    .Distinct(StringComparer.Ordinal).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
                games.Add(new ManagedGame(appId, FindGameName(installation.SteamAppsPath, appId), path, depotIds, paths));
            }
            catch (InvalidDataException)
            {
                // Ignore unrelated or malformed Lua files; never execute them.
            }
        }
        return games;
    }

    public IReadOnlyList<RemovedGameArchive> FindRemovedGames(string recoveryRoot)
    {
        var root = Path.GetFullPath(recoveryRoot);
        if (!Directory.Exists(root)) return [];
        var archives = new List<RemovedGameArchive>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (new DirectoryInfo(directory).LinkTarget is not null) continue;
            try
            {
                var metadata = JsonSerializer.Deserialize<ArchiveMetadata>(File.ReadAllText(Path.Combine(directory, MetadataFileName)));
                if (metadata is null || metadata.Files.Count == 0) continue;
                archives.Add(new RemovedGameArchive(metadata.ArchiveId, metadata.RemovedUtc, metadata.Games, metadata.Files, directory));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return archives.OrderByDescending(item => item.RemovedUtc).ToArray();
    }

    public GameManagementResult RemoveGames(
        IReadOnlyCollection<ManagedGame> selectedGames,
        IReadOnlyCollection<ManagedGame> allGames,
        SteamInstallation installation,
        string recoveryRoot)
    {
        if (selectedGames.Count == 0) return new(false, "Select at least one game to remove.");
        var selectedLua = selectedGames.Select(game => Path.GetFullPath(game.LuaPath)).ToHashSet(StringComparer.Ordinal);
        var usedElsewhere = allGames.Where(game => !selectedLua.Contains(Path.GetFullPath(game.LuaPath)))
            .SelectMany(game => game.ManifestPaths).Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        var manifestFiles = selectedGames.SelectMany(game => game.ManifestPaths).Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal).Where(path => !usedElsewhere.Contains(path)).ToArray();
        var sharedCount = selectedGames.SelectMany(game => game.ManifestPaths).Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal).Count() - manifestFiles.Length;

        var archiveId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archiveDirectory = Path.Combine(Path.GetFullPath(recoveryRoot), archiveId);
        var files = new List<RemovedFileEntry>();
        foreach (var game in selectedGames) AddFile(files, "Lua", game.LuaPath, installation.SlsPluginPath);
        foreach (var path in manifestFiles) AddFile(files, "Manifest", path, installation.DepotCachePath);
        var games = selectedGames.Select(game => new RemovedGameEntry(game.AppId, game.DisplayName, Path.GetFileName(game.LuaPath))).ToArray();
        var metadata = new ArchiveMetadata(archiveId, DateTime.UtcNow, games, files);
        var completed = new List<(string Source, string Archived)>();
        try
        {
            Directory.CreateDirectory(archiveDirectory);
            foreach (var file in files)
            {
                var root = file.Kind == "Lua" ? installation.SlsPluginPath : installation.DepotCachePath;
                var source = Path.Combine(root, file.FileName);
                var archived = Path.Combine(archiveDirectory, file.ArchiveRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(archived)!);
                File.Move(source, archived);
                completed.Add((source, archived));
            }
            File.WriteAllText(Path.Combine(archiveDirectory, MetadataFileName), JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            RollBack(completed);
            return new(false, $"Could not remove the selected games: {ex.Message}");
        }
        var message = $"Archived {files.Count} file{(files.Count == 1 ? "" : "s")}.";
        if (sharedCount > 0) message += $" Kept {sharedCount} shared manifest{(sharedCount == 1 ? "" : "s")}.";
        return new(true, message + " Restart Steam to apply the change.", archiveId);
    }

    public GameManagementResult RestoreArchive(RemovedGameArchive archive, SteamInstallation installation, string recoveryRoot)
    {
        if (!IsInside(archive.ArchiveDirectory, recoveryRoot)) return new(false, "The recovery archive path is invalid.");
        var moves = new List<(string Archived, string Destination)>();
        foreach (var file in archive.Files)
        {
            if (!ValidEntry(file)) return new(false, $"The recovery entry for {file.FileName} is invalid.");
            var source = Path.GetFullPath(Path.Combine(archive.ArchiveDirectory, file.ArchiveRelativePath));
            var root = file.Kind == "Lua" ? installation.SlsPluginPath : installation.DepotCachePath;
            var destination = Path.GetFullPath(Path.Combine(root, file.FileName));
            if (!IsInside(source, archive.ArchiveDirectory) || !IsInside(destination, root) || !File.Exists(source))
                return new(false, $"The recovery file {file.FileName} is missing or invalid.");
            if (new FileInfo(source).LinkTarget is not null || File.Exists(destination))
                return new(false, $"Cannot safely restore {file.FileName}; the destination already exists or the archive is invalid.");
            moves.Add((source, destination));
        }
        var completed = new List<(string Archived, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                File.Move(move.Archived, move.Destination);
                completed.Add(move);
            }
            Directory.Delete(archive.ArchiveDirectory, true);
            return new(true, $"Restored {moves.Count} files. Restart Steam to apply the change.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var move in completed.AsEnumerable().Reverse())
                if (File.Exists(move.Destination) && !File.Exists(move.Archived)) File.Move(move.Destination, move.Archived);
            return new(false, $"Could not restore the archive: {ex.Message}");
        }
    }

    private string? FindGameName(string steamAppsRoot, string appId)
    {
        try { return appManifestParser.Parse(Path.Combine(steamAppsRoot, $"appmanifest_{appId}.acf")).Name; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }

    private static Dictionary<string, List<string>> FindDepotManifests(string root)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return result;
        foreach (var path in Directory.EnumerateFiles(root, "*.manifest", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null) continue;
            var stem = Path.GetFileNameWithoutExtension(path);
            var separator = stem.IndexOf('_');
            var depot = separator < 0 ? stem : stem[..separator];
            if (depot.Length == 0 || !depot.All(char.IsDigit)) continue;
            if (!result.TryGetValue(depot, out var items)) result[depot] = items = [];
            items.Add(path);
        }
        return result;
    }

    private static void AddFile(List<RemovedFileEntry> files, string kind, string source, string root)
    {
        var path = Path.GetFullPath(source);
        if (!IsInside(path, root) || !File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidDataException($"Managed file is outside its expected directory: {source}");
        var name = Path.GetFileName(path);
        if (!files.Any(item => item.Kind == kind && item.FileName == name))
            files.Add(new(kind, name, Path.Combine("files", kind.ToLowerInvariant(), name)));
    }

    private static bool ValidEntry(RemovedFileEntry item) =>
        item.FileName == Path.GetFileName(item.FileName) && item.Kind switch
        {
            "Lua" => Path.GetExtension(item.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase),
            "Manifest" => Path.GetExtension(item.FileName).Equals(".manifest", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static bool IsInside(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.Ordinal);
    }

    private static void RollBack(IEnumerable<(string Source, string Archived)> completed)
    {
        foreach (var move in completed.Reverse())
            if (File.Exists(move.Archived) && !File.Exists(move.Source)) File.Move(move.Archived, move.Source);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record ArchiveMetadata(string ArchiveId, DateTime RemovedUtc, IReadOnlyList<RemovedGameEntry> Games, IReadOnlyList<RemovedFileEntry> Files);
}
