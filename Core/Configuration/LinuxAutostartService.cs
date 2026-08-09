using System.Text;

namespace Trionine.TOST.Core.Configuration;

public enum AutostartState { Disabled, Enabled, Conflict }
public sealed record AutostartStatus(AutostartState State, string Path, string? Message);

public sealed class LinuxAutostartService
{
    private const string Marker = "# Managed by TOST";
    private static readonly UTF8Encoding Utf8 = new(false);

    public AutostartStatus Inspect(string autostartDirectory, string executablePath)
    {
        var path = TargetPath(autostartDirectory);
        if (!File.Exists(path)) return new(AutostartState.Disabled, path, null);
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Length is <= 0 or > 64 * 1024)
            return new(AutostartState.Conflict, path, "Existing autostart entry is not a bounded regular file.");
        var expected = BuildContent(executablePath);
        var actual = File.ReadAllText(path, Utf8);
        return actual == expected
            ? new(AutostartState.Enabled, path, null)
            : new(AutostartState.Conflict, path, "Existing autostart entry is unmanaged or was modified.");
    }

    public AutostartStatus Enable(string autostartDirectory, string executablePath)
    {
        var status = Inspect(autostartDirectory, executablePath);
        if (status.State == AutostartState.Conflict) throw new IOException(status.Message);
        if (status.State == AutostartState.Enabled) return status;
        Directory.CreateDirectory(Path.GetFullPath(autostartDirectory));
        AtomicWrite(status.Path, BuildContent(executablePath));
        return new(AutostartState.Enabled, status.Path, null);
    }

    public AutostartStatus Disable(string autostartDirectory, string executablePath)
    {
        var status = Inspect(autostartDirectory, executablePath);
        if (status.State == AutostartState.Conflict) throw new IOException(status.Message);
        if (status.State == AutostartState.Enabled) File.Delete(status.Path);
        return new(AutostartState.Disabled, status.Path, null);
    }

    private static string BuildContent(string executablePath)
    {
        var executable = Path.GetFullPath(executablePath);
        var info = new FileInfo(executable);
        if (!info.Exists || info.LinkTarget is not null || executable.IndexOfAny(['\r', '\n']) >= 0)
            throw new InvalidDataException("The TOST executable path is invalid.");
        var quoted = "\"" + executable.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
        return $"{Marker}\n[Desktop Entry]\nType=Application\nName=TOST\nComment=Start TOST in the desktop tray\nExec={quoted}\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
    }

    private static string TargetPath(string directory) => Path.Combine(Path.GetFullPath(directory), "tost.desktop");

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, Utf8);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
