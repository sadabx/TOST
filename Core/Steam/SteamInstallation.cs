namespace Trionine.TOST.Core.Steam;

public enum SteamInstallationKind
{
    Native,
    Flatpak,
    Windows
}

public sealed record SteamInstallation(
    string RootPath,
    SteamInstallationKind Kind,
    bool HasSteamApps,
    bool HasConfig)
{
    public string SteamAppsPath => Path.Combine(RootPath, "steamapps");
    public string ConfigPath => Path.Combine(RootPath, "config");
    public string SlsPluginPath => Path.Combine(ConfigPath, "stplug-in");
    public string DepotCachePath => Path.Combine(RootPath, "depotcache");
    public string ManagedScriptsPath => Kind == SteamInstallationKind.Windows
        ? Path.Combine(ConfigPath, "lua")
        : SlsPluginPath;
    public string ManagedManifestsPath => Kind == SteamInstallationKind.Windows
        ? SteamAppsPath
        : DepotCachePath;
    public string CommonAppsPath => Path.Combine(SteamAppsPath, "common");
    public string UserDataPath => Path.Combine(RootPath, "userdata");
}
