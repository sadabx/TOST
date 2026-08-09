using Trionine.TOST.Core.Configuration;
using Microsoft.Win32;

namespace Trionine.TOST.Desktop.Services;

internal static class StartupRegistrationService
{
    public static bool CanRegister(out string reason)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            reason = "Startup registration is available on Windows and Linux.";
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Startup registration is disabled while running through dotnet. Use a packaged TOST executable.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    public static AutostartStatus Inspect()
    {
        if (!CanRegister(out var reason)) throw new InvalidOperationException(reason);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            var value = key?.GetValue("TOST") as string;
            return new AutostartStatus(
                string.IsNullOrWhiteSpace(value) ? AutostartState.Disabled : AutostartState.Enabled,
                DesktopPaths.LauncherPath,
                null);
        }

        return new LinuxAutostartService().Inspect(AutostartDirectory(), Environment.ProcessPath!);
    }

    public static AutostartStatus Apply(bool enabled)
    {
        if (!CanRegister(out var reason)) throw new InvalidOperationException(reason);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true) ?? throw new InvalidOperationException("The Windows startup registry key could not be opened.");
            if (enabled)
            {
                key.SetValue("TOST", $"\"{DesktopPaths.LauncherPath}\"");
            }
            else
            {
                key.DeleteValue("TOST", throwOnMissingValue: false);
            }

            return new AutostartStatus(
                enabled ? AutostartState.Enabled : AutostartState.Disabled,
                DesktopPaths.LauncherPath,
                null);
        }

        var service = new LinuxAutostartService();
        return enabled
            ? service.Enable(AutostartDirectory(), Environment.ProcessPath!)
            : service.Disable(AutostartDirectory(), Environment.ProcessPath!);
    }

    private static string AutostartDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
}
