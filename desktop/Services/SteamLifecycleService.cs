using System.Diagnostics;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop.Services;

internal sealed class SteamLifecycleService
{
    public async Task RestartAsync(SteamRestartPlan plan, CancellationToken cancellationToken = default)
    {
        using var shutdown = Start(plan.Shutdown);
        await shutdown.WaitForExitAsync(cancellationToken);
        if (shutdown.ExitCode != 0)
            throw new InvalidOperationException($"Steam shutdown command exited with code {shutdown.ExitCode}.");

        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        _ = Start(plan.Launch);
    }

    private static Process Start(SteamCommand command)
    {
        var startInfo = new ProcessStartInfo(command.Executable) { UseShellExecute = false };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command.Executable}.");
    }
}
