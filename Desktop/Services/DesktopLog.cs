namespace Trionine.TOST.Desktop.Services;

internal static class DesktopLog
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DesktopPaths.LogDirectory);
                File.AppendAllText(
                    DesktopPaths.LogPath,
                    $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never prevent the desktop app from operating.
        }
    }
}
