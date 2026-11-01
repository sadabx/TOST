using System.Text;
using System.Text.RegularExpressions;

namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed class SlsSteamConfigService
{
    public const long MaximumConfigBytes = 2L * 1024 * 1024;

    private static readonly Regex TopLevelKeyPattern = new(
        @"^(?<key>[A-Za-z][A-Za-z0-9_-]*):(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly HashSet<string> BooleanSettings = new(StringComparer.OrdinalIgnoreCase)
    {
        "SafeMode",
        "Notifications",
        "NotifyInit",
        "API",
        "DisableCloud",
        "DisableUpdates",
        "ExtendedLogging",
        "WarnHashMissmatch",
        "DumpClientInterfaces",
        "UseWhitelist"
    };

    public static IReadOnlyCollection<string> SupportedBooleanSettings => BooleanSettings;

    public SlsSteamConfigInspection Inspect(string configPath)
    {
        var snapshot = ReadSnapshot(configPath);
        var keys = new List<string>();
        var warnings = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;

        using var reader = new StringReader(snapshot.Text);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('\t'))
            {
                warnings.Add($"Line {lineNumber} uses a tab for indentation.");
                continue;
            }

            if (char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var match = TopLevelKeyPattern.Match(line);
            if (!match.Success)
            {
                warnings.Add($"Line {lineNumber} is not a recognized top-level YAML key.");
                continue;
            }

            var key = match.Groups["key"].Value;
            if (!seenKeys.Add(key))
            {
                warnings.Add($"Top-level key '{key}' appears more than once.");
                continue;
            }

            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            warnings.Add("No top-level configuration keys were found.");
        }

        return new SlsSteamConfigInspection(configPath, snapshot.Bytes.LongLength, keys, warnings);
    }

    public SlsSteamConfigBackup CreateBackup(string configPath, string backupDirectory)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new ArgumentException("A backup directory is required.", nameof(backupDirectory));
        }

        var snapshot = ReadSnapshot(configPath);
        return CreateBackup(configPath, backupDirectory, snapshot);
    }

    public IReadOnlyList<SlsSteamConfigBackupEntry> FindBackups(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(backupDirectory, "config-*.yaml", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists && info.LinkTarget is null && info.Length <= MaximumConfigBytes)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new SlsSteamConfigBackupEntry(info.Name, info.FullName, info.Length, info.LastWriteTimeUtc))
            .ToArray();
    }

    public SlsSteamConfigInspection InspectBackup(string backupDirectory, string backupFileName)
        => Inspect(ResolveBackupPath(backupDirectory, backupFileName));

    public SlsSteamConfigWriteResult RestoreBackup(
        string configPath,
        string backupDirectory,
        string backupFileName)
    {
        var fullConfigPath = Path.GetFullPath(configPath);
        var restoreSnapshot = ReadSnapshot(ResolveBackupPath(backupDirectory, backupFileName));
        var currentSnapshot = ReadSnapshot(fullConfigPath);
        if (restoreSnapshot.Bytes.AsSpan().SequenceEqual(currentSnapshot.Bytes))
        {
            return new SlsSteamConfigWriteResult(false, null, fullConfigPath);
        }

        var safetyBackup = CreateBackup(fullConfigPath, backupDirectory, currentSnapshot);
        WriteAtomic(fullConfigPath, restoreSnapshot.Bytes);
        return new SlsSteamConfigWriteResult(true, safetyBackup, fullConfigPath);
    }

    public SlsSteamConfigChangePreview PreviewSafeMode(string configPath, bool enabled)
        => PreviewBooleanSetting(configPath, "SafeMode", enabled);

    public SlsSteamConfigChangePreview PreviewBooleanSetting(string configPath, string setting, bool enabled)
    {
        setting = ValidateBooleanSetting(setting);
        var snapshot = ReadSnapshot(configPath);
        return BuildBooleanPreview(snapshot.Text, setting, enabled);
    }

    public SlsSteamConfigWriteResult SetSafeMode(string configPath, bool enabled, string backupDirectory)
        => SetBooleanSetting(configPath, "SafeMode", enabled, backupDirectory);

    public SlsSteamConfigWriteResult SetBooleanSetting(
        string configPath,
        string setting,
        bool enabled,
        string backupDirectory)
    {
        setting = ValidateBooleanSetting(setting);
        var fullPath = Path.GetFullPath(configPath);
        var snapshot = ReadSnapshot(fullPath);
        var preview = BuildBooleanPreview(snapshot.Text, setting, enabled);
        if (!preview.ChangesFile)
        {
            return new SlsSteamConfigWriteResult(false, null, fullPath);
        }

        var backup = CreateBackup(fullPath, backupDirectory, snapshot);
        WriteAtomic(fullPath, StrictUtf8.GetBytes(preview.UpdatedText));

        return new SlsSteamConfigWriteResult(true, backup, fullPath);
    }

    private static SlsSteamConfigChangePreview BuildBooleanPreview(string text, string setting, bool enabled)
    {
        var settingPattern = new Regex(
            $@"^(?<prefix>{Regex.Escape(setting)}:[ \t]*)(?<value>true|false|yes|no)(?<suffix>[ \t]*(?:#.*)?)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var matches = settingPattern.Matches(text);
        if (matches.Count > 1)
        {
            throw new InvalidDataException($"{setting} appears more than once in the SLSsteam configuration.");
        }

        var newValue = enabled ? "yes" : "no";
        if (matches.Count == 1)
        {
            var match = matches[0];
            var previousValue = ParseYamlBoolean(match.Groups["value"].Value);
            var replacement = match.Groups["prefix"].Value + newValue + match.Groups["suffix"].Value;
            var updated = text.Remove(match.Index, match.Length).Insert(match.Index, replacement);
            return new SlsSteamConfigChangePreview(setting, previousValue, enabled, previousValue != enabled, updated);
        }

        if (Regex.IsMatch(
                text,
                $@"(?m)^{Regex.Escape(setting)}\s*:",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            throw new InvalidDataException($"{setting} has an unsupported value and was not changed.");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var separator = text.Length == 0 || text.EndsWith('\n') ? string.Empty : newline;
        var updatedText = $"{text}{separator}{setting}: {newValue}{newline}";
        return new SlsSteamConfigChangePreview(setting, null, enabled, true, updatedText);
    }

    private static string ValidateBooleanSetting(string setting)
    {
        if (string.IsNullOrWhiteSpace(setting))
        {
            throw new ArgumentException("A setting name is required.", nameof(setting));
        }

        var canonicalName = BooleanSettings.FirstOrDefault(candidate =>
            candidate.Equals(setting, StringComparison.OrdinalIgnoreCase));
        return canonicalName ?? throw new ArgumentException(
            $"Unsupported boolean setting '{setting}'.",
            nameof(setting));
    }

    private static bool ParseYamlBoolean(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static SlsSteamConfigBackup CreateBackup(
        string configPath,
        string backupDirectory,
        ConfigSnapshot snapshot)
    {
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            Path.GetFullPath(backupDirectory),
            $"config-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.yaml");

        using (var stream = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(snapshot.Bytes);
            stream.Flush(flushToDisk: true);
        }

        return new SlsSteamConfigBackup(configPath, backupPath, snapshot.Bytes.LongLength);
    }

    private static string ResolveBackupPath(string backupDirectory, string backupFileName)
    {
        if (string.IsNullOrWhiteSpace(backupFileName) ||
            !backupFileName.Equals(Path.GetFileName(backupFileName), StringComparison.Ordinal) ||
            !backupFileName.StartsWith("config-", StringComparison.Ordinal) ||
            !backupFileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The backup filename is invalid.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(backupDirectory, backupFileName));
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.LinkTarget is not null)
        {
            throw new FileNotFoundException("The requested regular backup file was not found.", backupFileName);
        }

        return fullPath;
    }

    private static void WriteAtomic(string configPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(configPath)
            ?? throw new InvalidOperationException("The configuration directory could not be determined.");
        var temporaryPath = Path.Combine(directory, $".config.yaml.tost-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            PreserveUnixMode(configPath, temporaryPath);
            File.Move(temporaryPath, configPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void PreserveUnixMode(string sourcePath, string destinationPath)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(destinationPath, File.GetUnixFileMode(sourcePath));
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // The original configuration remains intact; stale temp cleanup is best effort.
        }
    }

    private static ConfigSnapshot ReadSnapshot(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new ArgumentException("A configuration path is required.", nameof(configPath));
        }

        var fullPath = Path.GetFullPath(configPath);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumConfigBytes)
        {
            throw new InvalidDataException($"The SLSsteam configuration exceeds {MaximumConfigBytes} bytes.");
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("The SLSsteam configuration is not valid UTF-8.", ex);
        }

        if (text.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("The SLSsteam configuration contains null bytes.");
        }

        return new ConfigSnapshot(bytes, text);
    }

    private sealed record ConfigSnapshot(byte[] Bytes, string Text);
}
