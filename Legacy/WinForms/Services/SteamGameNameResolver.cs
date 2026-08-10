using System.Text.Json;

namespace Trionine.TOST;

internal static class SteamGameNameResolver
{
    private const long MaxResponseBytes = 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> appIds,
        InstallerLogger logger,
        CancellationToken cancellationToken)
    {
        var cache = LoadCache(logger);
        var requestedIds = appIds
            .Where(appId => !string.IsNullOrWhiteSpace(appId) && appId.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var missingIds = requestedIds.Where(appId => !cache.ContainsKey(appId)).ToList();
        var cacheChanged = false;

        foreach (var appId in missingIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var name = await FetchNameAsync(appId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cache[appId] = name;
                    cacheChanged = true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Error($"Steam name lookup timed out for App {appId}.");
            }
            catch (Exception ex)
            {
                logger.Error($"Could not look up the Steam name for App {appId}: {ex.Message}");
            }
        }

        if (cacheChanged)
        {
            SaveCache(cache, logger);
        }

        return cache
            .Where(pair => requestedIds.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static async Task<string?> FetchNameAsync(string appId, CancellationToken cancellationToken)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=english";
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException("The Steam Store response was larger than expected.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limitedStream = new LimitedReadStream(responseStream, MaxResponseBytes);
        using var document = await JsonDocument.ParseAsync(limitedStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty(appId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        return nameElement.GetString()?.Trim();
    }

    private static Dictionary<string, string> LoadCache(InstallerLogger logger)
    {
        try
        {
            if (!File.Exists(AppPaths.GameNamesCachePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var cachedNames = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(AppPaths.GameNamesCachePath));
            return cachedNames is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(cachedNames, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.Error($"Could not read the Steam game-name cache: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void SaveCache(Dictionary<string, string> cache, InstallerLogger logger)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(
                AppPaths.GameNamesCachePath,
                JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            logger.Error($"Could not save the Steam game-name cache: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/1.2 (+https://github.com/sadabx/TOST)");
        return client;
    }

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            TrackBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            TrackBytes(read);
            return read;
        }

        private void TrackBytes(int count)
        {
            bytesRead += count;
            if (bytesRead > maximumBytes)
            {
                throw new InvalidDataException("The Steam Store response was larger than expected.");
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

