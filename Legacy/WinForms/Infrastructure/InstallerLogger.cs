namespace Trionine.TOST;

internal sealed class InstallerLogger
{
    private readonly string path;

    public InstallerLogger(string path)
    {
        this.path = path;
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never block installation.
        }
    }
}


