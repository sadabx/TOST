using System.Security.Cryptography;
using System.Text;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamBinaryInfo(string Path, long SizeBytes, DateTime LastWriteUtc, string Sha256);

public sealed record SlsSteamLogTail(string Path, IReadOnlyList<string> Lines, bool Truncated);

public sealed class SlsSteamDiagnosticsService
{
    public const int MaximumTailLines = 500;
    private const int MaximumTailBytes = 512 * 1024;

    public SlsSteamBinaryInfo? InspectBinary(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        info.Refresh();
        return new SlsSteamBinaryInfo(info.FullName, info.Length, info.LastWriteTimeUtc, hash);
    }

    public SlsSteamLogTail? ReadLatestLog(IReadOnlyList<string> candidates, int lineCount)
    {
        if (lineCount is < 1 or > MaximumTailLines)
        {
            throw new ArgumentOutOfRangeException(nameof(lineCount), $"Line count must be between 1 and {MaximumTailLines}.");
        }

        var path = candidates
            .Where(IsRegularFile)
            .Select(Path.GetFullPath)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytesToRead = (int)Math.Min(stream.Length, MaximumTailBytes);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        var buffer = new byte[bytesToRead];
        stream.ReadExactly(buffer);

        var text = Encoding.UTF8.GetString(buffer);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var truncatedByBytes = stream.Length > bytesToRead;
        if (truncatedByBytes && lines.Length > 0)
        {
            lines = lines[1..];
        }

        var nonTrailingCount = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var skip = Math.Max(0, nonTrailingCount - lineCount);
        var selected = lines.Skip(skip).Take(nonTrailingCount - skip).ToArray();
        return new SlsSteamLogTail(path, selected, truncatedByBytes || skip > 0);
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.LinkTarget is null;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
