using Trionine.TOST.Core.Configuration;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop;

internal static class DesktopPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST");
    public static TostPreferencesStore PreferencesStore { get; } = new(Path.Combine(DataRoot, "desktop-settings.json"));
    public static int PreferredInstallationIndex => PreferencesStore.Load().PreferredSteamInstallation == SteamInstallationKind.Flatpak ? 1 : 0;
}
