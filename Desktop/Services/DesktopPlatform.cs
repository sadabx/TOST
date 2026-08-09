using Trionine.TOST.Core.Configuration;
using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop.Services;

internal sealed record DesktopImportSummary(int ImportedFiles, IReadOnlyList<string> Failures)
{
    public bool Success => ImportedFiles > 0 && Failures.Count == 0;

    public string ToMessage()
    {
        var lines = new List<string>
        {
            $"Imported {ImportedFiles} file{(ImportedFiles == 1 ? string.Empty : "s")}"
        };
        lines.AddRange(Failures.Select(failure => $"Skipped {failure}"));
        lines.Add("Will take effect after Steam restarts");
        return string.Join(Environment.NewLine, lines);
    }
}

internal static class DesktopPlatform
{
    public static bool UsesOpenSteamTool => OperatingSystem.IsWindows();
    public static string IntegrationName => UsesOpenSteamTool ? "OpenSteamTool" : "SLSsteam";
    public static string IntegrationReleasesUrl => UsesOpenSteamTool
        ? "https://github.com/OpenSteam001/OpenSteamTool/releases"
        : "https://github.com/AceSLS/SLSsteam/releases";

    public static IReadOnlyList<SteamInstallation> FindInstallations()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        return SteamDiscovery.FindInstallations(preferences.WindowsSteamRoot);
    }

    public static SteamInstallation? PreferredInstallation()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        var installations = SteamDiscovery.FindInstallations(preferences.WindowsSteamRoot);
        if (OperatingSystem.IsWindows())
        {
            return installations.FirstOrDefault();
        }

        return installations.FirstOrDefault(item => item.Kind == preferences.PreferredSteamInstallation)
            ?? installations.FirstOrDefault();
    }

    public static DesktopImportSummary ImportLinuxFiles(SteamInstallation steam, IEnumerable<string> inputPaths)
    {
        var failures = new List<string>();
        var candidates = ExpandFiles(inputPaths, failures).ToArray();
        if (candidates.Length == 0)
        {
            return new DesktopImportSummary(0, failures.Count == 0 ? ["no supported files were provided"] : failures);
        }

        SteamImportPlan plan;
        try
        {
            plan = new SteamImportService().CreatePlan(steam, candidates);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            failures.Add(ex.Message);
            return new DesktopImportSummary(0, failures);
        }

        foreach (var conflict in plan.Items.Where(item => item.State == SteamImportPlanState.Conflict))
        {
            failures.Add($"{Path.GetFileName(conflict.Inspection.Path)}: {conflict.Message}");
        }

        if (!plan.CanApply)
        {
            return new DesktopImportSummary(0, failures);
        }

        var result = new SteamImportService().ApplyNewFiles(steam, candidates);
        if (!result.Success)
        {
            failures.Add(result.Message);
            return new DesktopImportSummary(0, failures);
        }

        try
        {
            var conversion = new SlsSteamImportConversionService().CreatePlan(plan.Items.Select(item => item.Inspection));
            var paths = steam.Kind == SteamInstallationKind.Flatpak
                ? SlsSteamPaths.ForFlatpakUser()
                : SlsSteamPaths.ForCurrentUser();
            var backupRoot = Path.Combine(DesktopPaths.DataRoot, "backups");
            if (conversion.AdditionalApps.Count > 0)
            {
                new SlsSteamImportConfigService().Apply(paths.ConfigPath, conversion, Path.Combine(backupRoot, "SLSsteam"));
            }

            if (conversion.DepotKeys.Count > 0)
            {
                new SteamDepotKeyService().Apply(
                    Path.Combine(steam.ConfigPath, "config.vdf"),
                    conversion.DepotKeys,
                    Path.Combine(backupRoot, "Steam-config"));
            }

            failures.AddRange(conversion.Warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            failures.Add($"configuration: {ex.Message}");
        }

        return new DesktopImportSummary(result.ImportedCount, failures);
    }

    private static IEnumerable<string> ExpandFiles(IEnumerable<string> paths, ICollection<string> failures)
    {
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(path))
            {
                yield return path;
                continue;
            }

            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }

                foreach (var file in files.Where(IsSupportedLinuxImport))
                {
                    yield return file;
                }

                continue;
            }

            failures.Add($"{Path.GetFileName(path)}: path does not exist");
        }
    }

    private static bool IsSupportedLinuxImport(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        return extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".acf", StringComparison.OrdinalIgnoreCase) &&
               name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase);
    }
}
