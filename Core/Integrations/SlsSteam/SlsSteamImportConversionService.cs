using Trionine.TOST.Core.Imports;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamManifestOverride(string DepotId, string ManifestId, long? Size);
public sealed record SlsSteamImportConversionPlan(
    IReadOnlyList<string> AdditionalApps,
    IReadOnlyDictionary<string, string> AppTokens,
    IReadOnlyList<SlsSteamManifestOverride> ManifestIds,
    IReadOnlyDictionary<string, string> DepotKeys,
    IReadOnlyList<string> Warnings);

public sealed class SlsSteamImportConversionService
{
    public SlsSteamImportConversionPlan CreatePlan(IEnumerable<SteamImportInspection> inspections)
    {
        var inputs = inspections.Where(item => item.Kind == SteamImportKind.Lua).ToArray();
        var apps = inputs.SelectMany(item => item.AppDeclarations).Select(item => item.AppId)
            .Distinct(StringComparer.Ordinal).OrderBy(ulong.Parse).ToArray();
        var tokens = LastValues(inputs.SelectMany(item => item.Tokens).Select(item => (item.AppId, item.Token)));
        var keys = LastValues(inputs.SelectMany(item => item.AppDeclarations)
            .Where(item => item.DepotKey is not null).Select(item => (item.AppId, item.DepotKey!)));
        var manifests = inputs.SelectMany(item => item.Manifests)
            .GroupBy(item => item.DepotId, StringComparer.Ordinal).Select(group => group.Last())
            .OrderBy(item => ulong.Parse(item.DepotId))
            .Select(item => new SlsSteamManifestOverride(item.DepotId, item.ManifestId, item.Size)).ToArray();
        var warnings = new List<string>();
        if (keys.Count > 0)
            warnings.Add("Depot keys are not part of SLSsteam config.yaml and must be registered separately in Steam config.vdf.");
        warnings.Add("Lua addappid declarations do not identify which IDs are DLC; DlcData requires explicit app-to-DLC mapping.");
        return new SlsSteamImportConversionPlan(apps, tokens, manifests, keys, warnings);
    }

    private static IReadOnlyDictionary<string, string> LastValues(IEnumerable<(string Key, string Value)> values)
        => values.GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
}
