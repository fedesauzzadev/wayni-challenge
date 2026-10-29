using Bcra.Shared.Domain.Errors;

namespace Bcra.Shared.Domain.Results;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error ?? Error.None;
    }

    public static Result Ok() => new(true, null);
    public static Result Fail(Error error) => new(false, error);

    public static Result<TValue> Ok<TValue>(TValue value) => Result<TValue>.Ok(value);
    public static Result<TValue> Fail<TValue>(Error error) => Result<TValue>.Fail(error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");

    public static Result<TValue> Ok(TValue value) => new(value, true, null);
    public static new Result<TValue> Fail(Error error) => new(default, false, error);
}
