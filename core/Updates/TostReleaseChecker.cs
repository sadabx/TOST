using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trionine.TOST.Core.Updates;

public enum LinuxPackageKind
{
    ArchPackage,
    DebianPackage,
    AppImage,
    Tarball
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

public sealed record LinuxUpdateInfo(
    string TagName,
    Version Version,
    string AssetName,
    string DownloadUrl,
    LinuxPackageKind Kind,
    long Size);

public static class TostReleaseChecker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim().TrimStart('v', 'V');
        var match = Regex.Match(trimmed, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return false;

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        version = new Version(major, minor, patch);
        return true;
    }

    public static bool IsLinuxRelease(string tagName, string? releaseName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return false;

        if (tagName.EndsWith("L", StringComparison.OrdinalIgnoreCase) ||
            tagName.EndsWith("-linux", StringComparison.OrdinalIgnoreCase) ||
            tagName.Contains("-linux-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(releaseName) &&
            releaseName.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static LinuxPackageKind DetectSystemKind(
        string? processPath,
        bool isArch,
        bool isDebian,
        string? appImageEnv)
    {
        if (!string.IsNullOrEmpty(appImageEnv))
        {
            return LinuxPackageKind.AppImage;
        }

        if (isArch || (processPath != null && processPath.StartsWith("/usr/bin/tost", StringComparison.Ordinal)))
        {
            return LinuxPackageKind.ArchPackage;
        }

        if (isDebian)
        {
            return LinuxPackageKind.DebianPackage;
        }

        return LinuxPackageKind.Tarball;
    }

    public static (GitHubAsset? Asset, LinuxPackageKind Kind) SelectAsset(GitHubRelease release, LinuxPackageKind preferredKind)
    {
        GitHubAsset? asset = null;
        switch (preferredKind)
        {
            case LinuxPackageKind.ArchPackage:
                asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".pkg.tar.zst", StringComparison.OrdinalIgnoreCase));
                if (asset != null) return (asset, LinuxPackageKind.ArchPackage);
                break;

            case LinuxPackageKind.DebianPackage:
                asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase));
                if (asset != null) return (asset, LinuxPackageKind.DebianPackage);
                break;

            case LinuxPackageKind.AppImage:
                asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
                if (asset != null) return (asset, LinuxPackageKind.AppImage);
                break;
        }

        // Fallback to tarball
        asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith("linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase));
        if (asset != null) return (asset, LinuxPackageKind.Tarball);

        // Fallback to AppImage
        asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));
        if (asset != null) return (asset, LinuxPackageKind.AppImage);

        // Fallback to Arch package
        asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".pkg.tar.zst", StringComparison.OrdinalIgnoreCase));
        if (asset != null) return (asset, LinuxPackageKind.ArchPackage);

        // Fallback to Debian package
        asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase));
        if (asset != null) return (asset, LinuxPackageKind.DebianPackage);

        return (null, preferredKind);
    }

    public static LinuxUpdateInfo? FindLatestLinuxUpdate(
        IEnumerable<GitHubRelease> releases,
        Version currentVersion,
        LinuxPackageKind preferredKind)
    {
        foreach (var release in releases)
        {
            if (release.Draft) continue;

            if (!IsLinuxRelease(release.TagName, release.Name)) continue;

            if (!TryParseVersion(release.TagName, out var remoteVersion)) continue;

            if (remoteVersion > currentVersion)
            {
                var (asset, kind) = SelectAsset(release, preferredKind);
                if (asset != null)
                {
                    return new LinuxUpdateInfo(
                        release.TagName,
                        remoteVersion,
                        asset.Name,
                        asset.DownloadUrl,
                        kind,
                        asset.Size);
                }
            }

            // Since releases are returned in descending chronological order,
            // the first valid Linux release is the latest. If it's not newer, we are current.
            break;
        }

        return null;
    }

    public static Version GetCurrentAssemblyVersion(Assembly assembly)
    {
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (infoVersion != null)
        {
            var clean = infoVersion.Split('+')[0];
            if (TryParseVersion(clean, out var v)) return v;
        }

        var asmVersion = assembly.GetName().Version;
        if (asmVersion != null)
        {
            return new Version(asmVersion.Major, asmVersion.Minor, Math.Max(0, asmVersion.Build));
        }

        return new Version(0, 5, 0);
    }

    public static async Task<List<GitHubRelease>> FetchReleasesAsync(HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/sadabx/TOST/releases");
        request.Headers.UserAgent.ParseAdd("TOST-Linux-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github.v3+json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, JsonOptions, cancellationToken)
            ?? new List<GitHubRelease>();
    }
}
