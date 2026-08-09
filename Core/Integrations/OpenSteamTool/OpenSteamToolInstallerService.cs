using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Integrations.OpenSteamTool;

public sealed record OpenSteamToolRelease(string Tag, string AssetName, Uri DownloadUri);
public sealed record OpenSteamToolFileResult(string Name, string? Destination, string? Error)
{
    public bool Success => Error is null;
}

public sealed record OpenSteamToolInstallResult(string? Tag, IReadOnlyList<OpenSteamToolFileResult> Files)
{
    public int ImportedCount => Files.Count(file => file.Success);
    public int FailureCount => Files.Count - ImportedCount;
    public bool Success => ImportedCount > 0 && FailureCount == 0;

    public string ToMessage()
    {
        var lines = new List<string>();
        if (Tag is not null)
        {
            lines.Add($"OpenSteamTool {Tag}");
        }

        lines.Add($"Imported {ImportedCount} file{(ImportedCount == 1 ? string.Empty : "s")}.");
        foreach (var failure in Files.Where(file => !file.Success))
        {
            lines.Add(IsLockedOrDenied(failure.Error)
                ? $"Could not replace {failure.Name} because Steam or another process is using it."
                : $"Skipped {failure.Name}: {failure.Error}");
        }

        if (Files.Any(file => !file.Success && IsLockedOrDenied(file.Error)))
        {
            lines.Add(string.Empty);
            lines.Add("Close Steam completely using Steam > Exit, wait for its tray icon to disappear, then run Install / Repair again.");
            lines.Add("If Steam is already closed, restart TOST as administrator and verify that your Steam folder is writable.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsLockedOrDenied(string? error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("access", StringComparison.OrdinalIgnoreCase) && error.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("being used", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("sharing violation", StringComparison.OrdinalIgnoreCase));
}

public sealed class OpenSteamToolInstallerService
{
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const long MaximumEntryBytes = 256L * 1024 * 1024;
    private const long MaximumPayloadBytes = 512L * 1024 * 1024;
    private const int MaximumArchiveEntries = 10_000;
    private static readonly Uri LatestReleaseUri = new("https://github.com/OpenSteam001/OpenSteamTool/releases/latest");
    private readonly HttpClient client;

    public OpenSteamToolInstallerService(HttpClient client)
    {
        this.client = client;
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/2.0 (+https://github.com/sadabx/TOST)");
        }
    }

    public async Task<OpenSteamToolInstallResult> InstallLatestAsync(
        SteamInstallation steam,
        bool overwrite,
        bool backupBeforeOverwrite,
        CancellationToken cancellationToken = default)
    {
        EnsureWindowsSteam(steam);
        var release = await GetLatestAsync(cancellationToken);
        var temporaryArchive = Path.Combine(Path.GetTempPath(), $"TOST-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(release.DownloadUri, temporaryArchive, cancellationToken);
            var files = InstallArchive(temporaryArchive, steam, overwrite, backupBeforeOverwrite);
            return new OpenSteamToolInstallResult(release.Tag, files);
        }
        finally
        {
            try { File.Delete(temporaryArchive); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public OpenSteamToolInstallResult Import(
        SteamInstallation steam,
        IEnumerable<string> inputPaths,
        bool overwrite,
        bool backupBeforeOverwrite)
    {
        EnsureWindowsSteam(steam);
        var results = new List<OpenSteamToolFileResult>();
        foreach (var input in inputPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ImportPath(input, steam, overwrite, backupBeforeOverwrite, results);
        }

        return new OpenSteamToolInstallResult(null, results);
    }

    public async Task<OpenSteamToolRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var latestResponse = await client.GetAsync(
            LatestReleaseUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        latestResponse.EnsureSuccessStatusCode();
        var releaseUri = latestResponse.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("GitHub did not return the latest OpenSteamTool release URL.");
        var segments = releaseUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var marker = Array.FindIndex(segments, value => value.Equals("tag", StringComparison.OrdinalIgnoreCase));
        if (marker < 0 || marker + 1 >= segments.Length)
        {
            throw new InvalidDataException("Could not determine the latest OpenSteamTool release tag.");
        }

        var tag = Uri.UnescapeDataString(segments[marker + 1]);
        var assetsUri = new Uri($"https://github.com/OpenSteam001/OpenSteamTool/releases/expanded_assets/{Uri.EscapeDataString(tag)}");
        var html = await client.GetStringAsync(assetsUri, cancellationToken);
        var paths = Regex.Matches(
                html,
                "href=\"(?<path>/OpenSteam001/OpenSteamTool/releases/download/[^\"]+\\.zip)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => WebUtility.HtmlDecode(match.Groups["path"].Value))
            .Where(path =>
                Path.GetFileName(path).StartsWith("OpenSteamTool-", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).EndsWith("-Release.zip", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length != 1)
        {
            throw new InvalidDataException(paths.Length == 0
                ? "The latest OpenSteamTool release does not contain a release ZIP."
                : "The latest OpenSteamTool release contains multiple matching release ZIPs.");
        }

        var download = new Uri(new Uri("https://github.com"), paths[0]);
        return new OpenSteamToolRelease(tag, Path.GetFileName(download.LocalPath), download);
    }

    private async Task DownloadAsync(Uri source, string destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long size && size > MaximumDownloadBytes)
        {
            throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied += read;
            if (copied > MaximumDownloadBytes)
            {
                throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private void ImportPath(
        string input,
        SteamInstallation steam,
        bool overwrite,
        bool backup,
        ICollection<OpenSteamToolFileResult> results)
    {
        if (Directory.Exists(input))
        {
            foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
            {
                ImportPath(file, steam, overwrite, backup, results);
            }

            return;
        }

        if (!File.Exists(input))
        {
            results.Add(new(Path.GetFileName(input), null, "Path does not exist."));
            return;
        }

        if (Path.GetExtension(input).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var result in InstallArchive(input, steam, overwrite, backup))
            {
                results.Add(result);
            }

            return;
        }

        var destinationDirectory = ResolveDestination(steam, Path.GetFileName(input));
        if (destinationDirectory is null)
        {
            results.Add(new(Path.GetFileName(input), null, "Unexpected file type or name."));
            return;
        }

        results.Add(CopyFile(input, destinationDirectory, overwrite, backup));
    }

    private IReadOnlyList<OpenSteamToolFileResult> InstallArchive(
        string archivePath,
        SteamInstallation steam,
        bool overwrite,
        bool backup)
    {
        var results = new List<OpenSteamToolFileResult>();
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException($"Archive contains more than {MaximumArchiveEntries:N0} entries.");
        }

        var entries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) && ResolveDestination(steam, entry.Name) is not null)
            .ToArray();
        if (entries.Length == 0)
        {
            throw new InvalidDataException("Archive contains no supported OpenSteamTool files.");
        }

        if (entries.Any(entry => entry.Length > MaximumEntryBytes) || entries.Sum(entry => entry.Length) > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Archive payload is larger than the supported limit.");
        }

        var duplicates = entries.GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
        {
            throw new InvalidDataException($"Archive contains duplicate supported file {duplicates.Key}.");
        }

        foreach (var entry in entries)
        {
            var destinationDirectory = ResolveDestination(steam, entry.Name)!;
            string? temporary = null;
            try
            {
                Directory.CreateDirectory(destinationDirectory);
                temporary = Path.Combine(destinationDirectory, $".{entry.Name}.{Guid.NewGuid():N}.tost-tmp");
                using (var input = entry.Open())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                }

                results.Add(MoveTemporary(temporary, entry.Name, destinationDirectory, overwrite, backup));
                temporary = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                results.Add(new(entry.Name, null, ex.Message));
            }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); } catch { }
                }
            }
        }

        return results;
    }

    private static OpenSteamToolFileResult CopyFile(string source, string destinationDirectory, bool overwrite, bool backup)
    {
        var name = Path.GetFileName(source);
        Directory.CreateDirectory(destinationDirectory);
        var temporary = Path.Combine(destinationDirectory, $".{name}.{Guid.NewGuid():N}.tost-tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            var result = MoveTemporary(temporary, name, destinationDirectory, overwrite, backup);
            temporary = string.Empty;
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(name, null, ex.Message);
        }
        finally
        {
            if (temporary.Length > 0)
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    private static OpenSteamToolFileResult MoveTemporary(
        string temporary,
        string name,
        string destinationDirectory,
        bool overwrite,
        bool backup)
    {
        var destination = Path.Combine(destinationDirectory, name);
        if (File.Exists(destination))
        {
            if (!overwrite)
            {
                throw new IOException("Destination already exists and overwriting is disabled.");
            }

            if (backup)
            {
                var backupPath = destination + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destination, backupPath, overwrite: false);
            }
        }

        File.Move(temporary, destination, overwrite);
        return new(name, destination, null);
    }

    private static string? ResolveDestination(SteamInstallation steam, string fileName)
    {
        if (fileName.Equals("opensteamtool.toml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("OpenSteamTool.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return steam.RootPath;
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return steam.ManagedScriptsPath;
        }

        if (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".acf", StringComparison.OrdinalIgnoreCase) && fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase))
        {
            return steam.SteamAppsPath;
        }

        return null;
    }

    private static void EnsureWindowsSteam(SteamInstallation steam)
    {
        if (steam.Kind != SteamInstallationKind.Windows)
        {
            throw new ArgumentException("OpenSteamTool can only be installed into Windows Steam.", nameof(steam));
        }

        if (!Directory.Exists(steam.RootPath))
        {
            throw new DirectoryNotFoundException($"Steam folder not found: {steam.RootPath}");
        }
    }
}
