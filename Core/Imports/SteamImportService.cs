namespace Trionine.TOST.Core.Imports;

using Trionine.TOST.Core.Steam;

public enum SteamImportPlanState
{
    Ready,
    Conflict
}

public sealed record SteamImportPlanItem(
    SteamImportInspection Inspection,
    string DestinationPath,
    SteamImportPlanState State,
    string? Message);

public sealed record SteamImportPlan(IReadOnlyList<SteamImportPlanItem> Items)
{
    public bool CanApply => Items.Count > 0 && Items.All(item => item.State == SteamImportPlanState.Ready);
}

public sealed record SteamImportResult(bool Success, int ImportedCount, string Message);

public sealed class SteamImportService
{
    private readonly SteamImportInspector inspector;

    public SteamImportService(SteamImportInspector? inspector = null)
    {
        this.inspector = inspector ?? new SteamImportInspector();
    }

    public SteamImportPlan CreatePlan(SteamInstallation steam, IEnumerable<string> inputPaths)
    {
        var items = inputPaths.Select(path =>
        {
            var inspection = inspector.Inspect(path);
            var destinationDirectory = inspection.Kind switch
            {
                SteamImportKind.Lua => steam.ManagedScriptsPath,
                SteamImportKind.DepotManifest => steam.ManagedManifestsPath,
                SteamImportKind.AppManifest => steam.SteamAppsPath,
                _ => throw new InvalidDataException("Unsupported import type.")
            };
            var destination = Path.GetFullPath(Path.Combine(destinationDirectory, Path.GetFileName(inspection.Path)));
            return new SteamImportPlanItem(
                inspection,
                destination,
                File.Exists(destination) ? SteamImportPlanState.Conflict : SteamImportPlanState.Ready,
                File.Exists(destination) ? "Destination already exists; overwriting is disabled." : null);
        }).ToList();

        var duplicateGroups = items
            .GroupBy(item => item.DestinationPath, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (var group in duplicateGroups)
        {
            foreach (var duplicate in group.ToArray())
            {
                var index = items.IndexOf(duplicate);
                items[index] = duplicate with
                {
                    State = SteamImportPlanState.Conflict,
                    Message = "Multiple inputs target the same destination."
                };
            }
        }

        return new SteamImportPlan(items);
    }

    public SteamImportResult ApplyNewFiles(SteamInstallation steam, IEnumerable<string> inputPaths)
    {
        var plan = CreatePlan(steam, inputPaths);
        if (!plan.CanApply)
        {
            return new SteamImportResult(false, 0, "The import plan has conflicts and was not applied.");
        }

        var completedDestinations = new List<string>();
        try
        {
            foreach (var item in plan.Items)
            {
                var directory = Path.GetDirectoryName(item.DestinationPath)
                    ?? throw new InvalidOperationException("Import destination directory is invalid.");
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(item.DestinationPath)}.tost-{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var source = new FileStream(item.Inspection.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var destination = new FileStream(
                               temporaryPath,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None,
                               81920,
                               FileOptions.WriteThrough))
                    {
                        source.CopyTo(destination);
                        destination.Flush(flushToDisk: true);
                    }

                    File.Move(temporaryPath, item.DestinationPath, overwrite: false);
                    completedDestinations.Add(item.DestinationPath);
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            foreach (var destination in completedDestinations.AsEnumerable().Reverse()) TryDelete(destination);
            return new SteamImportResult(false, 0, $"Import failed; rollback of newly copied files was attempted: {ex.Message}");
        }

        return new SteamImportResult(
            true,
            completedDestinations.Count,
            $"Imported {completedDestinations.Count} file{(completedDestinations.Count == 1 ? string.Empty : "s")}.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* Best-effort rollback; the caller receives a failure result. */ }
    }
}
