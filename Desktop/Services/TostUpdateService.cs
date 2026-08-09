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
        if (manager is null)
        {
            return new TostUpdateAvailability(false, null, null);
        }

        if (!manager.IsInstalled)
        {
            return new TostUpdateAvailability(false, null, null);
        }

        var update = await manager.CheckForUpdatesAsync();
        return new TostUpdateAvailability(
            true,
            update?.TargetFullRelease.Version.ToString(),
            update);
    }

    public async Task DownloadAndApplyAsync(TostUpdateAvailability availability)
    {
        if (manager is null || availability.State is not UpdateInfo update)
        {
            throw new InvalidOperationException("No TOST update is ready to download.");
        }

        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease, null);
    }
}
