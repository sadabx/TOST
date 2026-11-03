using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Downloads;

public enum GameInstallState
{
    NotInstalled,
    Installed,
    Downloading
}

public static class GameInstallStatus
{
    private static readonly SteamAppManifestParser AcfParser = new();

    public static bool IsGameInstalled(SteamInstallation installation, ManagedGame game)
    {
        var acfPath = Path.Combine(installation.SteamAppsPath, $"appmanifest_{game.AppId}.acf");
        if (!File.Exists(acfPath))
        {
            // If the Steam ACF manifest does not exist, Steam considers the game uninstalled.
            return false;
        }

        try
        {
            var manifest = AcfParser.Parse(acfPath);
            // StateFlags 4 indicates fully installed in Steam
            if (manifest.StateFlags == 4)
            {
                var installDir = manifest.InstallDirectory;
                if (string.IsNullOrWhiteSpace(installDir))
                {
                    installDir = GameDownloadPlanner.ResolveInstallDirName(installation, game);
                }

                if (!string.IsNullOrWhiteSpace(installDir))
                {
                    var fullCommonPath = Path.Combine(installation.CommonAppsPath, installDir);
                    if (Directory.Exists(fullCommonPath))
                    {
                        var hasFiles = Directory.EnumerateFileSystemEntries(fullCommonPath)
                            .Any(path => !path.EndsWith(".DepotDownloader", StringComparison.OrdinalIgnoreCase));
                        if (hasFiles)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
