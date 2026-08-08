using System.Text;
using System.Text.Json;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public enum SlsSteamLaunchItemState { Ready, AlreadyConfigured, Conflict }
public sealed record SlsSteamLaunchPlanItem(string Path, string Content, bool Executable, SlsSteamLaunchItemState State, string? Message);
public sealed record SlsSteamLaunchPlan(string Kind, IReadOnlyList<SlsSteamLaunchPlanItem> Items)
{
    public bool CanApply => Items.Count > 0 && Items.All(item => item.State != SlsSteamLaunchItemState.Conflict);
    public bool HasChanges => Items.Any(item => item.State == SlsSteamLaunchItemState.Ready);
}
public sealed record SlsSteamLaunchRecoveryEntry(string ArchiveId, string Kind, DateTime RemovedUtc, IReadOnlyList<string> Paths);

public sealed class SlsSteamLaunchConfigurationService
{
    private const string Marker = "# Managed by TOST - SLSsteam launch injection";
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SlsSteamLaunchPlan PreviewNative(
        SlsSteamPaths paths,
        string homeDirectory,
        IReadOnlyDictionary<string, string> steamExecutables)
    {
        ValidateLibraries(paths);
        var wrapperDirectory = Path.Combine(Path.GetFullPath(paths.DataDirectory), "path");
        var items = new List<SlsSteamLaunchPlanItem>();
        foreach (var pair in steamExecutables.Where(pair => pair.Key is "steam" or "steam-runtime" or "steam-native"))
        {
            var executable = Path.GetFullPath(pair.Value);
            if (!File.Exists(executable) || executable.StartsWith(wrapperDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException($"Native Steam executable is invalid: {executable}");
            var content = $"{Marker}\n#!/bin/sh\nexport LD_AUDIT={ShellQuote(paths.InjectorLibraryPath + ":" + paths.MainLibraryPath)}\nexec {ShellQuote(executable)} \"$@\"\n";
            items.Add(PlanNewFile(Path.Combine(wrapperDirectory, pair.Key), content, executable: true));
        }
        if (items.Count == 0) throw new InvalidDataException("No native Steam executables were found on PATH.");

        var fishPath = Path.Combine(Path.GetFullPath(homeDirectory), ".config", "fish", "conf.d", "SLSsteam.fish");
        var fishContent = $"{Marker}\nfish_add_path --prepend {ShellQuote(wrapperDirectory)}\n";
        items.Add(PlanNewFile(fishPath, fishContent, executable: false));
        return new SlsSteamLaunchPlan("Native", items);
    }

    public SlsSteamLaunchPlan PreviewFlatpak(SlsSteamPaths paths, string homeDirectory)
    {
        ValidateLibraries(paths);
        var target = Path.Combine(Path.GetFullPath(homeDirectory), ".local", "share", "flatpak", "overrides", "com.valvesoftware.Steam");
        var audit = "/app/links/$LIB/libshared-library-guard.so:" + paths.InjectorLibraryPath + ":" + paths.MainLibraryPath;
        var content = $"{Marker}\n[Environment]\nLD_AUDIT={audit}\nSHARED_LIBRARY_GUARD=0\n";
        return new SlsSteamLaunchPlan("Flatpak", [PlanNewFile(target, content, executable: false)]);
    }

    public IReadOnlyList<string> Apply(SlsSteamLaunchPlan plan)
    {
        if (!plan.CanApply) throw new IOException("Launch-hook plan contains conflicts.");
        var created = new List<string>();
        try
        {
            foreach (var item in plan.Items.Where(item => item.State == SlsSteamLaunchItemState.Ready))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                File.WriteAllText(item.Path, item.Content, Utf8);
                if (item.Executable && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(item.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                created.Add(item.Path);
            }
        }
        catch
        {
            foreach (var path in created) if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return created;
    }

    public IReadOnlyList<string> RemoveManaged(SlsSteamLaunchPlan plan)
    {
        var removed = new List<string>();
        foreach (var item in plan.Items)
        {
            if (!File.Exists(item.Path)) continue;
            var current = File.ReadAllText(item.Path, Utf8);
            if (!current.StartsWith(Marker, StringComparison.Ordinal) || current != item.Content)
                throw new IOException($"Refusing to remove modified or unmanaged hook: {item.Path}");
        }
        foreach (var item in plan.Items)
        {
            if (!File.Exists(item.Path)) continue;
            File.Delete(item.Path);
            removed.Add(item.Path);
        }
        return removed;
    }

    public SlsSteamLaunchRecoveryEntry ArchiveManaged(SlsSteamLaunchPlan plan, string recoveryRoot)
    {
        ValidateManagedForRemoval(plan);
        var present = plan.Items.Where(item => File.Exists(item.Path)).ToArray();
        if (present.Length == 0) throw new IOException("No managed launch hooks were found.");
        var archiveId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archiveDirectory = Path.Combine(Path.GetFullPath(recoveryRoot), archiveId);
        var filesDirectory = Path.Combine(archiveDirectory, "files");
        var entry = new SlsSteamLaunchRecoveryEntry(archiveId, plan.Kind, DateTime.UtcNow, present.Select(item => item.Path).ToArray());
        var moved = new List<(string Source, string Archived)>();
        try
        {
            Directory.CreateDirectory(filesDirectory);
            File.WriteAllText(Path.Combine(archiveDirectory, "launch-hooks.json"), JsonSerializer.Serialize(entry, JsonOptions), Utf8);
            for (var index = 0; index < present.Length; index++)
            {
                var archived = Path.Combine(filesDirectory, $"{index:D2}-{Path.GetFileName(present[index].Path)}");
                File.Move(present[index].Path, archived);
                moved.Add((present[index].Path, archived));
            }
        }
        catch
        {
            foreach (var move in moved.AsEnumerable().Reverse()) if (File.Exists(move.Archived)) File.Move(move.Archived, move.Source);
            throw;
        }
        return entry;
    }

    public IReadOnlyList<SlsSteamLaunchRecoveryEntry> FindRecoveryEntries(string recoveryRoot)
    {
        if (!Directory.Exists(recoveryRoot)) return [];
        return Directory.EnumerateDirectories(recoveryRoot).Select(ReadRecoveryEntry)
            .Where(entry => entry is not null).Cast<SlsSteamLaunchRecoveryEntry>()
            .OrderByDescending(entry => entry.RemovedUtc).ToArray();
    }

    public IReadOnlyList<string> Restore(SlsSteamLaunchPlan plan, string recoveryRoot, string archiveId)
    {
        if (archiveId.Length is < 10 or > 80 || archiveId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidDataException("Invalid launch-hook archive ID.");
        var archiveDirectory = Path.Combine(Path.GetFullPath(recoveryRoot), archiveId);
        var entry = ReadRecoveryEntry(archiveDirectory) ?? throw new InvalidDataException("Launch-hook recovery archive is invalid.");
        if (!entry.Kind.Equals(plan.Kind, StringComparison.Ordinal) || !entry.Paths.SequenceEqual(plan.Items.Where(item => entry.Paths.Contains(item.Path)).Select(item => item.Path)))
            throw new InvalidDataException("Recovery archive does not match this launch configuration.");
        var restored = new List<(string Archived, string Destination)>();
        try
        {
            for (var index = 0; index < entry.Paths.Count; index++)
            {
                var destination = entry.Paths[index];
                if (File.Exists(destination)) throw new IOException($"Restore destination already exists: {destination}");
                var archived = Path.Combine(archiveDirectory, "files", $"{index:D2}-{Path.GetFileName(destination)}");
                if (!File.Exists(archived) || new FileInfo(archived).LinkTarget is not null) throw new InvalidDataException("Recovery file is missing or invalid.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(archived, destination);
                restored.Add((archived, destination));
            }
        }
        catch
        {
            foreach (var move in restored.AsEnumerable().Reverse()) if (File.Exists(move.Destination)) File.Move(move.Destination, move.Archived);
            throw;
        }
        return restored.Select(item => item.Destination).ToArray();
    }

    private static void ValidateManagedForRemoval(SlsSteamLaunchPlan plan)
    {
        foreach (var item in plan.Items)
        {
            if (!File.Exists(item.Path)) continue;
            var current = File.ReadAllText(item.Path, Utf8);
            if (!current.StartsWith(Marker, StringComparison.Ordinal) || current != item.Content)
                throw new IOException($"Refusing to archive modified or unmanaged hook: {item.Path}");
        }
    }

    private static SlsSteamLaunchRecoveryEntry? ReadRecoveryEntry(string directory)
    {
        try
        {
            var metadata = Path.Combine(directory, "launch-hooks.json");
            var info = new FileInfo(metadata);
            if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > 64 * 1024) return null;
            var entry = JsonSerializer.Deserialize<SlsSteamLaunchRecoveryEntry>(File.ReadAllText(metadata, Utf8));
            if (entry is null || Path.GetFileName(directory) != entry.ArchiveId || entry.Paths.Count == 0 ||
                entry.Paths.Distinct(StringComparer.Ordinal).Count() != entry.Paths.Count ||
                entry.Paths.Any(path => !Path.IsPathFullyQualified(path))) return null;
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static SlsSteamLaunchPlanItem PlanNewFile(string path, string content, bool executable)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return new SlsSteamLaunchPlanItem(path, content, executable, SlsSteamLaunchItemState.Ready, null);
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Length > 256 * 1024)
            return new SlsSteamLaunchPlanItem(path, content, executable, SlsSteamLaunchItemState.Conflict, "Existing path is not a bounded regular file.");
        var existing = File.ReadAllText(path, Utf8);
        return existing == content
            ? new SlsSteamLaunchPlanItem(path, content, executable, SlsSteamLaunchItemState.AlreadyConfigured, null)
            : new SlsSteamLaunchPlanItem(path, content, executable, SlsSteamLaunchItemState.Conflict, "Existing file is unmanaged or has been modified.");
    }

    private static void ValidateLibraries(SlsSteamPaths paths)
    {
        foreach (var path in new[] { paths.MainLibraryPath, paths.InjectorLibraryPath })
        {
            var info = new FileInfo(Path.GetFullPath(path));
            if (!info.Exists || info.LinkTarget is not null || info.Length == 0)
                throw new InvalidDataException($"Required SLSsteam library is missing: {path}");
        }
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
