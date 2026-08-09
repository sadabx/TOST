using Trionine.TOST.Core.Configuration;
using Trionine.TOST.Core.Steam;
using Velopack.Locators;
using System.Text.Json;

namespace Trionine.TOST.Desktop;

internal static class DesktopPaths
{
    public static bool IsPortable { get; } = DetectPortable();
    public static string DataRoot { get; } = ResolveDataRoot();
    public static string LauncherPath { get; } = ResolveLauncherPath();
    public static string PreferencesPath { get; } = Path.Combine(DataRoot, "desktop-settings.json");
    public static TostPreferencesStore PreferencesStore { get; } = new(PreferencesPath);
    public static string LogDirectory { get; } = Path.Combine(DataRoot, "logs");
    public static string LogPath { get; } = Path.Combine(LogDirectory, "tost.log");
    public static string RecoveryRoot { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(DataRoot, "removed-games")
        : Path.Combine(DataRoot, "backups", "removed-games");
    public static int PreferredInstallationIndex => PreferencesStore.Load().PreferredSteamInstallation == SteamInstallationKind.Flatpak ? 1 : 0;

    public static void Initialize()
    {
        Directory.CreateDirectory(DataRoot);
        if (!OperatingSystem.IsWindows() || File.Exists(PreferencesPath))
        {
            return;
        }

        var legacyPath = Path.Combine(DataRoot, "installer-settings.json");
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(legacyPath));
            var root = document.RootElement;
            var migrated = new TostPreferences
            {
                WindowsSteamRoot = ReadString(root, "SteamRoot") ?? string.Empty,
                OverwriteExistingFiles = ReadBoolean(root, "OverwriteExisting", true),
                BackupFilesBeforeOverwrite = ReadBoolean(root, "BackupBeforeOverwrite", true),
                StartWithDesktop = ReadBoolean(root, "StartWithWindows", false),
                FloatingIconAlwaysOnTop = ReadBoolean(root, "AlwaysOnTop", true),
                AutomaticallyCheckForUpdates = ReadBoolean(root, "AutoCheckForUpdates", true)
            };
            PreferencesStore.Save(migrated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Invalid legacy settings must not prevent startup.
        }
    }

    private static bool DetectPortable() => !OperatingSystem.IsWindows() || VelopackLocator.Current.IsPortable;

    private static string ResolveDataRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var locator = VelopackLocator.Current;
            if (locator.IsPortable)
            {
                return AppContext.BaseDirectory;
            }

            var root = string.IsNullOrWhiteSpace(locator.RootAppDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST")
                : locator.RootAppDir;
            return Path.Combine(root, "data");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST");
    }

    private static string ResolveLauncherPath()
    {
        if (!OperatingSystem.IsWindows() || VelopackLocator.Current.IsPortable)
        {
            return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TOST.Desktop");
        }

        var root = VelopackLocator.Current.RootAppDir;
        return string.IsNullOrWhiteSpace(root)
            ? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TOST.Desktop.exe")
            : Path.Combine(root, "TOST.Desktop.exe");
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
