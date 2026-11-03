using System.Text.RegularExpressions;
using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Downloads;

public sealed record DepotDownloadItem(
    string DepotId,
    string ManifestId,
    string? ManifestPath,
    string? DepotKey,
    long? Size);

public sealed record GameDownloadPlan(
    string AppId,
    string GameName,
    string InstallDirName,
    string TargetDirectory,
    IReadOnlyList<DepotDownloadItem> Depots);

public static class GameDownloadPlanner
{
    private static readonly SteamImportInspector Inspector = new();
    private static readonly SteamAppManifestParser AcfParser = new();
    private static readonly Regex InvalidPathChars = new(
        @"[\/\\:\*\?""<>\|]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static GameDownloadPlan CreatePlan(SteamInstallation installation, ManagedGame game)
    {
        var manifests = new Dictionary<string, (string ManifestId, string? ManifestPath, long? Size)>(StringComparer.OrdinalIgnoreCase);

        // 1. Check manifests declared in Lua
        if (File.Exists(game.LuaPath))
        {
            try
            {
                var inspection = Inspector.Inspect(game.LuaPath);
                foreach (var decl in inspection.Manifests)
                {
                    if (!string.IsNullOrWhiteSpace(decl.DepotId) && !string.IsNullOrWhiteSpace(decl.ManifestId))
                    {
                        manifests[decl.DepotId] = (decl.ManifestId, null, decl.Size);
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        // 2. Map existing manifest files in depotcache
        var depotcacheDir = installation.DepotCachePath;
        foreach (var path in game.ManifestPaths)
        {
            var fileName = Path.GetFileName(path);
            var match = Regex.Match(fileName, @"^(?<depot>\d+)_(?<manifest>\d+)\.manifest$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var depotId = match.Groups["depot"].Value;
                var manifestId = match.Groups["manifest"].Value;
                var size = manifests.TryGetValue(depotId, out var existing) ? existing.Size : null;
                manifests[depotId] = (manifestId, Path.GetFullPath(path), size);
            }
        }

        // 3. Extract depot keys
        var keys = DepotKeyExtractor.ExtractKeys(installation, game.LuaPath, manifests.Keys);

        // 4. Resolve install directory name
        var installDirName = ResolveInstallDirName(installation, game);
        var targetDir = Path.Combine(installation.CommonAppsPath, installDirName);

        // Prioritize depots that have local manifest files in depotcache (e.g. skip unused Mac depot on Linux if unmanifested)
        var withManifests = manifests.Where(pair => pair.Value.ManifestPath != null).ToList();
        var selectedDepots = withManifests.Count > 0 ? withManifests : manifests.ToList();

        var depotItems = selectedDepots.OrderBy(pair => ulong.TryParse(pair.Key, out var val) ? val : 0)
            .Select(pair => new DepotDownloadItem(
                pair.Key,
                pair.Value.ManifestId,
                pair.Value.ManifestPath,
                keys.TryGetValue(pair.Key, out var key) ? key : null,
                pair.Value.Size))
            .ToList();

        return new GameDownloadPlan(
            game.AppId,
            game.DisplayName,
            installDirName,
            targetDir,
            depotItems);
    }

    public static string ResolveInstallDirName(SteamInstallation installation, ManagedGame game)
    {
        var acfPath = Path.Combine(installation.SteamAppsPath, $"appmanifest_{game.AppId}.acf");
        if (File.Exists(acfPath))
        {
            try
            {
                var acf = AcfParser.Parse(acfPath);
                if (!string.IsNullOrWhiteSpace(acf.InstallDirectory))
                {
                    return acf.InstallDirectory.Trim();
                }
            }
            catch
            {
                // Fall through
            }
        }

        // Check if steam-game-names.json in ~/.local/share/TOST has a cached name
        var cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "TOST", "steam-game-names.json");
        if (File.Exists(cachePath) && (string.IsNullOrWhiteSpace(game.Name) || game.DisplayName.StartsWith("App ")))
        {
            try
            {
                var json = File.ReadAllText(cachePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(game.AppId, out var prop) && prop.GetString() is { } cachedName && !string.IsNullOrWhiteSpace(cachedName))
                {
                    var cleanCached = InvalidPathChars.Replace(cachedName, "_").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanCached))
                    {
                        return cleanCached;
                    }
                }
            }
            catch { }
        }

        var candidate = InvalidPathChars.Replace(game.DisplayName, "_").Trim();
        while (candidate.Contains("__"))
        {
            candidate = candidate.Replace("__", "_");
        }

        return string.IsNullOrWhiteSpace(candidate) ? game.AppId : candidate;
    }
}
