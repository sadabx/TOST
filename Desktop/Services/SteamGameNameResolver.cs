using System.Text.Json;

namespace Trionine.TOST.Desktop.Services;

internal static class SteamGameNameResolver
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly string CachePath = Path.Combine(DesktopPaths.DataRoot, "steam-game-names.json");
    private static readonly HttpClient Client = CreateClient();

    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> appIds,
        CancellationToken cancellationToken = default)
    {
        var requested = appIds
            .Where(id => id.Length > 0 && id.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var cache = LoadCache();
        var changed = false;
        foreach (var appId in requested.Where(id => !cache.ContainsKey(id)))
        {
            try
            {
                var name = await FetchNameAsync(appId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cache[appId] = name;
                    changed = true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
            {
                DesktopLog.Error($"Steam name lookup failed for App {appId}: {ex.Message}");
            }
        }

        if (changed)
        {
            SaveCache(cache);
        }

        return cache
            .Where(pair => requested.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static async Task<string?> FetchNameAsync(string appId, CancellationToken cancellationToken)
    {
        var uri = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=english";
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException("The Steam Store response was larger than expected.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > MaximumResponseBytes)
        {
            throw new InvalidDataException("The Steam Store response was larger than expected.");
        }

        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty(appId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("name", out var name))
        {
            return null;
        }

        return name.GetString()?.Trim();
    }

    private static Dictionary<string, string> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath))
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DesktopLog.Error($"Could not read the Steam name cache: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void SaveCache(Dictionary<string, string> cache)
    {
        try
        {
            Directory.CreateDirectory(DesktopPaths.DataRoot);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DesktopLog.Error($"Could not save the Steam name cache: {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/2.0 (+https://github.com/sadabx/TOST)");
        return client;
    }
}
