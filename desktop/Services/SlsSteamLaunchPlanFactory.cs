using Trionine.TOST.Core.Integrations.SlsSteam;

namespace Trionine.TOST.Desktop.Services;

internal static class SlsSteamLaunchPlanFactory
{
    public static SlsSteamLaunchPlan Create(bool flatpak, SlsSteamPaths paths)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var service = new SlsSteamLaunchConfigurationService();
        return flatpak
            ? service.PreviewFlatpak(paths, home)
            : service.PreviewNative(paths, home, FindSteamExecutables(paths.DataDirectory));
    }

    private static IReadOnlyDictionary<string, string> FindSteamExecutables(string slsDataDirectory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var wrappers = Path.Combine(Path.GetFullPath(slsDataDirectory), "path") + Path.DirectorySeparatorChar;
        foreach (var name in new[] { "steam", "steam-runtime", "steam-native" })
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.GetFullPath(Path.Combine(directory, name));
                if (candidate.StartsWith(wrappers, StringComparison.Ordinal) || !File.Exists(candidate)) continue;
                result[name] = candidate;
                break;
            }
        }
        return result;
    }
}
