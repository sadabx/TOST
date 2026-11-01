using System.Text;
using System.Text.RegularExpressions;

namespace Trionine.TOST.Core.Imports;

public sealed record SteamAppManifest(string Path, string AppId, string? Name, string? InstallDirectory, uint? StateFlags);

public sealed class SteamAppManifestParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex PairPattern = new(
        "\"(?<key>(?:[^\"\\\\]|\\\\.)*)\"\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SteamAppManifest Parse(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > SteamImportInspector.MaximumFileBytes)
            throw new InvalidDataException("App manifest must be a bounded regular file.");

        string text;
        try { text = StrictUtf8.GetString(File.ReadAllBytes(fullPath)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("App manifest is not valid UTF-8.", ex); }
        if (text.IndexOf('\0') >= 0 || !text.Contains("\"AppState\"", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File is not a Steam AppState manifest.");

        var values = PairPattern.Matches(text)
            .GroupBy(match => match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Unescape(group.Last().Groups["value"].Value), StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("appid", out var appId) || !ulong.TryParse(appId, out _))
            throw new InvalidDataException("App manifest has no valid appid value.");

        uint? stateFlags = values.TryGetValue("StateFlags", out var rawFlags) && uint.TryParse(rawFlags, out var flags) ? flags : null;
        values.TryGetValue("name", out var name);
        values.TryGetValue("installdir", out var installDirectory);
        return new SteamAppManifest(fullPath, appId, name, installDirectory, stateFlags);
    }

    private static string Unescape(string value) => value.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
