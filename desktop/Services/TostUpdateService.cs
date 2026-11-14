using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using Trionine.TOST.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Trionine.TOST.Desktop.Services;

internal sealed record TostUpdateAvailability(bool InstalledBuild, string? Version, object? State);

internal sealed class TostUpdateService
{
    private const string UpdateUrl = "https://github.com/sadabx/TOST/releases/latest/download";
    private readonly UpdateManager? manager;

    public TostUpdateService()
    {
        if (OperatingSystem.IsWindows())
        {
            manager = new UpdateManager(new SimpleWebSource(UpdateUrl));
        }
    }

    public async Task<TostUpdateAvailability> CheckAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            if (manager is null || !manager.IsInstalled)
            {
                return new TostUpdateAvailability(false, null, null);
            }

            var update = await manager.CheckForUpdatesAsync();
            return new TostUpdateAvailability(
                true,
                update?.TargetFullRelease.Version.ToString(),
                update);
        }

        if (OperatingSystem.IsLinux())
        {
            return await CheckLinuxAsync();
        }

        return new TostUpdateAvailability(false, null, null);
    }

    public async Task DownloadAndApplyAsync(TostUpdateAvailability availability)
    {
        if (OperatingSystem.IsWindows())
        {
            if (manager is null || availability.State is not UpdateInfo update)
            {
                throw new InvalidOperationException("No TOST update is ready to download.");
            }

            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease, null);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            if (availability.State is not LinuxUpdateInfo updateInfo)
            {
                throw new InvalidOperationException("No TOST Linux update is ready to download.");
            }

            await ApplyLinuxUpdateAsync(updateInfo);
            return;
        }

        throw new PlatformNotSupportedException("TOST auto-updates are only supported on Windows and Linux.");
    }

    private static async Task<TostUpdateAvailability> CheckLinuxAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var releases = await TostReleaseChecker.FetchReleasesAsync(client);
            var currentVersion = TostReleaseChecker.GetCurrentAssemblyVersion(typeof(TostUpdateService).Assembly);
            var preferredKind = TostReleaseChecker.DetectSystemKind(
                Environment.ProcessPath,
                File.Exists("/etc/arch-release"),
                File.Exists("/etc/debian_version"),
                Environment.GetEnvironmentVariable("APPIMAGE"));

            var updateInfo = TostReleaseChecker.FindLatestLinuxUpdate(releases, currentVersion, preferredKind);
            if (updateInfo != null)
            {
                return new TostUpdateAvailability(true, updateInfo.TagName, updateInfo);
            }

            return new TostUpdateAvailability(true, null, null);
        }
        catch (Exception ex)
        {
            DesktopLog.Error($"Linux update check failed: {ex}");
            throw;
        }
    }

    private static async Task ApplyLinuxUpdateAsync(LinuxUpdateInfo updateInfo)
    {
        var cacheDir = Path.Combine(DesktopPaths.DataRoot, "cache", "updates");
        Directory.CreateDirectory(cacheDir);
        var targetFile = Path.Combine(cacheDir, updateInfo.AssetName);

        DesktopLog.Info($"Downloading Linux update asset {updateInfo.AssetName} from {updateInfo.DownloadUrl}...");
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST-Desktop-Linux");
            using var response = await client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var netStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(targetFile);
            await netStream.CopyToAsync(fileStream);
        }

        DesktopLog.Info($"Downloaded update asset to {targetFile} ({new FileInfo(targetFile).Length} bytes). Applying...");

        switch (updateInfo.Kind)
        {
            case LinuxPackageKind.ArchPackage:
                await ApplyArchPackageAsync(targetFile);
                break;

            case LinuxPackageKind.DebianPackage:
                await ApplyDebianPackageAsync(targetFile);
                break;

            case LinuxPackageKind.AppImage:
                ApplyAppImage(targetFile);
                break;

            case LinuxPackageKind.Tarball:
                ApplyTarball(targetFile);
                break;
        }
    }

    private static async Task ApplyArchPackageAsync(string packagePath)
    {
        var pkexec = FindExecutableOnPath("pkexec");
        if (pkexec != null)
        {
            var psi = new ProcessStartInfo(pkexec)
            {
                ArgumentList = { "pacman", "-U", "--noconfirm", packagePath },
                UseShellExecute = false
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to launch pkexec for package installation.");

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Package installation was canceled or failed (exit code {proc.ExitCode}).\n\nYou can install it manually by running:\nsudo pacman -U \"{packagePath}\"");
            }

            RestartApp();
            return;
        }

        throw new InvalidOperationException(
            $"Update downloaded to:\n{packagePath}\n\nPlease install it using terminal:\nsudo pacman -U \"{packagePath}\"");
    }

    private static async Task ApplyDebianPackageAsync(string packagePath)
    {
        var pkexec = FindExecutableOnPath("pkexec");
        if (pkexec != null)
        {
            var psi = new ProcessStartInfo(pkexec)
            {
                ArgumentList = { "dpkg", "-i", packagePath },
                UseShellExecute = false
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to launch pkexec for Debian package installation.");

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Debian package installation was canceled or failed (exit code {proc.ExitCode}).\n\nYou can install it manually by running:\nsudo dpkg -i \"{packagePath}\"");
            }

            RestartApp();
            return;
        }

        throw new InvalidOperationException(
            $"Update downloaded to:\n{packagePath}\n\nPlease install it using terminal:\nsudo dpkg -i \"{packagePath}\"");
    }

    private static void ApplyAppImage(string newAppImagePath)
    {
        var currentAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(newAppImagePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        if (!string.IsNullOrEmpty(currentAppImage) && File.Exists(currentAppImage))
        {
            File.Move(newAppImagePath, currentAppImage, overwrite: true);
            Process.Start(new ProcessStartInfo(currentAppImage) { UseShellExecute = true });
            Environment.Exit(0);
            return;
        }

        Process.Start(new ProcessStartInfo(newAppImagePath) { UseShellExecute = true });
        Environment.Exit(0);
    }

    private static void ApplyTarball(string tarballPath)
    {
        var targetDir = AppContext.BaseDirectory;
        ExtractTarGzSafe(tarballPath, targetDir);
        RestartApp();
    }

    private static void ExtractTarGzSafe(string tarGzPath, string targetDir)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tost-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using (var fs = File.OpenRead(tarGzPath))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                TarFile.ExtractToDirectory(gz, tempDir, overwriteFiles: true);
            }

            foreach (var file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(tempDir, file);
                var dest = Path.Combine(targetDir, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(file, dest);

                if ((relative is "tost" or "TOST.Desktop" or "tost-cli" || relative.EndsWith(".sh", StringComparison.Ordinal)) && OperatingSystem.IsLinux())
                {
                    File.SetUnixFileMode(dest,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private static void RestartApp()
    {
        var exePath = Environment.ProcessPath ?? "/usr/bin/tost";
        if (File.Exists(exePath))
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        Environment.Exit(0);
    }

    private static string? FindExecutableOnPath(string executable)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
