namespace Trionine.TOST.Core.Downloads;

public static class DepotDownloaderLocator
{
    private const string ExecutableName = "DepotDownloaderMod";

    public static string GetDefaultToolDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TOST",
            "tools");

    public static string GetDefaultExecutablePath() =>
        Path.Combine(GetDefaultToolDirectory(), ExecutableName);

    public static string? FindExecutable(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var candidates = new[]
        {
            GetDefaultExecutablePath(),
            Path.Combine(AppContext.BaseDirectory, "tools", ExecutableName),
            Path.Combine(AppContext.BaseDirectory, ExecutableName),
            Path.Combine("/usr/lib/tost/tools", ExecutableName),
            Path.Combine("/usr/share/tost/tools", ExecutableName),
            Path.Combine("/usr/local/bin", ExecutableName),
            Path.Combine("/usr/bin", ExecutableName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, ExecutableName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }
}
