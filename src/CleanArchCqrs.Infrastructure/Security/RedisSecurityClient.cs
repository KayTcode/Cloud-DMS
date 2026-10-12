using System.Reflection;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class RedisSecurityClient : ISecurityRedis, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> ScriptResources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["set-readiness"] = "CleanArchCqrs.Infrastructure.Security.Scripts.set-readiness.lua",
            ["get-readiness"] = "CleanArchCqrs.Infrastructure.Security.Scripts.get-readiness.lua",
            ["evaluate-access"] = "CleanArchCqrs.Infrastructure.Security.Scripts.evaluate-access.lua",
            ["begin-change"] = "CleanArchCqrs.Infrastructure.Security.Scripts.begin-change.lua",
            ["cancel-change"] = "CleanArchCqrs.Infrastructure.Security.Scripts.cancel-change.lua",
            ["publish-change"] = "CleanArchCqrs.Infrastructure.Security.Scripts.publish-change.lua",
            ["list-scope-projections"] = "CleanArchCqrs.Infrastructure.Security.Scripts.list-scope-projections.lua",
            ["set-scope-projection"] = "CleanArchCqrs.Infrastructure.Security.Scripts.set-scope-projection.lua",
            ["remove-scope-projection"] = "CleanArchCqrs.Infrastructure.Security.Scripts.remove-scope-projection.lua"
        };

    private readonly ConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly RedisOptions _options;
    private readonly IReadOnlyDictionary<string, string> _scripts;
    private readonly ILogger<RedisSecurityClient> _logger;
    private readonly object _circuitLock = new();
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenUntil = DateTimeOffset.MinValue;

    public RedisSecurityClient(
        IOptions<RedisOptions> options,
        ILogger<RedisSecurityClient> logger)
    {
        _options = options.Value;
        _options.Validate();
        _logger = logger;
        _scripts = ScriptResources.ToDictionary(
            entry => entry.Key,
            entry => ReadEmbeddedScript(entry.Value),
            StringComparer.Ordinal);

        var configuration = ConfigurationOptions.Parse(_options.ConnectionString!);
        configuration.AbortOnConnectFail = false;
        configuration.AllowAdmin = true;
        configuration.AsyncTimeout = _options.CommandTimeoutMilliseconds;
        configuration.SyncTimeout = _options.CommandTimeoutMilliseconds;
        configuration.ConnectTimeout = _options.ConnectTimeoutMilliseconds;
        configuration.ConnectRetry = 0;
        configuration.BacklogPolicy = BacklogPolicy.FailFast;

        _multiplexer = ConnectionMultiplexer.Connect(configuration);
        var endpoints = _multiplexer.GetEndPoints();
        if (endpoints.Length != 1)
        {
            _multiplexer.Dispose();
            throw new InvalidOperationException("Security Redis must use one standalone primary endpoint.");
        }

        _database = _multiplexer.GetDatabase();
    }

    public async Task<string> ExecuteAsync(
        string scriptName,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (!_scripts.TryGetValue(scriptName, out var script))
        {
            throw new ArgumentOutOfRangeException(nameof(scriptName), "The Redis security script is not allow-listed.");
        }

        var redisKeys = keys.Select(key => (RedisKey)key).ToArray();
        var redisArgs = args.Select(value => (RedisValue)value).ToArray();

        return await RunRedisCommandAsync(
            async () =>
            {
                var result = await _database.ScriptEvaluateAsync(script, redisKeys, redisArgs);
                if (result.IsNull)
                {
                    throw new RedisException("The security Redis script returned no result.");
                }

                return result.ToString();
            },
            cancellationToken);
    }

    internal Task<string> GetServerRunIdAsync(CancellationToken cancellationToken)
    {
        return RunRedisCommandAsync(async () =>
        {
            var endpoint = _multiplexer.GetEndPoints().Single();
            var sections = await _multiplexer.GetServer(endpoint).InfoAsync("server");
            foreach (var section in sections)
            {
                foreach (var entry in section)
                {
                    if (string.Equals(entry.Key, "run_id", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(entry.Value))
                    {
                        return entry.Value;
                    }
                }
            }

            throw new RedisException("Redis INFO server did not include a run_id.");
        }, cancellationToken);
    }

    public void Dispose() => _multiplexer.Dispose();

    private async Task<T> RunRedisCommandAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfCircuitOpen();

        try
        {
            var result = await operation().WaitAsync(cancellationToken);
            RegisterSuccess();
            return result;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            RegisterFailure();
            throw new SecurityUnavailableException(exception);
        }
    }

    private void ThrowIfCircuitOpen()
    {
        lock (_circuitLock)
        {
            if (DateTimeOffset.UtcNow < _circuitOpenUntil)
            {
                throw new SecurityUnavailableException();
            }
        }
    }

    private void RegisterSuccess()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures = 0;
            _circuitOpenUntil = DateTimeOffset.MinValue;
        }
    }

    private void RegisterFailure()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _options.CircuitFailureThreshold)
            {
                _circuitOpenUntil = DateTimeOffset.UtcNow.AddMilliseconds(_options.CircuitOpenMilliseconds);
                _logger.LogWarning("Security Redis circuit opened; authorization stays fail-closed.");
            }
        }
    }

    private static string ReadEmbeddedScript(string resourceName)
    {
        using var stream = typeof(RedisSecurityClient).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Redis script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
