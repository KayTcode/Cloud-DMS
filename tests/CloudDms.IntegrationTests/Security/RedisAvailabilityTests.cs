using System.Diagnostics;
using CleanArchCqrs.Application.Common.Exceptions;
using CloudDms.IntegrationTests.Support;
using CleanArchCqrs.Infrastructure.Security;
using Xunit;

namespace CloudDms.IntegrationTests.Security;

[CollectionDefinition("Redis security readiness", DisableParallelization = true)]
public sealed class RedisSecurityCollection : ICollectionFixture<RedisFixture>
{
}

[Collection("Redis security readiness")]
public sealed class RedisAvailabilityTests(RedisFixture fixture)
{
    [Fact]
    public async Task DownFailsWithinConfiguredTimeout()
    {
        using var client = fixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var runId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        await readiness.SetReadyAsync("generation-down-test", runId, CancellationToken.None);
        await fixture.StopAsync();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<SecurityUnavailableException>(
                () => readiness.AssertReadyAsync(CancellationToken.None));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        }
        finally
        {
            await fixture.StartAsync();
        }
    }

    [Fact]
    public async Task RestartDoesNotTrustReadinessRestoredFromAof()
    {
        using var client = fixture.CreateClient();
        var readiness = new RedisReadiness(client);
        var oldRunId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
        await readiness.SetReadyAsync("generation-before-restart", oldRunId, CancellationToken.None);
        Assert.Equal("generation-before-restart", await readiness.AssertReadyAsync(CancellationToken.None));

        await fixture.RestartAsync();

        var stopwatch = Stopwatch.StartNew();
        string? newRunId = null;
        string? previousRunId = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            try
            {
                var currentRunId = await readiness.GetCurrentServerRunIdAsync(CancellationToken.None);
                if (!string.Equals(oldRunId, currentRunId, StringComparison.Ordinal)
                    && string.Equals(previousRunId, currentRunId, StringComparison.Ordinal))
                {
                    newRunId = currentRunId;
                    break;
                }

                previousRunId = currentRunId;
            }
            catch (SecurityUnavailableException)
            {
                previousRunId = null;
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        Assert.NotNull(newRunId);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAsync<SecurityUnavailableException>(
            () => readiness.AssertReadyAsync(CancellationToken.None));
    }
}
