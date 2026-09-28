using System.Text.Json.Serialization;

namespace CleanArchCqrs.Application.Common.Models;

/// <summary>
/// Standard Result pattern for operation outcomes without throwing business exceptions.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error => Errors.Count > 0 ? Errors[0] : null;
    public IReadOnlyList<string> Errors { get; }

    [JsonConstructor]
    protected Result(bool isSuccess, IEnumerable<string>? errors = null)
    {
        if (isSuccess && errors != null && errors.Any())
        {
            throw new InvalidOperationException("A successful result cannot contain error messages.");
        }

        if (!isSuccess && (errors == null || !errors.Any()))
        {
            throw new InvalidOperationException("A failure result must contain at least one error message.");
        }

        IsSuccess = isSuccess;
        Errors = errors?.ToList().AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    public static Result Success() => new(true);

    public static Result Failure(string error) => new(false, new[] { error });

    public static Result Failure(IEnumerable<string> errors) => new(false, errors);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);

    public static Result<T> Failure<T>(IEnumerable<string> errors) => Result<T>.Failure(errors);
}

/// <summary>
/// Generic Result pattern encapsulating return data payload.
/// </summary>
/// <typeparam name="T">Payload data type</typeparam>
public class Result<T> : Result
{
    private readonly T? _value;

    public T? Value => _value;

    [JsonConstructor]
    protected Result(T? value, bool isSuccess, IEnumerable<string>? errors = null)
        : base(isSuccess, errors)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(value, true);

    public static new Result<T> Failure(string error) => new(default, false, new[] { error });

    public static new Result<T> Failure(IEnumerable<string> errors) => new(default, false, errors);

    public static implicit operator Result<T>(T value) => Success(value);
}
