using System.Diagnostics;

namespace Trionine.TOST.Desktop.Services;

internal static class SteamProcessGuard
{
    public static bool IsSteamRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("steam");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            DesktopLog.Error($"Could not check whether Steam is running: {ex.Message}");
            return false;
        }

        try
        {
            return processes.Any(process => !process.HasExited);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static string CloseSteamInstructions =>
        "Steam is currently running.\n\n" +
        "1. In Steam, open Steam > Exit.\n" +
        "2. Wait until the Steam tray icon disappears.\n" +
        "3. Try your action again.\n\n" +
        "TOST requires Steam to be closed so that Steam does not overwrite configuration changes on exit.";
}
