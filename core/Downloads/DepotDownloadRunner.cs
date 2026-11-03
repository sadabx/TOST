using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Downloads;

public sealed record DepotDownloadProgress(
    string AppId,
    string? CurrentDepotId,
    int CompletedDepots,
    int TotalDepots,
    double CurrentDepotPercent,
    double OverallPercent,
    string? CurrentFile,
    string StatusMessage);

public sealed record DepotDownloadResult(
    bool Success,
    string Message,
    long TotalBytesOnDisk);

public sealed class DepotDownloadRunner
{
    private static readonly Regex ProgressPercentPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)%\s+(?<file>.*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<DepotDownloadResult> DownloadAsync(
        GameDownloadPlan plan,
        SteamInstallation installation,
        string? explicitExecutablePath = null,
        Action<DepotDownloadProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var executable = DepotDownloaderLocator.FindExecutable(explicitExecutablePath)
            ?? throw new FileNotFoundException(
                "DepotDownloaderMod executable was not found. Please install it in ~/.local/share/TOST/tools/DepotDownloaderMod");

        Directory.CreateDirectory(plan.TargetDirectory);

        // Write temporary depot keys file if any keys are available
        string? keysFilePath = null;
        var keysWithValues = plan.Depots.Where(d => !string.IsNullOrWhiteSpace(d.DepotKey)).ToList();
        if (keysWithValues.Count > 0)
        {
            keysFilePath = Path.Combine(Path.GetTempPath(), $"tost-keys-{Guid.NewGuid():N}.txt");
            var keyLines = keysWithValues.Select(d => $"{d.DepotId};{d.DepotKey}");
            await File.WriteAllLinesAsync(keysFilePath, keyLines, cancellationToken);
        }

        try
        {
            var totalDepots = plan.Depots.Count;
            if (totalDepots == 0)
            {
                return new DepotDownloadResult(false, "No depots declared for this application.", 0);
            }

            var completedDepots = 0;

            foreach (var depot in plan.Depots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                onProgress?.Invoke(new DepotDownloadProgress(
                    plan.AppId,
                    depot.DepotId,
                    completedDepots,
                    totalDepots,
                    0,
                    (completedDepots * 100.0) / totalDepots,
                    null,
                    $"Starting download for Depot {depot.DepotId}..."));

                var args = new List<string>
                {
                    "-app", plan.AppId,
                    "-depot", depot.DepotId,
                    "-manifest", depot.ManifestId,
                    "-dir", plan.TargetDirectory,
                    "-max-downloads", "16"
                };

                if (string.IsNullOrWhiteSpace(depot.ManifestPath) || !File.Exists(depot.ManifestPath))
                {
                    return new DepotDownloadResult(
                        false,
                        $"Manifest file ({depot.DepotId}_{depot.ManifestId}.manifest) is missing from depotcache. Valve CDN blocks anonymous manifest requests. Please import the .manifest file or .zip into TOST first.",
                        0);
                }

                args.Add("-manifestfile");
                args.Add(depot.ManifestPath);

                if (keysFilePath != null)
                {
                    args.Add("-depotkeys");
                    args.Add(keysFilePath);
                }

                var (success, errorMsg) = await RunProcessAsync(
                    executable,
                    args,
                    plan.AppId,
                    depot.DepotId,
                    completedDepots,
                    totalDepots,
                    onProgress,
                    cancellationToken);

                if (!success)
                {
                    return new DepotDownloadResult(false, $"Failed to download Depot {depot.DepotId}: {errorMsg}", 0);
                }

                completedDepots++;
                onProgress?.Invoke(new DepotDownloadProgress(
                    plan.AppId,
                    depot.DepotId,
                    completedDepots,
                    totalDepots,
                    100.0,
                    (completedDepots * 100.0) / totalDepots,
                    null,
                    $"Depot {depot.DepotId} completed ({completedDepots}/{totalDepots})."));
            }

            // Calculate total size on disk
            var totalBytes = CalculateDirectorySize(plan.TargetDirectory);

            // Write / update appmanifest_<appId>.acf
            CreateOrUpdateAppManifest(installation, plan, totalBytes);

            return new DepotDownloadResult(true, "Download completed successfully.", totalBytes);
        }
        finally
        {
            if (keysFilePath != null && File.Exists(keysFilePath))
            {
                try { File.Delete(keysFilePath); } catch { }
            }
        }
    }

    private static async Task<(bool Success, string? Error)> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string appId,
        string depotId,
        int completedDepots,
        int totalDepots,
        Action<DepotDownloadProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var errorBuilder = new StringBuilder();
        var lastSignificantMessage = string.Empty;

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lock (errorBuilder) errorBuilder.AppendLine(e.Data);
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            var trimmed = e.Data.Trim();
            if (trimmed.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("No valid depot key", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            {
                lastSignificantMessage = trimmed;
            }

            var match = ProgressPercentPattern.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups["percent"].Value, out var depotPercent))
            {
                var currentFile = match.Groups["file"].Value.Trim();
                var overallPercent = ((completedDepots * 100.0) + depotPercent) / totalDepots;
                onProgress?.Invoke(new DepotDownloadProgress(
                    appId,
                    depotId,
                    completedDepots,
                    totalDepots,
                    depotPercent,
                    overallPercent,
                    currentFile,
                    $"Downloading: {depotPercent:F1}% ({currentFile})"));
            }
            else
            {
                onProgress?.Invoke(new DepotDownloadProgress(
                    appId,
                    depotId,
                    completedDepots,
                    totalDepots,
                    0,
                    (completedDepots * 100.0) / totalDepots,
                    null,
                    trimmed));
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        await process.WaitForExitAsync(cancellationToken);

        string? error = null;
        if (process.ExitCode != 0)
        {
            var errStr = errorBuilder.ToString().Trim();
            error = !string.IsNullOrWhiteSpace(lastSignificantMessage)
                ? lastSignificantMessage
                : (!string.IsNullOrWhiteSpace(errStr) ? errStr : $"Exit code {process.ExitCode}");
        }

        return (process.ExitCode == 0, error);
    }

    private static void CreateOrUpdateAppManifest(
        SteamInstallation installation,
        GameDownloadPlan plan,
        long totalBytes)
    {
        var acfPath = Path.Combine(installation.SteamAppsPath, $"appmanifest_{plan.AppId}.acf");
        var tempPath = Path.Combine(installation.SteamAppsPath, $".appmanifest_{plan.AppId}.tost-{Guid.NewGuid():N}.tmp");

        var builder = new StringBuilder();
        builder.AppendLine("\"AppState\"");
        builder.AppendLine("{");
        builder.AppendLine($"\t\"appid\"\t\t\"{plan.AppId}\"");
        builder.AppendLine("\t\"Universe\"\t\t\"1\"");
        builder.AppendLine($"\t\"name\"\t\t\"{plan.GameName}\"");
        builder.AppendLine("\t\"StateFlags\"\t\t\"4\"");
        builder.AppendLine($"\t\"installdir\"\t\t\"{plan.InstallDirName}\"");
        builder.AppendLine($"\t\"LastUpdated\"\t\t\"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\"");
        builder.AppendLine($"\t\"SizeOnDisk\"\t\t\"{totalBytes}\"");
        builder.AppendLine("\t\"buildid\"\t\t\"0\"");
        builder.AppendLine("\t\"LastOwner\"\t\t\"0\"");
        builder.AppendLine("\t\"InstalledDepots\"");
        builder.AppendLine("\t{");

        foreach (var depot in plan.Depots)
        {
            builder.AppendLine($"\t\t\"{depot.DepotId}\"");
            builder.AppendLine("\t\t{");
            builder.AppendLine($"\t\t\t\"manifest\"\t\t\"{depot.ManifestId}\"");
            builder.AppendLine($"\t\t\t\"size\"\t\t\"{depot.Size ?? 0}\"");
            builder.AppendLine("\t\t}");
        }

        builder.AppendLine("\t}");
        builder.AppendLine("}");

        Directory.CreateDirectory(installation.SteamAppsPath);
        File.WriteAllText(tempPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(tempPath, acfPath, overwrite: true);
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return 0;

        long size = 0;
        try
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (file.FullName.Contains(".DepotDownloader")) continue;
                size += file.Length;
            }
        }
        catch
        {
            // Ignore access errors during size check
        }

        return size;
    }
}
