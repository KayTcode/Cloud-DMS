using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Workers;

public sealed class AuthorizationOutboxWorker(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<AuthorizationOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Authorization outbox polling failed; pending scopes remain fail-closed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuthorizationProjectionWriter>();
        var operationIds = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.CreatedAt)
            .Select(message => new { message.AggregateKey, message.Version })
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var item in operationIds)
        {
            try
            {
                var operationId = await dbContext.AuthorizationOperations
                    .AsNoTracking()
                    .Where(operation => operation.ScopeKey == item.AggregateKey
                        && operation.Version == item.Version)
                    .Select(operation => operation.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (operationId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"Outbox item '{item.AggregateKey}' version {item.Version} has no operation.");
                }

                await writer.PublishAsync(operationId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Authorization outbox processing failed for scope {ScopeKey}, version {Version}; access remains fail-closed.",
                    item.AggregateKey,
                    item.Version);
            }
        }

    }
}
