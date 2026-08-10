using Velopack.Locators;

namespace Trionine.TOST;

internal static class AppPaths
{
    public static bool IsPortable { get; private set; } = true;
    public static string DataDirectory { get; private set; } = AppContext.BaseDirectory;
    public static string LauncherPath { get; private set; } = Application.ExecutablePath;
    public static string SettingsPath => Path.Combine(DataDirectory, "installer-settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string LogPath => Path.Combine(LogDirectory, "install.log");
    public static string RemovedGamesDirectory => Path.Combine(DataDirectory, "removed-games");
    public static string GameNamesCachePath => Path.Combine(DataDirectory, "steam-game-names.json");

    public static void Initialize()
    {
        var locator = VelopackLocator.Current;
        IsPortable = locator.IsPortable;

        if (IsPortable)
        {
            DataDirectory = AppContext.BaseDirectory;
            LauncherPath = Application.ExecutablePath;
        }
        else
        {
            var root = string.IsNullOrWhiteSpace(locator.RootAppDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST")
                : locator.RootAppDir;
            DataDirectory = Path.Combine(root, "data");
            LauncherPath = Path.Combine(root, "TOST.exe");
        }

        Directory.CreateDirectory(DataDirectory);
        MigrateLegacyData();
    }

    private static void MigrateLegacyData()
    {
        var oldLocalData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OST",
            "data");
        var settingsCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "installer-settings.json"),
            Path.Combine(oldLocalData, "installer-settings.json")
        };
        var logCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "logs", "install.log"),
            Path.Combine(oldLocalData, "logs", "install.log")
        };

        MigrateFirstExistingFile(settingsCandidates, SettingsPath);
        MigrateFirstExistingFile(logCandidates, LogPath);
    }

    private static void MigrateFirstExistingFile(IEnumerable<string> candidates, string destination)
    {
        if (File.Exists(destination))
        {
            return;
        }

        var source = candidates.FirstOrDefault(path =>
            !Path.GetFullPath(path).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path));
        if (source is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        catch
        {
            // A migration failure must not prevent TOST from starting.
        }
    }
}


