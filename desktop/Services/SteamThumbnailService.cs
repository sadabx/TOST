using Avalonia.Media.Imaging;

namespace Trionine.TOST.Desktop.Services;

internal static class SteamThumbnailService
{
    private static readonly string CacheDirectory = Path.Combine(DesktopPaths.DataRoot, "cache", "thumbnails");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly Dictionary<string, Bitmap?> MemoryCache = new(StringComparer.Ordinal);

    static SteamThumbnailService()
    {
        try { Directory.CreateDirectory(CacheDirectory); } catch { }
    }

    public static async Task<Bitmap?> GetThumbnailAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId) || !appId.All(char.IsDigit)) return null;

        lock (MemoryCache)
        {
            if (MemoryCache.TryGetValue(appId, out var cached)) return cached;
        }

        var localPath = Path.Combine(CacheDirectory, $"{appId}.jpg");
        if (File.Exists(localPath))
        {
            try
            {
                var bitmap = new Bitmap(localPath);
                lock (MemoryCache) MemoryCache[appId] = bitmap;
                return bitmap;
            }
            catch
            {
                try { File.Delete(localPath); } catch { }
            }
        }

        try
        {
            var url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
            var bytes = await Client.GetByteArrayAsync(url, cancellationToken);
            if (bytes.Length > 0)
            {
                await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
                using var ms = new MemoryStream(bytes);
                var bitmap = new Bitmap(ms);
                lock (MemoryCache) MemoryCache[appId] = bitmap;
                return bitmap;
            }
        }
        catch
        {
            // Fall back gracefully when offline or image not found
        }

        lock (MemoryCache) MemoryCache[appId] = null;
        return null;
    }
}
