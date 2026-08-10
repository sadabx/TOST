namespace Trionine.TOST;

internal sealed record ManagedGame(
    string AppId,
    string? Name,
    string LuaPath,
    IReadOnlyList<string> DepotIds,
    IReadOnlyList<string> ManifestPaths)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"App {AppId}" : Name;
}

internal sealed class RemovedGameArchive
{
    public string ArchiveId { get; set; } = string.Empty;
    public DateTime RemovedUtc { get; set; }
    public List<RemovedGameEntry> Games { get; set; } = [];
    public List<RemovedFileEntry> Files { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string ArchiveDirectory { get; set; } = string.Empty;
}

internal sealed class RemovedGameEntry
{
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LuaFileName { get; set; } = string.Empty;
}

internal sealed class RemovedFileEntry
{
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ArchiveRelativePath { get; set; } = string.Empty;
}

internal sealed record GameManagementResult(bool Success, string Message);

