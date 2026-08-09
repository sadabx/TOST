using System.Diagnostics;

namespace Trionine.TOST.Desktop.Services;

internal static class SteamProcessGuard
{
    public static bool IsSteamRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

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
        "Steam is currently running and is locking OpenSteamTool files.\n\n" +
        "1. In Steam, open Steam > Exit.\n" +
        "2. Wait until the Steam tray icon disappears.\n" +
        "3. Choose Install / Repair OpenSteamTool again.\n\n" +
        "TOST has not applied any files.";
}
