namespace Trionine.TOST.Core.Steam;

public static class SteamDiscovery
{
    public static IReadOnlyList<SteamInstallation> FindInstallations(string? rootOverride = null)
    {
        return LinuxSteamDiscovery.FindInstallations();
    }
}
