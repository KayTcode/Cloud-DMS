namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ISecurityReadiness
{
    Task<string> GetCurrentServerRunIdAsync(CancellationToken cancellationToken);

    Task<string> AssertReadyAsync(CancellationToken cancellationToken);

    Task SetReadyAsync(
        string generation,
        string serverRunId,
        CancellationToken cancellationToken);
}
