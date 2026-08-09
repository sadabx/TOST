using System.Text;
using System.Text.RegularExpressions;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamImportConfigPreview(bool ChangesFile, string UpdatedText, IReadOnlyList<string> ChangedSections);

public sealed class SlsSteamImportConfigService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex TopLevelPattern = new(
        @"(?m)^(?<key>[A-Za-z][A-Za-z0-9_-]*):[^\r\n]*(?:\r?\n|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public SlsSteamImportConfigPreview Preview(string configPath, SlsSteamImportConversionPlan plan)
    {
        var text = Read(configPath);
        var sections = new List<string>();
        text = MergeList(text, "AdditionalApps", plan.AdditionalApps, sections);
        text = MergeMap(text, "AppTokens", plan.AppTokens, sections);
        text = MergeMap(text, "ManifestIds", plan.ManifestIds.ToDictionary(x => x.DepotId, x => x.ManifestId), sections);
        return new SlsSteamImportConfigPreview(sections.Count > 0, text, sections);
    }

    public SlsSteamConfigWriteResult Apply(
        string configPath,
        SlsSteamImportConversionPlan plan,
        string backupDirectory)
    {
        var preview = Preview(configPath, plan);
        if (!preview.ChangesFile)
            return new SlsSteamConfigWriteResult(false, null, Path.GetFullPath(configPath));

        var backup = new SlsSteamConfigService().CreateBackup(configPath, backupDirectory);
        WriteAtomic(configPath, StrictUtf8.GetBytes(preview.UpdatedText));
        return new SlsSteamConfigWriteResult(true, backup, Path.GetFullPath(configPath));
    }

    private static string MergeList(string text, string key, IEnumerable<string> incoming, List<string> changed)
    {
        var values = ReadSection(text, key).Lines.Select(line =>
            Regex.Match(line, @"^\s+-\s+(?<value>\d+)\s*(?:#.*)?$")).Where(match => match.Success)
            .Select(match => match.Groups["value"].Value).Concat(incoming)
            .Distinct(StringComparer.Ordinal).OrderBy(ulong.Parse).ToArray();
        var replacement = key + ":" + NewLine(text) + string.Join(NewLine(text), values.Select(value => $"  - {value}"));
        if (values.Length > 0) replacement += NewLine(text);
        return ReplaceIfChanged(text, key, replacement, changed);
    }

    private static string MergeMap(string text, string key, IReadOnlyDictionary<string, string> incoming, List<string> changed)
    {
        var values = ReadSection(text, key).Lines.Select(line =>
            Regex.Match(line, "^\\s+(?<key>\\d+):\\s*[\\\"']?(?<value>\\d+)[\\\"']?\\s*(?:#.*)?$"))
            .Where(match => match.Success).ToDictionary(match => match.Groups["key"].Value,
                match => match.Groups["value"].Value, StringComparer.Ordinal);
        foreach (var pair in incoming) values[pair.Key] = pair.Value;
        var replacement = key + ":" + NewLine(text) + string.Join(NewLine(text), values.OrderBy(pair => ulong.Parse(pair.Key))
            .Select(pair => $"  {pair.Key}: {pair.Value}"));
        if (values.Count > 0) replacement += NewLine(text);
        return ReplaceIfChanged(text, key, replacement, changed);
    }

    private static (int Start, int Length, string[] Lines) ReadSection(string text, string key)
    {
        var matches = TopLevelPattern.Matches(text);
        var matching = matches.Where(match => match.Groups["key"].Value.Equals(key, StringComparison.Ordinal)).ToArray();
        if (matching.Length != 1) throw new InvalidDataException($"SLSsteam configuration must contain exactly one {key} section.");
        var match = matching[0];
        var next = matches.FirstOrDefault(item => item.Index > match.Index);
        var end = next?.Index ?? text.Length;
        var bodyStart = match.Index + match.Length;
        var lines = text[bodyStart..end].Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')).ToArray();
        return (match.Index, end - match.Index, lines);
    }

    private static string ReplaceIfChanged(string text, string key, string replacement, List<string> changed)
    {
        var section = ReadSection(text, key);
        var current = text.Substring(section.Start, section.Length);
        if (current == replacement) return text;
        changed.Add(key);
        return text.Remove(section.Start, section.Length).Insert(section.Start, replacement);
    }

    private static string Read(string path)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || info.LinkTarget is not null || info.Length is <= 0 or > SlsSteamConfigService.MaximumConfigBytes)
            throw new InvalidDataException("SLSsteam config must be a bounded regular file.");
        try { return StrictUtf8.GetString(File.ReadAllBytes(info.FullName)); }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("SLSsteam config is not valid UTF-8.", ex); }
    }

    private static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Invalid config path.");
        var temporary = Path.Combine(directory, $".config.yaml.tost-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
