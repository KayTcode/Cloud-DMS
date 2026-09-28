using CleanArchCqrs.Application.Common.Models;
using FluentValidation;
using MediatR;
using System.Reflection;

namespace CleanArchCqrs.Application.Common.Behaviors;

/// <summary>
/// Pipeline behavior that automatically runs all FluentValidation validators registered for the request.
/// If validation errors exist, it intercepts the execution pipeline and returns a Result.Failure without throwing an exception.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var errors = failures.Select(f => f.ErrorMessage).Distinct().ToList();

        // If TResponse is non-generic Result
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(errors);
        }

        // If TResponse is generic Result<T>
        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = typeof(TResponse).GetMethod(
                nameof(Result.Failure),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(IEnumerable<string>) },
                null);

            if (failureMethod != null)
            {
                return (TResponse)failureMethod.Invoke(null, new object[] { errors })!;
            }
        }

        // Fallback for non-Result responses
        throw new ValidationException(failures);
    }
}
