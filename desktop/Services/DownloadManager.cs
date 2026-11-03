using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Trionine.TOST.Core.Downloads;
using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop.Services;

internal sealed class DownloadItem
{
    public required ManagedGame Game { get; init; }
    public required SteamInstallation Installation { get; init; }
    public required string InstallDirName { get; init; }
    public Bitmap? Thumbnail { get; set; }

    public string AppId => Game.AppId;
    public string DisplayName => Game.DisplayName;

    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double DownloadPercent { get; set; }
    public double InstallPercent { get; set; }
    public double CurrentSpeedBytesPerSec { get; set; }
    public double PeakSpeedBytesPerSec { get; set; }
    public TimeSpan? EstimatedRemaining { get; set; }
    public string StatusMessage { get; set; } = "Queued";

    public bool IsDownloading { get; set; }
    public bool IsPaused { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedTime { get; set; }

    public CancellationTokenSource? Cts { get; set; }
}

internal sealed class DownloadManager
{
    private static readonly Lazy<DownloadManager> LazyInstance = new(() => new DownloadManager());
    public static DownloadManager Instance => LazyInstance.Value;

    private readonly object syncRoot = new();
    private readonly List<DownloadItem> queue = [];
    private readonly List<DownloadItem> completed = [];
    private DownloadItem? activeItem;
    private bool isWorkerRunning;

    public event Action? Changed;
    public event Action<DownloadItem>? DownloadFinished;

    public DownloadItem? ActiveItem
    {
        get { lock (syncRoot) return activeItem; }
    }

    public IReadOnlyList<DownloadItem> Queue
    {
        get { lock (syncRoot) return [.. queue]; }
    }

    public IReadOnlyList<DownloadItem> Completed
    {
        get { lock (syncRoot) return [.. completed]; }
    }

    public bool IsActiveOrQueued(string appId)
    {
        lock (syncRoot)
        {
            if (activeItem?.AppId == appId) return true;
            return queue.Any(item => item.AppId == appId);
        }
    }

    public void Enqueue(SteamInstallation installation, ManagedGame game, Bitmap? thumbnail = null)
    {
        lock (syncRoot)
        {
            if (activeItem?.AppId == game.AppId || queue.Any(i => i.AppId == game.AppId))
            {
                return;
            }

            // Remove from completed if re-downloading
            completed.RemoveAll(i => i.AppId == game.AppId);

            var dirName = GameDownloadPlanner.ResolveInstallDirName(installation, game);
            var item = new DownloadItem
            {
                Game = game,
                Installation = installation,
                InstallDirName = dirName,
                Thumbnail = thumbnail
            };

            queue.Add(item);
        }

        NotifyChanged();
        EnsureWorkerRunning();
    }

    public void CancelCurrent()
    {
        DownloadItem? toCancel;
        lock (syncRoot)
        {
            toCancel = activeItem;
        }

        if (toCancel != null)
        {
            toCancel.StatusMessage = "Canceling...";
            NotifyChanged();
            toCancel.Cts?.Cancel();
        }
    }

    public void CancelQueued(string appId)
    {
        lock (syncRoot)
        {
            queue.RemoveAll(i => i.AppId == appId);
        }

        NotifyChanged();
    }

    public void ClearCompleted()
    {
        lock (syncRoot)
        {
            completed.Clear();
        }

        NotifyChanged();
    }

    public void RemoveCompleted(string appId)
    {
        lock (syncRoot)
        {
            completed.RemoveAll(i => i.AppId == appId);
        }

        NotifyChanged();
    }

    private void EnsureWorkerRunning()
    {
        lock (syncRoot)
        {
            if (isWorkerRunning) return;
            isWorkerRunning = true;
        }

        Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            DownloadItem current;
            lock (syncRoot)
            {
                if (queue.Count == 0)
                {
                    activeItem = null;
                    isWorkerRunning = false;
                    NotifyChanged();
                    return;
                }

                current = queue[0];
                queue.RemoveAt(0);
                activeItem = current;
                current.IsDownloading = true;
                current.StatusMessage = "Preparing download...";
                current.Cts = new CancellationTokenSource();
            }

            NotifyChanged();

            await ExecuteDownloadAsync(current);

            lock (syncRoot)
            {
                activeItem = null;
                if (current.IsCompleted)
                {
                    completed.Insert(0, current);
                }
            }

            NotifyChanged();
            Dispatcher.UIThread.Post(() => DownloadFinished?.Invoke(current));
        }
    }

    private async Task ExecuteDownloadAsync(DownloadItem item)
    {
        var exe = DepotDownloaderLocator.FindExecutable();
        if (exe is null)
        {
            item.IsDownloading = false;
            item.IsFailed = true;
            item.ErrorMessage = $"DepotDownloaderMod not found in {DepotDownloaderLocator.GetDefaultToolDirectory()}.";
            item.StatusMessage = "DepotDownloaderMod not found.";
            return;
        }

        var plan = GameDownloadPlanner.CreatePlan(item.Installation, item.Game);
        if (plan.Depots.Count == 0)
        {
            item.IsDownloading = false;
            item.IsFailed = true;
            item.ErrorMessage = "No manifests found for this game.";
            item.StatusMessage = "No manifests found.";
            return;
        }

        // Load thumbnail if missing
        if (item.Thumbnail == null)
        {
            var thumb = await SteamThumbnailService.GetThumbnailAsync(item.AppId);
            if (thumb != null)
            {
                item.Thumbnail = thumb;
                NotifyChanged();
            }
        }

        var runner = new DepotDownloadRunner();
        var sw = Stopwatch.StartNew();
        var lastBytes = 0L;
        var lastTime = sw.Elapsed;
        var peakSpeed = 0.0;

        // Estimate total bytes from existing directory or set a reasonable baseline
        item.StatusMessage = "Connecting to Steam CDN...";
        NotifyChanged();

        try
        {
            var result = await runner.DownloadAsync(
                plan,
                item.Installation,
                explicitExecutablePath: exe,
                onProgress: progress =>
                {
                    var now = sw.Elapsed;
                    var elapsedSec = (now - lastTime).TotalSeconds;

                    // Progress overall percent
                    item.DownloadPercent = progress.OverallPercent;
                    item.InstallPercent = Math.Max(0, progress.OverallPercent - 2.0);
                    item.StatusMessage = progress.StatusMessage;

                    if (elapsedSec >= 0.5)
                    {
                        var targetDir = plan.TargetDirectory;
                        var currentDiskBytes = TryGetDirectorySize(targetDir);
                        if (currentDiskBytes > 0)
                        {
                            item.DownloadedBytes = currentDiskBytes;
                            if (item.TotalBytes < currentDiskBytes)
                            {
                                item.TotalBytes = progress.OverallPercent > 0
                                    ? (long)(currentDiskBytes / (progress.OverallPercent / 100.0))
                                    : currentDiskBytes;
                            }

                            var deltaBytes = currentDiskBytes - lastBytes;
                            if (deltaBytes > 0 && elapsedSec > 0)
                            {
                                var speed = deltaBytes / elapsedSec;
                                item.CurrentSpeedBytesPerSec = speed;
                                peakSpeed = Math.Max(peakSpeed, speed);
                                item.PeakSpeedBytesPerSec = peakSpeed;

                                var remainingBytes = Math.Max(0, item.TotalBytes - currentDiskBytes);
                                if (speed > 1024)
                                {
                                    item.EstimatedRemaining = TimeSpan.FromSeconds(remainingBytes / speed);
                                }
                            }

                            lastBytes = currentDiskBytes;
                        }

                        lastTime = now;
                        NotifyChanged();
                    }
                },
                cancellationToken: item.Cts!.Token);

            item.IsDownloading = false;
            if (result.Success)
            {
                item.IsCompleted = true;
                item.CompletedTime = DateTime.Now;
                item.DownloadPercent = 100.0;
                item.InstallPercent = 100.0;
                item.TotalBytes = result.TotalBytesOnDisk;
                item.DownloadedBytes = result.TotalBytesOnDisk;
                item.StatusMessage = "Download completed successfully.";
            }
            else
            {
                item.IsFailed = true;
                item.ErrorMessage = result.Message;
                item.StatusMessage = $"Failed: {result.Message}";
            }
        }
        catch (OperationCanceledException)
        {
            item.IsDownloading = false;
            item.StatusMessage = "Canceled";
        }
        catch (Exception ex)
        {
            item.IsDownloading = false;
            item.IsFailed = true;
            item.ErrorMessage = ex.Message;
            item.StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            item.CurrentSpeedBytesPerSec = 0;
            NotifyChanged();
        }
    }

    private static long TryGetDirectorySize(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return 0;
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; } catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    private void NotifyChanged()
    {
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }
}
