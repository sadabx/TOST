using Trionine.TOST.Core.Configuration;

namespace Trionine.TOST.Desktop.Services;

internal static class StartupRegistrationService
{
    public static bool CanRegister(out string reason)
    {
        if (!OperatingSystem.IsLinux()) { reason = "Startup registration is currently available for packaged Linux builds."; return false; }
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
        return new LinuxAutostartService().Inspect(AutostartDirectory(), Environment.ProcessPath!);
    }

    public static AutostartStatus Apply(bool enabled)
    {
        if (!CanRegister(out var reason)) throw new InvalidOperationException(reason);
        var service = new LinuxAutostartService();
        return enabled
            ? service.Enable(AutostartDirectory(), Environment.ProcessPath!)
            : service.Disable(AutostartDirectory(), Environment.ProcessPath!);
    }

    private static string AutostartDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
}
