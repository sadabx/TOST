using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Downloads;

public static class DepotKeyExtractor
{
    private static readonly SteamImportInspector Inspector = new();

    public static IReadOnlyDictionary<string, string> ExtractKeys(
        SteamInstallation installation,
        string luaPath,
        IEnumerable<string>? targetDepotIds = null)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetSet = targetDepotIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Extract from the game's own Lua file
        ExtractFromLuaFile(luaPath, keys, targetSet);

        // 2. Extract from other Lua files in ManagedScriptsPath if any target keys are still missing
        if (targetSet == null || targetSet.Any(id => !keys.ContainsKey(id)))
        {
            var scriptsDir = installation.ManagedScriptsPath;
            if (Directory.Exists(scriptsDir))
            {
                foreach (var file in Directory.EnumerateFiles(scriptsDir, "*.lua"))
                {
                    if (string.Equals(file, luaPath, StringComparison.OrdinalIgnoreCase)) continue;
                    ExtractFromLuaFile(file, keys, targetSet);
                }
            }
        }

        // 3. Extract from Steam's config.vdf
        var configVdfPath = Path.Combine(installation.ConfigPath, "config.vdf");
        if (File.Exists(configVdfPath))
        {
            try
            {
                var text = File.ReadAllText(configVdfPath);
                var root = SteamDepotKeyService.VdfParser.Parse(text);
                var depots = SteamDepotKeyService.Find(root, "InstallConfigStore", "Software", "Valve", "Steam", "depots");
                if (depots != null)
                {
                    foreach (var child in depots.Children)
                    {
                        if (targetSet != null && !targetSet.Contains(child.Name)) continue;
                        var decKey = child.Properties.FirstOrDefault(p =>
                            p.Key.Equals("DecryptionKey", StringComparison.OrdinalIgnoreCase)).Value;
                        if (!string.IsNullOrWhiteSpace(decKey) && !keys.ContainsKey(child.Name))
                        {
                            keys[child.Name] = decKey;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Non-fatal if config.vdf cannot be parsed
            }
        }

        return keys;
    }

    private static void ExtractFromLuaFile(
        string path,
        Dictionary<string, string> keys,
        HashSet<string>? targetSet)
    {
        if (!File.Exists(path)) return;
        try
        {
            var inspection = Inspector.Inspect(path);
            foreach (var app in inspection.AppDeclarations)
            {
                if (!string.IsNullOrWhiteSpace(app.DepotKey))
                {
                    if (targetSet == null || targetSet.Contains(app.AppId))
                    {
                        keys[app.AppId] = app.DepotKey;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore malformed Lua files
        }
    }
}
