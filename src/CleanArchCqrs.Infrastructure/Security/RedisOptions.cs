namespace CleanArchCqrs.Infrastructure.Security;

public sealed class RedisOptions
{
    public const string SectionName = "SecurityRedis";

    public string? ConnectionString { get; set; }
    public int CommandTimeoutMilliseconds { get; set; } = 1_000;
    public int ConnectTimeoutMilliseconds { get; set; } = 2_000;
    public int CircuitFailureThreshold { get; set; } = 3;
    public int CircuitOpenMilliseconds { get; set; } = 5_000;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "SecurityRedis:ConnectionString must be configured before security Redis is used.");
        }

        if (CommandTimeoutMilliseconds is < 1 or > 30_000
            || ConnectTimeoutMilliseconds is < 1 or > 30_000
            || CircuitFailureThreshold < 1
            || CircuitOpenMilliseconds < 1)
        {
            throw new InvalidOperationException("SecurityRedis timeout and circuit settings are invalid.");
        }
    }
}
