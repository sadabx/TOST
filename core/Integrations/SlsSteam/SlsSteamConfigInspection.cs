namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed record SlsSteamConfigInspection(
    string Path,
    long SizeBytes,
    IReadOnlyList<string> TopLevelKeys,
    IReadOnlyList<string> Warnings)
{
    public bool IsStructurallyValid => Warnings.Count == 0;
}

public sealed record SlsSteamConfigBackup(string SourcePath, string BackupPath, long SizeBytes);

public sealed record SlsSteamConfigChangePreview(
    string Setting,
    bool? PreviousValue,
    bool NewValue,
    bool ChangesFile,
    string UpdatedText);

public sealed record SlsSteamConfigWriteResult(
    bool Changed,
    SlsSteamConfigBackup? Backup,
    string ConfigPath);

public sealed record SlsSteamConfigBackupEntry(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteUtc);
