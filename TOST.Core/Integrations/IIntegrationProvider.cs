namespace Trionine.TOST.Core.Integrations;

public interface IIntegrationProvider
{
    string Id { get; }
    string DisplayName { get; }
    ValueTask<IntegrationStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
