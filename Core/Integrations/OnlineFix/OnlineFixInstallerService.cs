using System.Net;
using System.Text.RegularExpressions;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Integrations.OnlineFix;

public sealed record OnlineFixRelease(string Tag, string AssetName, Uri DownloadUri);

public sealed record OnlineFixInstallResult(string? Tag, string? DestinationPath, bool Success, string? Error = null)
{
    public string ToMessage()
    {
        if (Success)
        {
            return $"Installed OnlineFix {(Tag is not null ? $"({Tag}) " : string.Empty)}successfully.{Environment.NewLine}{Environment.NewLine}" +
                   $"• OnlineFix.dll placed in Steam directory.{Environment.NewLine}" +
                   $"• Configured [[inject]] entry in opensteamtool.toml.{Environment.NewLine}{Environment.NewLine}" +
                   "Launch any target game with -onlinefix in its Steam launch options.";
        }

        return $"OnlineFix installation failed: {Error}";
    }
}

public sealed class OnlineFixInstallerService
{
    private const long MaximumDownloadBytes = 50L * 1024 * 1024;
    private static readonly Uri LatestReleaseUri = new("https://github.com/Ran-Mewo/OnlineFix/releases/latest");
    private const string FallbackTag = "v0.0.2";
    private readonly HttpClient client;

    public OnlineFixInstallerService(HttpClient client)
    {
        this.client = client;
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/2.0 (+https://github.com/sadabx/TOST)");
        }
    }

    public bool IsInstalled(SteamInstallation steam)
    {
        EnsureWindowsSteam(steam);
        return File.Exists(Path.Combine(steam.RootPath, "OnlineFix.dll"));
    }

    public bool Remove(SteamInstallation steam)
    {
        EnsureWindowsSteam(steam);
        var target = Path.Combine(steam.RootPath, "OnlineFix.dll");
        if (File.Exists(target))
        {
            File.Delete(target);
            return true;
        }

        return false;
    }

    public async Task<OnlineFixRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        string tag;
        try
        {
            using var latestResponse = await client.GetAsync(
                LatestReleaseUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            latestResponse.EnsureSuccessStatusCode();

            var releaseUri = latestResponse.RequestMessage?.RequestUri
                ?? throw new InvalidDataException("GitHub did not return the latest OnlineFix release URL.");
            var segments = releaseUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var marker = Array.FindIndex(segments, value => value.Equals("tag", StringComparison.OrdinalIgnoreCase));
            if (marker < 0 || marker + 1 >= segments.Length)
            {
                tag = FallbackTag;
            }
            else
            {
                tag = Uri.UnescapeDataString(segments[marker + 1]);
            }
        }
        catch
        {
            tag = FallbackTag;
        }

        var assetsUri = new Uri($"https://github.com/Ran-Mewo/OnlineFix/releases/expanded_assets/{Uri.EscapeDataString(tag)}");
        var html = await client.GetStringAsync(assetsUri, cancellationToken);
        var match = Regex.Match(
            html,
            "href=\"(?<path>/Ran-Mewo/OnlineFix/releases/download/[^\"]+/OnlineFix\\.dll)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
        {
            var relativePath = WebUtility.HtmlDecode(match.Groups["path"].Value);
            return new OnlineFixRelease(tag, "OnlineFix.dll", new Uri($"https://github.com{relativePath}"));
        }

        return new OnlineFixRelease(tag, "OnlineFix.dll", new Uri($"https://github.com/Ran-Mewo/OnlineFix/releases/download/{tag}/OnlineFix.dll"));
    }

    public async Task<OnlineFixInstallResult> InstallLatestAsync(
        SteamInstallation steam,
        bool overwrite = true,
        bool backupBeforeOverwrite = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWindowsSteam(steam);
        var release = await GetLatestAsync(cancellationToken);
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"OnlineFix-{Guid.NewGuid():N}.dll");
        try
        {
            await DownloadAsync(release.DownloadUri, temporaryPath, cancellationToken);

            var destinationDirectory = steam.RootPath;
            Directory.CreateDirectory(destinationDirectory);
            var destination = Path.Combine(destinationDirectory, "OnlineFix.dll");

            if (File.Exists(destination))
            {
                if (!overwrite)
                {
                    return new OnlineFixInstallResult(release.Tag, destination, false, "OnlineFix.dll already exists and overwriting is disabled.");
                }

                if (backupBeforeOverwrite)
                {
                    var backupPath = destination + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(destination, backupPath, overwrite: false);
                }
            }

            File.Move(temporaryPath, destination, overwrite: true);
            EnsureOpenSteamToolConfig(steam);
            return new OnlineFixInstallResult(release.Tag, destination, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            return new OnlineFixInstallResult(release.Tag, null, false, ex.Message);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    private static void EnsureOpenSteamToolConfig(SteamInstallation steam)
    {
        var configPath = Path.Combine(steam.RootPath, "opensteamtool.toml");
        const string injectBlock = "\n[[inject]]\npath = \"OnlineFix.dll\"\nwhen_cmdline = \"-onlinefix\"\n";

        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, "# Generated by TOST\n" + injectBlock.TrimStart());
            return;
        }

        var existingContent = File.ReadAllText(configPath);
        if (!existingContent.Contains("OnlineFix.dll", StringComparison.OrdinalIgnoreCase))
        {
            File.AppendAllText(configPath, Environment.NewLine + injectBlock.TrimStart());
        }
    }

    private async Task DownloadAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaximumDownloadBytes)
        {
            throw new InvalidDataException($"OnlineFix download exceeded maximum size limit ({contentLength} bytes).");
        }

        using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidDataException("OnlineFix payload exceeded maximum allowed size.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void EnsureWindowsSteam(SteamInstallation steam)
    {
        if (steam.Kind != SteamInstallationKind.Windows)
        {
            throw new ArgumentException("OnlineFix can only be installed into Windows Steam.", nameof(steam));
        }

        if (!Directory.Exists(steam.RootPath))
        {
            throw new DirectoryNotFoundException($"Steam folder not found: {steam.RootPath}");
        }
    }
}
