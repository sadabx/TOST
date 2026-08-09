using System.Net.Http.Headers;
using System.Text.Json;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamReleaseAsset(
    string Name,
    long SizeBytes,
    Uri DownloadUri,
    string? Sha256);

public sealed record SlsSteamRelease(
    string Tag,
    DateTimeOffset PublishedAt,
    Uri ReleaseUri,
    IReadOnlyList<SlsSteamReleaseAsset> Assets);

public sealed class SlsSteamReleaseService
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly Uri LatestReleaseApi = new(
        "https://api.github.com/repos/AceSLS/SLSsteam/releases/latest");
    private readonly HttpClient httpClient;

    public SlsSteamReleaseService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<SlsSteamRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TOST", "2.0.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new InvalidDataException("The SLSsteam release response was larger than expected.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limitedStream = new LimitedReadStream(responseStream, MaximumResponseBytes);
        using var document = await JsonDocument.ParseAsync(limitedStream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = RequiredString(root, "tag_name");
        var releaseUri = RequiredHttpsUri(root, "html_url", "github.com");
        if (!root.TryGetProperty("published_at", out var publishedElement) ||
            !publishedElement.TryGetDateTimeOffset(out var publishedAt))
        {
            throw new InvalidDataException("GitHub returned an invalid SLSsteam publication date.");
        }

        var assets = new List<SlsSteamReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = RequiredString(asset, "name");
                if (!name.Equals("SLSsteam-Any.7z", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("SLSsteam-Arch.pkg.tar.zst", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var downloadUri = RequiredHttpsUri(asset, "browser_download_url", "github.com");
                var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : throw new InvalidDataException($"GitHub returned an invalid size for {name}.");
                var digest = asset.TryGetProperty("digest", out var digestElement)
                    ? digestElement.GetString()
                    : null;
                var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                    ? digest[7..]
                    : null;
                assets.Add(new SlsSteamReleaseAsset(name, size, downloadUri, sha256));
            }
        }

        return new SlsSteamRelease(tag, publishedAt, releaseUri, assets);
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"GitHub did not return a valid {property} value.");
        }

        return value.GetString()!;
    }

    private static Uri RequiredHttpsUri(JsonElement element, string property, string requiredHost)
    {
        var value = RequiredString(element, property);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(requiredHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GitHub returned an invalid {property} URL.");
        }

        return uri;
    }

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long bytesRead;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => bytesRead; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);
            return read;
        }
        private void Count(int count)
        {
            bytesRead += count;
            if (bytesRead > maximumBytes)
            {
                throw new InvalidDataException("The SLSsteam release response was larger than expected.");
            }
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
