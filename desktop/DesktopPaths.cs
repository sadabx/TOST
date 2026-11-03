using Trionine.TOST.Core.Configuration;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop;

internal static class DesktopPaths
{
    public static bool IsPortable => false;
    public static string DataRoot { get; } = ResolveDataRoot();
    public static string LauncherPath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TOST.Desktop");
    public static string PreferencesPath { get; } = Path.Combine(DataRoot, "desktop-settings.json");
    public static TostPreferencesStore PreferencesStore { get; } = new(PreferencesPath);
    public static string LogDirectory { get; } = Path.Combine(DataRoot, "logs");
    public static string LogPath { get; } = Path.Combine(LogDirectory, "tost.log");
    public static string RecoveryRoot { get; } = Path.Combine(DataRoot, "backups", "removed-games");
    public static int PreferredInstallationIndex => PreferencesStore.Load().PreferredSteamInstallation == SteamInstallationKind.Flatpak ? 1 : 0;

    public static void Initialize()
    {
        Directory.CreateDirectory(DataRoot);
    }

    private static string ResolveDataRoot()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST");
    }
}
