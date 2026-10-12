namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ISecurityRedis
{
    Task<string> ExecuteAsync(
        string scriptName,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken);
}
