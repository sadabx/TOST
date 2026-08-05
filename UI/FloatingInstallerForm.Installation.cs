using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Velopack;
using Velopack.Sources;

namespace Trionine.TOST;

internal sealed partial class FloatingInstallerForm
{
    private async Task InstallOrRepairAsync()
    {
        if (isInstallingOpenSteamTool)
        {
            return;
        }

        isInstallingOpenSteamTool = true;
        UseWaitCursor = true;
        string? temporaryArchivePath = null;
        var report = new CopyReport();

        try
        {
            var release = await ResolveLatestOpenSteamToolReleaseAsync();
            temporaryArchivePath = Path.Combine(
                Path.GetTempPath(),
                $"TOST-{release.AssetName}-{Guid.NewGuid():N}.zip");

            logger.Info($"Downloading OpenSteamTool {release.Tag} from {release.DownloadUri}");
            await DownloadFileAsync(release.DownloadUri, temporaryArchivePath, MaxUpstreamDownloadBytes);

            EnsureSteamFolders(report);
            InstallFromZip(temporaryArchivePath, report);
            logger.Info($"Finished OpenSteamTool {release.Tag} install/repair.");
        }
        catch (Exception ex)
        {
            report.AddFailure("OpenSteamTool download", ex.Message);
            logger.Error($"Automatic OpenSteamTool install/repair failed: {ex}");
        }
        finally
        {
            UseWaitCursor = false;
            isInstallingOpenSteamTool = false;

            if (temporaryArchivePath is not null)
            {
                try
                {
                    File.Delete(temporaryArchivePath);
                }
                catch (Exception ex)
                {
                    logger.Error($"Could not remove temporary download {temporaryArchivePath}: {ex}");
                }
            }
        }

        ShowReport(report);
    }

    private static async Task<OpenSteamToolRelease> ResolveLatestOpenSteamToolReleaseAsync()
    {
        using var latestResponse = await UpstreamHttpClient.GetAsync(
            UpstreamLatestReleaseUrl,
            HttpCompletionOption.ResponseHeadersRead);
        latestResponse.EnsureSuccessStatusCode();

        var releaseUri = latestResponse.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("GitHub did not return the latest OpenSteamTool release URL.");
        var pathSegments = releaseUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tagMarkerIndex = Array.FindIndex(
            pathSegments,
            segment => segment.Equals("tag", StringComparison.OrdinalIgnoreCase));
        if (tagMarkerIndex < 0 || tagMarkerIndex + 1 >= pathSegments.Length)
        {
            throw new InvalidDataException("Could not determine the latest OpenSteamTool release tag.");
        }

        var tag = Uri.UnescapeDataString(pathSegments[tagMarkerIndex + 1]);
        var assetsUri = new Uri(
            $"https://github.com/OpenSteam001/OpenSteamTool/releases/expanded_assets/{Uri.EscapeDataString(tag)}");
        var assetsHtml = await UpstreamHttpClient.GetStringAsync(assetsUri);
        var assetPaths = Regex.Matches(
                assetsHtml,
                "href=\"(?<path>/OpenSteam001/OpenSteamTool/releases/download/[^\"]+\\.zip)\"",
                RegexOptions.IgnoreCase)
            .Select(match => WebUtility.HtmlDecode(match.Groups["path"].Value))
            .Where(path =>
                Path.GetFileName(path).StartsWith("OpenSteamTool-", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).EndsWith("-Release.zip", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (assetPaths.Count != 1)
        {
            throw new InvalidDataException(
                assetPaths.Count == 0
                    ? "The latest OpenSteamTool release does not contain a release ZIP."
                    : "The latest OpenSteamTool release contains multiple matching release ZIPs.");
        }

        var downloadUri = new Uri(new Uri("https://github.com"), assetPaths[0]);
        return new OpenSteamToolRelease(tag, Path.GetFileName(downloadUri.LocalPath), downloadUri);
    }

    private static async Task DownloadFileAsync(Uri sourceUri, string destinationPath, long maximumBytes)
    {
        using var response = await UpstreamHttpClient.GetAsync(
            sourceUri,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[81920];
        long bytesCopied = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer)) > 0)
        {
            if (bytesCopied > maximumBytes - bytesRead)
            {
                throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
            }

            bytesCopied += bytesRead;
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
        }
    }

    private static HttpClient CreateUpstreamHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/1.2 (+https://github.com/sadabx/TOST)");
        return client;
    }

    private void InstallFromZip(string archivePath, CopyReport report)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaxArchiveEntries)
            {
                report.AddFailure(
                    Path.GetFileName(archivePath),
                    $"Archive contains too many entries (maximum {MaxArchiveEntries:N0}).");
                return;
            }

            var recognizedEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && ResolveDestination(entry.Name) is not null)
                .ToList();

            if (recognizedEntries.Count == 0)
            {
                report.AddFailure(Path.GetFileName(archivePath), "Archive contains no supported files.");
                return;
            }

            var duplicateNames = recognizedEntries
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicateNames.Count > 0)
            {
                report.AddFailure(
                    Path.GetFileName(archivePath),
                    $"Archive contains duplicate supported files: {string.Join(", ", duplicateNames)}");
                return;
            }

            if (recognizedEntries.Any(entry => entry.Length > MaxArchiveEntryBytes) ||
                recognizedEntries.Sum(entry => entry.Length) > MaxArchivePayloadBytes)
            {
                report.AddFailure(Path.GetFileName(archivePath), "Archive payload is larger than the supported limit.");
                return;
            }

            long actualPayloadBytes = 0;
            foreach (var entry in recognizedEntries)
            {
                var destinationDirectory = ResolveDestination(entry.Name)!;
                CopyArchiveEntry(entry, destinationDirectory, report, ref actualPayloadBytes);
            }
        }
        catch (InvalidDataException ex)
        {
            report.AddFailure(Path.GetFileName(archivePath), $"Invalid ZIP archive: {ex.Message}");
            logger.Error($"Could not read ZIP archive {archivePath}: {ex}");
        }
        catch (Exception ex)
        {
            report.AddFailure(Path.GetFileName(archivePath), ex.Message);
            logger.Error($"Could not install ZIP archive {archivePath}: {ex}");
        }
    }

    private void CopyArchiveEntry(
        ZipArchiveEntry entry,
        string destinationDirectory,
        CopyReport report,
        ref long actualPayloadBytes)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, entry.Name);

            // Decompress into a same-directory temporary file first. Besides enforcing
            // the limit against the actual stream (not only the ZIP header), this keeps
            // a failed ZIP import from leaving a partially written Steam file behind.
            temporaryPath = Path.Combine(
                destinationDirectory,
                $".{entry.Name}.{Guid.NewGuid():N}.tost-tmp");
            long bytesCopied = 0;
            using (var source = entry.Open())
            using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (bytesCopied > MaxArchiveEntryBytes - bytesRead ||
                        actualPayloadBytes > MaxArchivePayloadBytes - bytesRead)
                    {
                        throw new InvalidDataException("Archive payload exceeds the supported size limit.");
                    }

                    bytesCopied += bytesRead;
                    actualPayloadBytes += bytesRead;
                    destination.Write(buffer, 0, bytesRead);
                }
            }

            if (settings.BackupBeforeOverwrite && File.Exists(destinationPath))
            {
                var backupPath = destinationPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destinationPath, backupPath, overwrite: false);
                logger.Info($"Backed up {destinationPath} -> {backupPath}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: settings.OverwriteExisting);
            temporaryPath = null;

            report.AddSuccess(entry.Name, destinationDirectory);
            logger.Info($"Copied ZIP entry {entry.FullName} -> {destinationPath}");
        }
        catch (Exception ex)
        {
            report.AddFailure(entry.Name, ex.Message);
            logger.Error($"Failed to copy ZIP entry {entry.FullName}: {ex}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup; the import failure is already reported.
                }
            }
        }
    }

    private static void OpenOfficialReleases()
    {
        OpenWebsite(UpstreamReleasesUrl);
    }

    private static void OpenManifestHub()
    {
        OpenWebsite(ManifestHubUrl);
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        try
        {
            var source = new SimpleWebSource(TostUpdateUrl);
            var manager = new UpdateManager(source);
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.Save();

            if (!manager.IsInstalled)
            {
                if (!silentWhenCurrent)
                {
                    TostDialog.Show(
                        this,
                        "Automatic updates are available in the installed TOST build.\n\nDownload TOST Setup from the Releases page to switch from a raw or portable build.",
                        "TOST Updates",
                        TostDialogButtons.Ok,
                        TostDialogIcon.Information);
                }

                return;
            }

            Cursor = Cursors.WaitCursor;
            trayIcon.Text = "TOST - checking for updates";
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                if (!silentWhenCurrent)
                {
                    TostDialog.Show(
                        this,
                        "TOST is up to date.",
                        "TOST Updates",
                        TostDialogButtons.Ok,
                        TostDialogIcon.Information);
                }

                return;
            }

            var result = TostDialog.Show(
                this,
                $"TOST {update.TargetFullRelease.Version} is available.\n\nDownload it now and restart TOST?",
                "TOST Update Available",
                TostDialogButtons.YesNo,
                TostDialogIcon.Information);
            if (result != DialogResult.Yes)
            {
                return;
            }

            trayIcon.Text = "TOST - downloading update";
            await manager.DownloadUpdatesAsync(
                update,
                progress => BeginInvoke(() => trayIcon.Text = $"TOST - downloading {progress}%"),
                CancellationToken.None);
            logger.Info($"Downloaded TOST update {update.TargetFullRelease.Version}.");
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease, null);
        }
        catch (Exception ex)
        {
            logger.Error($"TOST update check failed: {ex}");
            if (!silentWhenCurrent)
            {
                TostDialog.Show(
                    this,
                    $"Could not check for TOST updates.\n\n{ex.Message}",
                    "TOST Updates",
                    TostDialogButtons.Ok,
                    TostDialogIcon.Warning);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
            trayIcon.Text = "TOST";
        }
    }

    private static void OpenWebsite(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void EnsureSteamFolders(CopyReport report)
    {
        foreach (var directory in new[] { settings.SteamRoot, settings.SteamConfigPath, settings.LuaPath, settings.SteamAppsPath })
        {
            try
            {
                Directory.CreateDirectory(directory);
                logger.Info($"Ensured folder: {directory}");
            }
            catch (Exception ex)
            {
                report.AddFailure(directory, $"Could not create folder: {ex.Message}");
                logger.Error($"Could not create folder {directory}: {ex}");
            }
        }
    }

    private void CopyExpectedPath(string sourcePath, CopyReport report)
    {
        if (Directory.Exists(sourcePath))
        {
            CopyExpectedDirectory(sourcePath, report);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            report.AddFailure(sourcePath, "Path does not exist.");
            logger.Error($"Skipped missing path: {sourcePath}");
            return;
        }

        if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            InstallFromZip(sourcePath, report);
            return;
        }

        var fileName = Path.GetFileName(sourcePath);
        var destinationDirectory = ResolveDestination(fileName);
        if (destinationDirectory is null)
        {
            report.AddFailure(fileName, "Unexpected file type or name.");
            logger.Info($"Skipped unexpected file: {sourcePath}");
            return;
        }

        CopyFile(sourcePath, destinationDirectory, report);
    }

    private void CopyExpectedDirectory(string sourceDirectory, CopyReport report)
    {
        var copiedAny = false;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var before = report.Successes;
            CopyExpectedPath(file, report);
            copiedAny = copiedAny || report.Successes > before;
        }

        if (!copiedAny)
        {
            report.AddFailure(sourceDirectory, "Folder did not contain expected OpenSteamTool files.");
        }
    }

    private void CopyFile(string sourcePath, string destinationDirectory, CopyReport report)
    {
        var fileName = Path.GetFileName(sourcePath);

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, fileName);
            if (settings.BackupBeforeOverwrite && File.Exists(destinationPath))
            {
                var backupPath = destinationPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destinationPath, backupPath, overwrite: false);
                logger.Info($"Backed up {destinationPath} -> {backupPath}");
            }

            File.Copy(sourcePath, destinationPath, overwrite: settings.OverwriteExisting);
            report.AddSuccess(fileName, destinationDirectory);
            logger.Info($"Copied {sourcePath} -> {destinationPath}");
        }
        catch (Exception ex)
        {
            report.AddFailure(fileName, ex.Message);
            logger.Error($"Failed to copy {sourcePath}: {ex}");
        }
    }

    private string? ResolveDestination(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (fileName.Equals("opensteamtool.toml", StringComparison.OrdinalIgnoreCase))
        {
            return settings.SteamRoot;
        }

        if (fileName.Equals("OpenSteamTool.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return settings.SteamRoot;
        }

        if (extension == ".lua")
        {
            return settings.LuaPath;
        }

        if ((extension == ".acf" && fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)) ||
            extension == ".manifest")
        {
            return settings.SteamAppsPath;
        }

        return null;
    }

}

