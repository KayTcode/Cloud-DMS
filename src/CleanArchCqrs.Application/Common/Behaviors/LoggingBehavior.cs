using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Application.Common.Behaviors;

/// <summary>
/// Pipeline behavior for structured logging of MediatR requests and responses,
/// tracking execution duration and capturing failures.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request, 
        RequestHandlerDelegate<TResponse> next, 
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var correlationId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "[{CorrelationId}] Handling request {RequestName}",
            correlationId, requestName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            _logger.LogInformation(
                "[{CorrelationId}] Handled request {RequestName} successfully in {ElapsedMilliseconds}ms",
                correlationId, requestName, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (request is ISensitiveReadRequest)
            {
                _logger.LogWarning("[{CorrelationId}] Sensitive read {RequestName} failed after {ElapsedMilliseconds}ms",
                    correlationId, requestName, stopwatch.ElapsedMilliseconds);
                throw;
            }

            _logger.LogError("[{CorrelationId}] Request {RequestName} failed after {ElapsedMilliseconds}ms ({ExceptionType})",
                correlationId, requestName, stopwatch.ElapsedMilliseconds, ex.GetType().Name);

            throw;
        }
    }
}
