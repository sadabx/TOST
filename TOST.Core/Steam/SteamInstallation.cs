namespace Trionine.TOST.Core.Steam;

public enum SteamInstallationKind
{
    Native,
    Flatpak
}

public sealed record SteamInstallation(
    string RootPath,
    SteamInstallationKind Kind,
    bool HasSteamApps,
    bool HasConfig)
{
    public string SteamAppsPath => Path.Combine(RootPath, "steamapps");
    public string ConfigPath => Path.Combine(RootPath, "config");
}
