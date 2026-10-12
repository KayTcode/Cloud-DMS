using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace CloudDms.IntegrationTests.Support;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly string _containerName = $"cloud-dms-redis-tests-{Guid.NewGuid():N}";
    private uint _publishedPort;

    public async Task InitializeAsync()
    {
        _publishedPort = ReserveLoopbackPort();
        await RunDockerAsync(
            "run",
            "--detach",
            "--name",
            _containerName,
            "--publish",
            $"127.0.0.1:{_publishedPort}:6379",
            "redis:7.4-alpine",
            "redis-server",
            "--bind",
            "0.0.0.0",
            "--protected-mode",
            "no",
            "--appendonly",
            "yes",
            "--appendfsync",
            "always",
            "--save",
            "");

        await WaitForRedisAsync();
    }

    public RedisSecurityClient CreateClient()
    {
        if (_publishedPort == 0)
        {
            throw new InvalidOperationException("The Redis fixture has not been initialized.");
        }

        return new RedisSecurityClient(
            Options.Create(new RedisOptions
            {
                ConnectionString = $"127.0.0.1:{_publishedPort}",
                CommandTimeoutMilliseconds = 1_000,
                ConnectTimeoutMilliseconds = 2_000,
                CircuitFailureThreshold = 3,
                CircuitOpenMilliseconds = 5_000
            }),
            NullLogger<RedisSecurityClient>.Instance);
    }

    public async Task SetStringAsync(string key, string value)
    {
        using var multiplexer = await ConnectionMultiplexer.ConnectAsync($"127.0.0.1:{_publishedPort}");
        await multiplexer.GetDatabase().StringSetAsync(key, value);
    }

    public async Task<string?> GetStringAsync(string key)
    {
        using var multiplexer = await ConnectionMultiplexer.ConnectAsync($"127.0.0.1:{_publishedPort}");
        var value = await multiplexer.GetDatabase().StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public Task StopAsync() => RunDockerAsync("stop", _containerName);

    public async Task StartAsync()
    {
        await RunDockerAsync("start", _containerName);
        await WaitForRedisAsync();
    }

    public async Task RestartAsync()
    {
        await RunDockerAsync("restart", _containerName);
        await WaitForRedisAsync();
    }

    public async Task DisposeAsync()
    {
        await RunDockerAsync("rm", "--force", _containerName);
    }

    private static async Task<string> RunDockerAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Docker CLI.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker command failed: {error.Trim()}");
        }

        return output.Trim();
    }

    private static uint ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (uint)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private async Task WaitForRedisAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Exception? lastFailure = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await RunDockerAsync("exec", _containerName, "redis-cli", "ping");
                if (string.Equals(response, "PONG", StringComparison.Ordinal))
                {
                    using var client = CreateClient();
                    await new RedisReadiness(client).GetCurrentServerRunIdAsync(timeout.Token);
                    return;
                }

                lastFailure = new InvalidOperationException("Redis returned an unexpected ping response.");
            }
            catch (SecurityUnavailableException exception)
            {
                lastFailure = exception;
            }
            catch (InvalidOperationException exception)
            {
                lastFailure = exception;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        throw new InvalidOperationException("Redis did not become ready after restart.", lastFailure);
    }
}
