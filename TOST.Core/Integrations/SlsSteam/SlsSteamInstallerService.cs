using System.Net.Http.Headers;
using System.Security.Cryptography;
using SharpCompress.Archives;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamInstallPreview(string Tag, SlsSteamReleaseAsset Asset, IReadOnlyList<string> Destinations, bool CanInstall, string? BlockReason);
public sealed record SlsSteamInstallResult(string Tag, IReadOnlyList<string> InstalledFiles);

public sealed class SlsSteamInstallerService
{
    private const long MaximumDownloadBytes = 32L * 1024 * 1024;
    private const long MaximumExtractedBytes = 64L * 1024 * 1024;
    private static readonly HashSet<string> RequiredFiles = new(StringComparer.Ordinal) { "SLSsteam.so", "library-inject.so" };
    private readonly HttpClient httpClient;

    public SlsSteamInstallerService(HttpClient httpClient) => this.httpClient = httpClient;

    public SlsSteamInstallPreview Preview(SlsSteamRelease release, SlsSteamPaths paths)
    {
        var asset = release.Assets.FirstOrDefault(item => item.Name.Equals("SLSsteam-Any.7z", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The pinned SLSsteam release has no portable SLSsteam-Any.7z asset.");
        if (asset.Sha256 is null || asset.Sha256.Length != 64 || asset.Sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The pinned SLSsteam asset has no valid published SHA-256 digest.");
        if (asset.SizeBytes is <= 0 or > MaximumDownloadBytes)
            throw new InvalidDataException("The pinned SLSsteam asset size is outside the allowed range.");
        var destinations = RequiredFiles.Select(file => Path.Combine(paths.DataDirectory, file)).ToArray();
        var existing = destinations.Where(File.Exists).Select(Path.GetFileName).ToArray();
        return new SlsSteamInstallPreview(release.Tag, asset, destinations, existing.Length == 0,
            existing.Length == 0 ? null : $"Existing managed files must be removed or archived first: {string.Join(", ", existing)}");
    }

    public async Task<SlsSteamInstallResult> InstallAsync(SlsSteamRelease release, SlsSteamPaths paths, CancellationToken cancellationToken = default)
    {
        var preview = Preview(release, paths);
        if (!preview.CanInstall) throw new IOException(preview.BlockReason);
        var archiveBytes = await DownloadVerifiedAsync(preview.Asset, cancellationToken);
        var extracted = ExtractRequiredFiles(archiveBytes);
        Directory.CreateDirectory(paths.DataDirectory);
        var installed = new List<string>();
        try
        {
            foreach (var fileName in RequiredFiles)
            {
                var destination = Path.Combine(paths.DataDirectory, fileName);
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(extracted[fileName]);
                output.Flush(flushToDisk: true);
                installed.Add(destination);
            }
        }
        catch
        {
            foreach (var path in installed) if (File.Exists(path)) File.Delete(path);
            throw;
        }
        return new SlsSteamInstallResult(release.Tag, installed);
    }

    private async Task<byte[]> DownloadVerifiedAsync(SlsSteamReleaseAsset asset, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TOST", "1.2.0"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes ||
            response.Content.Headers.ContentLength is { } length && length != asset.SizeBytes)
            throw new InvalidDataException("SLSsteam download size does not match the pinned release metadata.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream((int)asset.SizeBytes);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > MaximumDownloadBytes) throw new InvalidDataException("SLSsteam download exceeded the size limit.");
            destination.Write(buffer, 0, read);
        }
        var bytes = destination.ToArray();
        if (bytes.LongLength != asset.SizeBytes) throw new InvalidDataException("SLSsteam download was incomplete.");
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!digest.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SLSsteam SHA-256 verification failed.");
        return bytes;
    }

    private static Dictionary<string, byte[]> ExtractRequiredFiles(byte[] archiveBytes)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = ArchiveFactory.OpenArchive(stream);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in archive.Entries.Where(item => !item.IsDirectory))
        {
            if (string.IsNullOrWhiteSpace(entry.Key)) continue;
            var fileName = Path.GetFileName(entry.Key.Replace('\\', '/'));
            if (!RequiredFiles.Contains(fileName)) continue;
            if (result.ContainsKey(fileName)) throw new InvalidDataException($"SLSsteam archive contains duplicate {fileName} entries.");
            if (entry.Size is <= 0 || total + entry.Size > MaximumExtractedBytes)
                throw new InvalidDataException("SLSsteam archive expands beyond the allowed size.");
            using var input = entry.OpenEntryStream();
            using var output = new MemoryStream((int)entry.Size);
            input.CopyTo(output);
            if (output.Length != entry.Size) throw new InvalidDataException($"SLSsteam archive entry {fileName} is incomplete.");
            result[fileName] = output.ToArray();
            total += entry.Size;
        }
        var missing = RequiredFiles.Where(file => !result.ContainsKey(file)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException($"SLSsteam archive is missing: {string.Join(", ", missing)}.");
        return result;
    }
}
