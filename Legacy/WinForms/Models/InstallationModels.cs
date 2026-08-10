namespace Trionine.TOST;

internal sealed record OpenSteamToolRelease(string Tag, string AssetName, Uri DownloadUri);

internal sealed class CopyReport
{
    private readonly List<string> lines = [];
    private readonly Dictionary<CopyCategory, int> categories = [];
    public int Successes { get; private set; }
    public int Failures { get; private set; }

    public void AddSuccess(string fileName, string destinationDirectory)
    {
        Successes++;
        var category = Categorize(fileName);
        categories[category] = categories.GetValueOrDefault(category) + 1;
        lines.Add($"Copied {fileName} -> {destinationDirectory}");
    }

    public void AddFailure(string fileName, string reason)
    {
        Failures++;
        lines.Add($"Skipped {fileName}: {reason}");
    }

    public string ToMessage()
    {
        return lines.Count == 0 ? "No files copied." : string.Join(Environment.NewLine, lines);
    }

    public string ToLogMessage()
    {
        return $"Copy report: {Successes} copied, {Failures} skipped. {string.Join(" | ", lines)}";
    }

    public string ToToastMessage()
    {
        if (Successes == 0)
        {
            return Failures == 1
                ? "No supported file was imported\nCheck Logs for details"
                : $"No supported files were imported\nSkipped {Failures} files\nCheck Logs for details";
        }

        var summary = new List<string>();
        AddCategoryLine(summary, CopyCategory.Lua, "Lua script", "Lua scripts");
        AddCategoryLine(summary, CopyCategory.Manifest, "manifest file", "manifest files");
        AddCategoryLine(summary, CopyCategory.OpenSteamTool, "OpenSteamTool file", "OpenSteamTool files");

        if (Failures > 0)
        {
            summary.Add($"Skipped {Failures} unsupported {(Failures == 1 ? "file" : "files")}");
        }

        summary.Add("Will take effect after Steam restarts");
        return string.Join(Environment.NewLine, summary);
    }

    private void AddCategoryLine(List<string> summary, CopyCategory category, string singular, string plural)
    {
        var count = categories.GetValueOrDefault(category);
        if (count > 0)
        {
            summary.Add($"Imported {count} {(count == 1 ? singular : plural)}");
        }
    }

    private static CopyCategory Categorize(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return CopyCategory.Lua;
        }

        if (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
            (extension.Equals(".acf", StringComparison.OrdinalIgnoreCase) &&
             fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)))
        {
            return CopyCategory.Manifest;
        }

        return CopyCategory.OpenSteamTool;
    }

    private enum CopyCategory
    {
        Lua,
        Manifest,
        OpenSteamTool
    }
}

