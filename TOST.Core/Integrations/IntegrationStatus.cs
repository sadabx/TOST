namespace Trionine.TOST.Core.Integrations;

public enum IntegrationHealth
{
    NotInstalled,
    Incomplete,
    Ready
}

public sealed record IntegrationStatus(
    string ProviderId,
    string DisplayName,
    IntegrationHealth Health,
    string Summary,
    IReadOnlyList<IntegrationComponent> Components);

public sealed record IntegrationComponent(
    string Name,
    string Path,
    bool Exists,
    bool Required = true);
