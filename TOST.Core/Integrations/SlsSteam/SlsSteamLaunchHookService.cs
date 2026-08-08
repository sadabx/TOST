using System.Text;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamLaunchHook(string Kind, string Path, bool Active);

public sealed class SlsSteamLaunchHookService
{
    private const int MaximumTextFileBytes = 256 * 1024;

    public IReadOnlyList<SlsSteamLaunchHook> FindHooks(string? homeDirectory = null)
    {
        homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory)) return [];

        var candidates = new[]
        {
            ("Native wrapper", Path.Combine(homeDirectory, ".local", "share", "SLSsteam", "path", "steam")),
            ("Native wrapper", Path.Combine(homeDirectory, ".local", "share", "SLSsteam", "path", "steam-runtime")),
            ("Native wrapper", Path.Combine(homeDirectory, ".local", "share", "SLSsteam", "path", "steam-native")),
            ("Desktop override", Path.Combine(homeDirectory, ".local", "share", "applications", "steam.desktop")),
            ("Desktop override", Path.Combine(homeDirectory, ".local", "share", "applications", "steam-native.desktop")),
            ("Fish startup", Path.Combine(homeDirectory, ".config", "fish", "conf.d", "SLSsteam.fish")),
            ("Flatpak override", Path.Combine(homeDirectory, ".local", "share", "flatpak", "overrides", "com.valvesoftware.Steam"))
        };

        return candidates
            .Where(candidate => File.Exists(candidate.Item2))
            .Select(candidate => new SlsSteamLaunchHook(
                candidate.Item1,
                candidate.Item2,
                ContainsSlsSteamReference(candidate.Item2)))
            .Where(hook => hook.Active)
            .ToArray();
    }

    private static bool ContainsSlsSteamReference(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || info.Length > MaximumTextFileBytes) return false;
            var text = File.ReadAllText(path, Encoding.UTF8);
            return text.Contains("SLSsteam", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("library-inject.so", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }
}
