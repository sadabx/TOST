namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamPaths(
    string DataDirectory,
    string ConfigDirectory,
    string MainLibraryPath,
    string InjectorLibraryPath,
    string ConfigPath,
    IReadOnlyList<string> LogPaths)
{
    public static SlsSteamPaths ForCurrentUser(
        string? homeDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new InvalidOperationException("The current user's home directory could not be determined.");
        }

        environment ??= ReadEnvironment();
        var dataHome = GetAbsoluteEnvironmentPath(environment, "XDG_DATA_HOME")
            ?? Path.Combine(homeDirectory, ".local", "share");
        var configHome = GetAbsoluteEnvironmentPath(environment, "XDG_CONFIG_HOME")
            ?? Path.Combine(homeDirectory, ".config");
        var dataDirectory = Path.Combine(dataHome, "SLSsteam");
        var configDirectory = Path.Combine(configHome, "SLSsteam");

        return new SlsSteamPaths(
            dataDirectory,
            configDirectory,
            Path.Combine(dataDirectory, "SLSsteam.so"),
            Path.Combine(dataDirectory, "library-inject.so"),
            Path.Combine(configDirectory, "config.yaml"),
            new[]
            {
                Path.Combine(homeDirectory, ".SLSsteam.log"),
                Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam", ".SLSsteam.log")
            });
    }

    public static SlsSteamPaths ForFlatpakUser(string? homeDirectory = null)
    {
        homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            throw new InvalidOperationException("The current user's home directory could not be determined.");
        }

        var flatpakHome = Path.Combine(homeDirectory, ".var", "app", "com.valvesoftware.Steam");
        var dataDirectory = Path.Combine(flatpakHome, ".local", "share", "SLSsteam");
        var configDirectory = Path.Combine(flatpakHome, ".config", "SLSsteam");
        return new SlsSteamPaths(
            dataDirectory,
            configDirectory,
            Path.Combine(dataDirectory, "SLSsteam.so"),
            Path.Combine(dataDirectory, "library-inject.so"),
            Path.Combine(configDirectory, "config.yaml"),
            [Path.Combine(flatpakHome, ".SLSsteam.log")]);
    }

    private static string? GetAbsoluteEnvironmentPath(
        IReadOnlyDictionary<string, string?> environment,
        string variable)
    {
        if (!environment.TryGetValue(variable, out var value) ||
            string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value))
        {
            return null;
        }

        return Path.GetFullPath(value);
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["XDG_DATA_HOME"] = Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            ["XDG_CONFIG_HOME"] = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        };
}
