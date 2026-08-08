using System.Text.Json;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamRemovalPreview(
    string InstallationKind,
    IReadOnlyList<string> Files,
    bool HasFiles);

public sealed record SlsSteamRecoveryEntry(
    string ArchiveId,
    string InstallationKind,
    DateTime RemovedUtc,
    IReadOnlyList<string> FileNames);

public sealed record SlsSteamRecoveryResult(
    bool Changed,
    string Message,
    string? ArchiveId = null);

public sealed class SlsSteamRecoveryService
{
    private const string MetadataFileName = "removal.json";
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.Ordinal)
    {
        "SLSsteam.so",
        "library-inject.so"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SlsSteamRemovalPreview PreviewRemoval(SlsSteamPaths paths, string installationKind)
    {
        var files = GetExistingManagedFiles(paths);
        return new SlsSteamRemovalPreview(installationKind, files, files.Count > 0);
    }

    public SlsSteamRecoveryResult Remove(
        SlsSteamPaths paths,
        string installationKind,
        string recoveryRoot)
    {
        var files = GetExistingManagedFiles(paths);
        if (files.Count == 0)
        {
            return new SlsSteamRecoveryResult(false, "No managed SLSsteam libraries were found.");
        }

        var archiveId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archiveDirectory = Path.Combine(Path.GetFullPath(recoveryRoot), archiveId);
        var filesDirectory = Path.Combine(archiveDirectory, "files");
        var metadata = new RecoveryMetadata
        {
            ArchiveId = archiveId,
            InstallationKind = installationKind,
            RemovedUtc = DateTime.UtcNow,
            FileNames = files.Select(Path.GetFileName).ToList()!
        };
        var completedMoves = new List<(string Source, string Archived)>();

        try
        {
            Directory.CreateDirectory(filesDirectory);
            File.WriteAllText(
                Path.Combine(archiveDirectory, MetadataFileName),
                JsonSerializer.Serialize(metadata, JsonOptions));
            foreach (var source in files)
            {
                var archived = Path.Combine(filesDirectory, Path.GetFileName(source));
                File.Move(source, archived);
                completedMoves.Add((source, archived));
            }
        }
        catch
        {
            RollBackMoves(completedMoves);
            TryDeleteEmptyArchive(archiveDirectory);
            throw;
        }

        return new SlsSteamRecoveryResult(
            true,
            $"Archived {files.Count} SLSsteam librar{(files.Count == 1 ? "y" : "ies")}. Configuration was preserved.",
            archiveId);
    }

    public IReadOnlyList<SlsSteamRecoveryEntry> FindRecoveryEntries(string recoveryRoot)
    {
        if (!Directory.Exists(recoveryRoot))
        {
            return [];
        }

        var entries = new List<SlsSteamRecoveryEntry>();
        foreach (var directory in Directory.EnumerateDirectories(recoveryRoot, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var metadata = ReadMetadata(directory);
                entries.Add(new SlsSteamRecoveryEntry(
                    metadata.ArchiveId,
                    metadata.InstallationKind,
                    metadata.RemovedUtc,
                    metadata.FileNames));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // Invalid or unrelated directories are not recovery entries.
            }
        }

        return entries.OrderByDescending(entry => entry.RemovedUtc).ToArray();
    }

    public SlsSteamRecoveryEntry GetRecoveryEntry(string recoveryRoot, string archiveId)
    {
        var metadata = ReadMetadata(ResolveArchiveDirectory(recoveryRoot, archiveId));
        return new SlsSteamRecoveryEntry(
            metadata.ArchiveId,
            metadata.InstallationKind,
            metadata.RemovedUtc,
            metadata.FileNames);
    }

    public SlsSteamRecoveryResult Restore(
        SlsSteamPaths paths,
        string installationKind,
        string recoveryRoot,
        string archiveId)
    {
        var archiveDirectory = ResolveArchiveDirectory(recoveryRoot, archiveId);
        var metadata = ReadMetadata(archiveDirectory);
        if (!metadata.InstallationKind.Equals(installationKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"This archive belongs to the {metadata.InstallationKind} installation, not {installationKind}.");
        }

        var moves = new List<(string Archived, string Destination)>();
        foreach (var fileName in metadata.FileNames)
        {
            ValidateFileName(fileName);
            var archived = Path.Combine(archiveDirectory, "files", fileName);
            var destination = Path.Combine(paths.DataDirectory, fileName);
            if (!IsRegularFile(archived))
            {
                throw new InvalidDataException($"Recovery file {fileName} is missing or is not a regular file.");
            }

            if (File.Exists(destination))
            {
                throw new IOException($"Cannot restore {fileName} because it already exists.");
            }

            moves.Add((archived, destination));
        }

        var completedMoves = new List<(string Archived, string Destination)>();
        try
        {
            Directory.CreateDirectory(paths.DataDirectory);
            foreach (var move in moves)
            {
                File.Move(move.Archived, move.Destination);
                completedMoves.Add(move);
            }
        }
        catch
        {
            foreach (var move in completedMoves.AsEnumerable().Reverse())
            {
                if (File.Exists(move.Destination) && !File.Exists(move.Archived))
                {
                    File.Move(move.Destination, move.Archived);
                }
            }
            throw;
        }

        TryDeleteCompletedArchive(archiveDirectory);

        return new SlsSteamRecoveryResult(true, $"Restored {moves.Count} SLSsteam libraries.");
    }

    private static List<string> GetExistingManagedFiles(SlsSteamPaths paths)
    {
        var root = Path.GetFullPath(paths.DataDirectory);
        var files = new[] { paths.MainLibraryPath, paths.InjectorLibraryPath };
        var result = new List<string>();
        foreach (var path in files)
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsInside(fullPath, root) || !IsRegularFile(fullPath))
            {
                continue;
            }
            ValidateFileName(Path.GetFileName(fullPath));
            result.Add(fullPath);
        }
        return result;
    }

    private static RecoveryMetadata ReadMetadata(string archiveDirectory)
    {
        var metadataPath = Path.Combine(archiveDirectory, MetadataFileName);
        if (!IsRegularFile(metadataPath) || new FileInfo(metadataPath).Length > 64 * 1024)
        {
            throw new InvalidDataException("Recovery metadata is missing or invalid.");
        }

        var metadata = JsonSerializer.Deserialize<RecoveryMetadata>(File.ReadAllText(metadataPath))
            ?? throw new InvalidDataException("Recovery metadata is empty.");
        if (string.IsNullOrWhiteSpace(metadata.ArchiveId) ||
            string.IsNullOrWhiteSpace(metadata.InstallationKind) ||
            metadata.FileNames.Count == 0 ||
            metadata.FileNames.Distinct(StringComparer.Ordinal).Count() != metadata.FileNames.Count)
        {
            throw new InvalidDataException("Recovery metadata is invalid.");
        }
        foreach (var fileName in metadata.FileNames)
        {
            ValidateFileName(fileName);
        }
        return metadata;
    }

    private static string ResolveArchiveDirectory(string recoveryRoot, string archiveId)
    {
        if (string.IsNullOrWhiteSpace(archiveId) ||
            !archiveId.Equals(Path.GetFileName(archiveId), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The recovery archive identifier is invalid.");
        }

        var root = Path.GetFullPath(recoveryRoot);
        var directory = Path.GetFullPath(Path.Combine(root, archiveId));
        if (!IsInside(directory, root) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The recovery archive was not found.");
        }
        return directory;
    }

    private static void ValidateFileName(string fileName)
    {
        if (!AllowedFiles.Contains(fileName) ||
            !fileName.Equals(Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported recovery filename: {fileName}");
        }
    }

    private static bool IsRegularFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget is null;
    }

    private static bool IsInside(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static void RollBackMoves(IEnumerable<(string Source, string Archived)> moves)
    {
        foreach (var move in moves.Reverse())
        {
            if (File.Exists(move.Archived) && !File.Exists(move.Source))
            {
                File.Move(move.Archived, move.Source);
            }
        }
    }

    private static void TryDeleteEmptyArchive(string archiveDirectory)
    {
        try
        {
            var metadataPath = Path.Combine(archiveDirectory, MetadataFileName);
            if (File.Exists(metadataPath)) File.Delete(metadataPath);
            var filesDirectory = Path.Combine(archiveDirectory, "files");
            if (Directory.Exists(filesDirectory) && !Directory.EnumerateFileSystemEntries(filesDirectory).Any())
                Directory.Delete(filesDirectory);
            if (Directory.Exists(archiveDirectory) && !Directory.EnumerateFileSystemEntries(archiveDirectory).Any())
                Directory.Delete(archiveDirectory);
        }
        catch
        {
            // Cleanup after a failed operation is best effort.
        }
    }

    private static void TryDeleteCompletedArchive(string archiveDirectory)
    {
        try
        {
            File.Delete(Path.Combine(archiveDirectory, MetadataFileName));
            var filesDirectory = Path.Combine(archiveDirectory, "files");
            if (Directory.Exists(filesDirectory) && !Directory.EnumerateFileSystemEntries(filesDirectory).Any())
                Directory.Delete(filesDirectory);
            if (Directory.Exists(archiveDirectory) && !Directory.EnumerateFileSystemEntries(archiveDirectory).Any())
                Directory.Delete(archiveDirectory);
        }
        catch
        {
            // The files are restored already; stale archive cleanup is best effort.
        }
    }

    private sealed class RecoveryMetadata
    {
        public string ArchiveId { get; set; } = string.Empty;
        public string InstallationKind { get; set; } = string.Empty;
        public DateTime RemovedUtc { get; set; }
        public List<string> FileNames { get; set; } = [];
    }
}
