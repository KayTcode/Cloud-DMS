using Microsoft.AspNetCore.Mvc;
using CleanArchCqrs.Application.Common.Exceptions;

namespace CleanArchCqrs.API.Middleware;

/// <summary>
/// Global exception handling middleware with RFC 7807 ProblemDetails responses.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (SecurityUnavailableException)
        {
            _logger.LogWarning(
                "Security dependency unavailable while processing {Path}",
                context.Request.Path);
            await WriteProblemDetailsAsync(context, new SecurityUnavailableException());
        }
        catch (AuthorizationChangedException ex)
        {
            _logger.LogWarning(
                "Authorization state changed before processing {Path}",
                context.Request.Path);
            await WriteProblemDetailsAsync(context, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred while processing {Path}", context.Request.Path);
            await WriteProblemDetailsAsync(context, ex);
        }
    }

    private static async Task WriteProblemDetailsAsync(HttpContext context, Exception exception)
    {
        var statusCode = exception switch
        {
            AuthorizationChangedException or
                AuthorizationMutationConflictException or
                AuthorizationMutationPendingException => StatusCodes.Status409Conflict,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            SecurityUnavailableException => StatusCodes.Status503ServiceUnavailable,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status400BadRequest => "Bad request",
                StatusCodes.Status401Unauthorized => "Unauthorized",
                StatusCodes.Status404NotFound => "Resource not found",
                StatusCodes.Status503ServiceUnavailable => "Security dependency unavailable",
                _ => "Internal server error"
            },
            Detail = exception.InnerException != null 
                ? $"{exception.Message} ---> {exception.InnerException.Message}" 
                : exception.Message,
            Instance = context.Request.Path
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            problem.Type = "https://httpstatuses.com/500";
        }
        else if (statusCode == StatusCodes.Status401Unauthorized)
        {
            problem.Type = "https://httpstatuses.com/401";
        }
        else if (statusCode == StatusCodes.Status404NotFound)
        {
            problem.Type = "https://httpstatuses.com/404";
        }
        else if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            problem.Type = "https://httpstatuses.com/503";
        }
        else if (statusCode == StatusCodes.Status409Conflict)
        {
            problem.Type = "https://httpstatuses.com/409";
        }
        else if (statusCode == StatusCodes.Status400BadRequest)
        {
            problem.Type = "https://httpstatuses.com/400";
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
