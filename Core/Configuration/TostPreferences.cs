using System.Text.Json;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.Configuration;

public sealed record TostPreferences
{
    public SteamInstallationKind PreferredSteamInstallation { get; init; } = SteamInstallationKind.Native;
    public string WindowsSteamRoot { get; init; } = string.Empty;
    public bool OverwriteExistingFiles { get; init; } = true;
    public bool BackupFilesBeforeOverwrite { get; init; } = true;
    public bool AutomaticallyCheckForUpdates { get; init; } = true;
    public DateTime? LastUpdateCheckUtc { get; init; }
    public bool ShowFloatingIcon { get; init; } = true;
    public bool FloatingIconAlwaysOnTop { get; init; } = true;
    public bool StartWithDesktop { get; init; }
    public int DiagnosticTailLines { get; init; } = 100;
}

public sealed class TostPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string path;

    public TostPreferencesStore(string path) => this.path = Path.GetFullPath(path);

    public TostPreferences Load()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null) return new();
            var loaded = JsonSerializer.Deserialize<TostPreferences>(File.ReadAllText(path));
            return Normalize(loaded ?? new());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(TostPreferences preferences)
    {
        var normalized = Normalize(preferences);
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static TostPreferences Normalize(TostPreferences settings) => settings with
    {
        WindowsSteamRoot = settings.WindowsSteamRoot.Trim(),
        DiagnosticTailLines = Math.Clamp(settings.DiagnosticTailLines, 10, 2_000)
    };
}
