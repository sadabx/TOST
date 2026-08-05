using System.Text.Json;
using Microsoft.Win32;

namespace Trionine.TOST;

internal sealed class InstallerSettings
{
    public string SteamRoot { get; set; } = DetectSteamRoot();
    public bool OverwriteExisting { get; set; } = true;
    public bool BackupBeforeOverwrite { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    public string SteamConfigPath => Path.Combine(SteamRoot, "config");
    public string LuaPath => Path.Combine(SteamConfigPath, "lua");
    public string SteamAppsPath => Path.Combine(SteamRoot, "steamapps");
    public string SteamCommonPath => Path.Combine(SteamAppsPath, "common");
    public string SteamUserDataPath => Path.Combine(SteamRoot, "userdata");
    public string LogDirectory => AppPaths.LogDirectory;
    public string LogPath => AppPaths.LogPath;

    public bool ShouldCheckForUpdates()
    {
        return AutoCheckForUpdates &&
            (!LastUpdateCheckUtc.HasValue || DateTime.UtcNow - LastUpdateCheckUtc.Value >= TimeSpan.FromHours(24));
    }

    public static InstallerSettings Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            return new InstallerSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<InstallerSettings>(File.ReadAllText(path)) ?? new InstallerSettings();
        }
        catch
        {
            return new InstallerSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    private static string SettingsPath => AppPaths.SettingsPath;

    private static string DetectSteamRoot()
    {
        var registryPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            return registryPath.Replace('/', '\\');
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return Path.Combine(programFilesX86, "Steam");
    }
}


