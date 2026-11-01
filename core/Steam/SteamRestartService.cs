namespace Trionine.TOST.Core.Steam;

public sealed record SteamCommand(string Executable, IReadOnlyList<string> Arguments);
public sealed record SteamRestartPlan(SteamInstallationKind Kind, SteamCommand Shutdown, SteamCommand Launch);

public sealed class SteamRestartService
{
    public SteamRestartPlan CreatePlan(
        SteamInstallationKind kind,
        string? pathEnvironment = null,
        Func<string, bool>? fileExists = null,
        string? steamRoot = null)
    {
        pathEnvironment ??= Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        fileExists ??= File.Exists;
        if (kind == SteamInstallationKind.Windows)
        {
            if (string.IsNullOrWhiteSpace(steamRoot))
            {
                throw new DirectoryNotFoundException("The Windows Steam folder is not configured.");
            }

            var steamExe = Path.Combine(Path.GetFullPath(steamRoot), "steam.exe");
            if (!fileExists(steamExe))
            {
                throw new FileNotFoundException("Steam.exe was not found in the configured Steam folder.", steamExe);
            }

            return new(kind,
                new SteamCommand(steamExe, ["-shutdown"]),
                new SteamCommand(steamExe, []));
        }

        if (kind == SteamInstallationKind.Flatpak)
        {
            var flatpak = FindExecutable("flatpak", pathEnvironment, fileExists);
            return new(kind,
                new SteamCommand(flatpak, ["run", "com.valvesoftware.Steam", "-shutdown"]),
                new SteamCommand(flatpak, ["run", "com.valvesoftware.Steam"]));
        }

        var steam = FindExecutable("steam", pathEnvironment, fileExists);
        return new(kind, new SteamCommand(steam, ["-shutdown"]), new SteamCommand(steam, []));
    }

    private static string FindExecutable(string name, string pathEnvironment, Func<string, bool> fileExists)
    {
        foreach (var directory in pathEnvironment.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(directory, name)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { continue; }
            if (fileExists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find {name} on PATH.");
    }
}
