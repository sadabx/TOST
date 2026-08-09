namespace Trionine.TOST.Core.Integrations.SlsSteam;

public sealed class SlsSteamProvider : IIntegrationProvider
{
    private readonly SlsSteamPaths paths;

    public SlsSteamProvider(SlsSteamPaths paths)
    {
        this.paths = paths;
    }

    public string Id => "slssteam";
    public string DisplayName => "SLSsteam";

    public ValueTask<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var components = new[]
        {
            Component("Main library", paths.MainLibraryPath),
            Component("Injection library", paths.InjectorLibraryPath),
            Component("Configuration", paths.ConfigPath)
        };
        var existingCount = components.Count(component => component.Exists);
        var health = existingCount switch
        {
            0 => IntegrationHealth.NotInstalled,
            _ when existingCount == components.Length => IntegrationHealth.Ready,
            _ => IntegrationHealth.Incomplete
        };
        var summary = health switch
        {
            IntegrationHealth.Ready => "All required SLSsteam files were found.",
            IntegrationHealth.Incomplete => "SLSsteam is only partially installed.",
            _ => "SLSsteam was not found."
        };

        return ValueTask.FromResult(new IntegrationStatus(Id, DisplayName, health, summary, components));
    }

    private static IntegrationComponent Component(string name, string path) =>
        new(name, path, File.Exists(path));
}
