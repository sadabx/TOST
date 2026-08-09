using System.Text;
using System.Text.RegularExpressions;

namespace Trionine.TOST.Core.Imports;

public enum SteamImportKind
{
    Lua,
    DepotManifest,
    AppManifest
}

public sealed record SteamImportInspection(
    string Path,
    SteamImportKind Kind,
    long SizeBytes,
    IReadOnlyList<string> AppIds,
    IReadOnlyList<string> DepotIds,
    IReadOnlyList<string> ManifestIds,
    IReadOnlyList<SteamLuaAppDeclaration> AppDeclarations,
    IReadOnlyList<SteamLuaTokenDeclaration> Tokens,
    IReadOnlyList<SteamLuaManifestDeclaration> Manifests);

public sealed record SteamLuaAppDeclaration(string AppId, int? Flag, string? DepotKey);
public sealed record SteamLuaTokenDeclaration(string AppId, string Token);
public sealed record SteamLuaManifestDeclaration(string DepotId, string ManifestId, long? Size);

public sealed class SteamImportInspector
{
    public const long MaximumFileBytes = 8L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex AddAppIdPattern = new(
        @"(?im)^[ \t]*addappid\s*\(\s*(?<id>\d+)\s*(?:,\s*(?<flag>\d+)\s*(?:,\s*[""'](?<key>[0-9a-fA-F]+)[""'])?)?\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AddTokenPattern = new(
        @"(?im)^[ \t]*addtoken\s*\(\s*(?<id>\d+)\s*,\s*[""'](?<token>\d+)[""']\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SetManifestPattern = new(
        @"(?im)^[ \t]*setmanifestid\s*\(\s*(?<depot>\d+)\s*,\s*[""']?(?<manifest>\d+)[""']?\s*(?:,\s*(?<size>\d+)\s*)?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DepotManifestNamePattern = new(
        @"^(?<depot>\d+)_(?<manifest>\d+)\.manifest$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AppManifestNamePattern = new(
        @"^appmanifest_(?<app>\d+)\.acf$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public SteamImportInspection Inspect(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.LinkTarget is not null)
            throw new InvalidDataException("Import input must be an existing regular file.");
        if (info.Length == 0 || info.Length > MaximumFileBytes)
            throw new InvalidDataException($"Import files must be between 1 and {MaximumFileBytes} bytes.");

        var fileName = info.Name;
        if (fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(fullPath);
            string text;
            try { text = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("Lua input is not valid UTF-8.", ex); }
            if (text.IndexOf('\0') >= 0) throw new InvalidDataException("Lua input contains null bytes.");

            var uncommentedText = RemoveSupportedComments(text);
            var appDeclarations = AddAppIdPattern.Matches(uncommentedText).Select(match =>
                new SteamLuaAppDeclaration(match.Groups["id"].Value,
                    match.Groups["flag"].Success ? int.Parse(match.Groups["flag"].Value) : null,
                    match.Groups["key"].Success ? match.Groups["key"].Value : null)).ToArray();
            var appIds = appDeclarations.Select(item => item.AppId)
                .Distinct(StringComparer.Ordinal).ToArray();
            var manifestMatches = SetManifestPattern.Matches(uncommentedText);
            var manifests = manifestMatches.Select(match => new SteamLuaManifestDeclaration(
                match.Groups["depot"].Value, match.Groups["manifest"].Value,
                match.Groups["size"].Success ? long.Parse(match.Groups["size"].Value) : null)).ToArray();
            var tokens = AddTokenPattern.Matches(uncommentedText).Select(match =>
                new SteamLuaTokenDeclaration(match.Groups["id"].Value, match.Groups["token"].Value)).ToArray();
            var depotIds = manifests.Select(item => item.DepotId)
                .Distinct(StringComparer.Ordinal).ToArray();
            var manifestIds = manifests.Select(item => item.ManifestId)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (appIds.Length == 0) throw new InvalidDataException("Lua input contains no supported addappid declarations.");
            return new SteamImportInspection(fullPath, SteamImportKind.Lua, info.Length, appIds, depotIds, manifestIds, appDeclarations, tokens, manifests);
        }

        var depotMatch = DepotManifestNamePattern.Match(fileName);
        if (depotMatch.Success)
            return new SteamImportInspection(fullPath, SteamImportKind.DepotManifest, info.Length, [],
                [depotMatch.Groups["depot"].Value], [depotMatch.Groups["manifest"].Value], [], [], []);

        var appMatch = AppManifestNamePattern.Match(fileName);
        if (appMatch.Success)
        {
            var manifest = new SteamAppManifestParser().Parse(fullPath);
            if (!manifest.AppId.Equals(appMatch.Groups["app"].Value, StringComparison.Ordinal))
                throw new InvalidDataException("App manifest filename AppID does not match its AppState appid.");
            return new SteamImportInspection(fullPath, SteamImportKind.AppManifest, info.Length,
                [manifest.AppId], [], [], [], [], []);
        }

        throw new InvalidDataException("Supported files are *.lua, <depot>_<manifest>.manifest, and appmanifest_<appid>.acf.");
    }

    private static string RemoveSupportedComments(string text)
    {
        var withoutBlocks = Regex.Replace(
            text,
            @"--\[\[.*?\]\]",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return Regex.Replace(
            withoutBlocks,
            @"(?m)--.*$",
            string.Empty,
            RegexOptions.CultureInvariant);
    }
}
