using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Trionine.TOST.Core.Steam;

public static class SteamDiscovery
{
    public static IReadOnlyList<SteamInstallation> FindInstallations(string? windowsRootOverride = null)
    {
        if (OperatingSystem.IsLinux())
        {
            return LinuxSteamDiscovery.FindInstallations();
        }

        if (OperatingSystem.IsWindows())
        {
            return FindWindowsInstallations(windowsRootOverride);
        }

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<SteamInstallation> FindWindowsInstallations(string? rootOverride)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            candidates.Add(rootOverride);
        }

        var registryPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            candidates.Add(registryPath);
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<SteamInstallation>();
        foreach (var candidate in candidates)
        {
            string root;
            try
            {
                root = Path.GetFullPath(candidate.Replace('/', Path.DirectorySeparatorChar));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (!seen.Add(root) || !Directory.Exists(root))
            {
                continue;
            }

            found.Add(new SteamInstallation(
                root,
                SteamInstallationKind.Windows,
                Directory.Exists(Path.Combine(root, "steamapps")),
                Directory.Exists(Path.Combine(root, "config"))));
        }

        return found;
    }
}
