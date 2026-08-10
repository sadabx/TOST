using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Core.GameManagement;

public enum LauncherWorkaroundKind
{
    Rockstar,
    EA,
    Ubisoft
}

public sealed record GameWorkaroundStatus(
    bool HasRockstarWorkaround,
    bool HasEaWorkaround,
    bool HasUbisoftWorkaround,
    string? GameDirectory,
    bool GameDirectoryExists);

public sealed class LauncherWorkaroundService
{
    public static string GetDllFileName(LauncherWorkaroundKind kind) => kind switch
    {
        LauncherWorkaroundKind.Rockstar => "socialclub64.dll",
        LauncherWorkaroundKind.EA => "Activation64.dll",
        LauncherWorkaroundKind.Ubisoft => "uplay_r1_loader64.dll",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public string? GetGameDirectory(ManagedGame game, SteamInstallation installation)
    {
        var commonPath = Path.Combine(installation.SteamAppsPath, "common");
        if (!string.IsNullOrWhiteSpace(game.InstallDirectory))
        {
            var path = Path.GetFullPath(Path.Combine(commonPath, game.InstallDirectory));
            if (path.StartsWith(commonPath, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        var fallbackName = !string.IsNullOrWhiteSpace(game.Name) ? game.Name : $"App_{game.AppId}";
        var fallbackPath = Path.GetFullPath(Path.Combine(commonPath, fallbackName));
        return fallbackPath.StartsWith(commonPath, StringComparison.OrdinalIgnoreCase) ? fallbackPath : null;
    }

    public GameWorkaroundStatus GetStatus(ManagedGame game, SteamInstallation installation)
    {
        var dir = GetGameDirectory(game, installation);
        if (dir is null || !Directory.Exists(dir))
        {
            return new GameWorkaroundStatus(false, false, false, dir, false);
        }

        var rockstar = File.Exists(Path.Combine(dir, "socialclub64.dll"));
        var ea = File.Exists(Path.Combine(dir, "Activation64.dll"));
        var ubisoft = File.Exists(Path.Combine(dir, "uplay_r1_loader64.dll")) ||
                      File.Exists(Path.Combine(dir, "uplay_r1_loader.dll"));

        return new GameWorkaroundStatus(rockstar, ea, ubisoft, dir, true);
    }

    public GameManagementResult ApplyWorkaround(
        ManagedGame game,
        LauncherWorkaroundKind kind,
        SteamInstallation installation,
        byte[]? customDllContent = null)
    {
        var dir = GetGameDirectory(game, installation);
        if (dir is null)
        {
            return new GameManagementResult(false, "Could not determine game installation directory.");
        }

        try
        {
            Directory.CreateDirectory(dir);
            var fileName = GetDllFileName(kind);
            var targetPath = Path.Combine(dir, fileName);

            var content = customDllContent ?? GetStubBytes(kind);
            File.WriteAllBytes(targetPath, content);

            return new GameManagementResult(true, $"Applied {kind} workaround ({fileName}) to {game.DisplayName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GameManagementResult(false, $"Could not apply {kind} workaround: {ex.Message}");
        }
    }

    public GameManagementResult RemoveWorkaround(
        ManagedGame game,
        LauncherWorkaroundKind kind,
        SteamInstallation installation)
    {
        var dir = GetGameDirectory(game, installation);
        if (dir is null || !Directory.Exists(dir))
        {
            return new GameManagementResult(false, "Game installation directory does not exist.");
        }

        try
        {
            var fileName = GetDllFileName(kind);
            var targetPath = Path.Combine(dir, fileName);
            var removedAny = false;

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                removedAny = true;
            }

            if (kind == LauncherWorkaroundKind.Ubisoft)
            {
                var alt32Path = Path.Combine(dir, "uplay_r1_loader.dll");
                if (File.Exists(alt32Path))
                {
                    File.Delete(alt32Path);
                    removedAny = true;
                }
            }

            return removedAny
                ? new GameManagementResult(true, $"Removed {kind} workaround from {game.DisplayName}.")
                : new GameManagementResult(false, $"No {kind} workaround DLL was found in {game.DisplayName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new GameManagementResult(false, $"Could not remove {kind} workaround: {ex.Message}");
        }
    }

    private static byte[] GetStubBytes(LauncherWorkaroundKind kind)
    {
        // Minimal marker byte sequence for TOST-applied launcher workaround stub
        return System.Text.Encoding.UTF8.GetBytes($"/* TOST Launcher Workaround Stub: {kind} */\n");
    }
}
