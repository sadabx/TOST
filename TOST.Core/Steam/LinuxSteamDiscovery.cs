namespace Trionine.TOST.Core.Steam;

public static class LinuxSteamDiscovery
{
    public static IReadOnlyList<SteamInstallation> FindInstallations(
        string? homeDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        environment ??= ReadEnvironment();

        var candidates = new List<(string Path, SteamInstallationKind Kind)>();
        AddEnvironmentCandidate(candidates, environment, "STEAM_DIR", SteamInstallationKind.Native);
        AddEnvironmentCandidate(candidates, environment, "STEAM_ROOT", SteamInstallationKind.Native);

        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            candidates.Add((Path.Combine(homeDirectory, ".local", "share", "Steam"), SteamInstallationKind.Native));
            candidates.Add((Path.Combine(homeDirectory, ".steam", "steam"), SteamInstallationKind.Native));
            candidates.Add((Path.Combine(homeDirectory, ".steam", "root"), SteamInstallationKind.Native));
            candidates.Add((Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"), SteamInstallationKind.Flatpak));
        }

        var installations = new List<SteamInstallation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate.Path);
            var canonicalPath = ResolveLinkTarget(fullPath);
            if (!Directory.Exists(canonicalPath) || !seen.Add(canonicalPath))
            {
                continue;
            }

            installations.Add(new SteamInstallation(
                canonicalPath,
                candidate.Kind,
                Directory.Exists(Path.Combine(canonicalPath, "steamapps")),
                Directory.Exists(Path.Combine(canonicalPath, "config"))));
        }

        return installations;
    }

    private static void AddEnvironmentCandidate(
        ICollection<(string Path, SteamInstallationKind Kind)> candidates,
        IReadOnlyDictionary<string, string?> environment,
        string variable,
        SteamInstallationKind kind)
    {
        if (environment.TryGetValue(variable, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            candidates.Add((value, kind));
        }
    }

    private static string ResolveLinkTarget(string path)
    {
        try
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["STEAM_DIR"] = Environment.GetEnvironmentVariable("STEAM_DIR"),
            ["STEAM_ROOT"] = Environment.GetEnvironmentVariable("STEAM_ROOT")
        };
}
