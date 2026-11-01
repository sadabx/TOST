using System.Diagnostics;

namespace Trionine.TOST.Desktop.Services;

internal static class FolderLauncher
{
    public static void Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
            startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        else if (OperatingSystem.IsMacOS())
            startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
        else
            startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        startInfo.ArgumentList.Add(fullPath);
        Process.Start(startInfo);
    }
}
