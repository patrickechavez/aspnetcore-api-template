namespace ApiTemplate.Common;

public enum ResultError
{
    None,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed class Result
{
    private Result(bool isSuccess, ResultError error, string? message)
    {
        IsSuccess = isSuccess;
        Error = error;
        Message = message;
    }

    public bool IsSuccess { get; }

    public ResultError Error { get; }

    public string? Message { get; }

    public static Result Success() => new(true, ResultError.None, null);

    public static Result Validation(string message) => new(false, ResultError.Validation, message);

    public static Result Unauthorized(string message) => new(false, ResultError.Unauthorized, message);

    public static Result Forbidden(string message) => new(false, ResultError.Forbidden, message);

    public static Result NotFound(string message) => new(false, ResultError.NotFound, message);

    public static Result Conflict(string message) => new(false, ResultError.Conflict, message);
}

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, ResultError error, string? message)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Message = message;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public ResultError Error { get; }

    public string? Message { get; }

    public static Result<T> Success(T value) => new(true, value, ResultError.None, null);

    public static Result<T> Validation(string message) => new(false, default, ResultError.Validation, message);

    public static Result<T> Unauthorized(string message) => new(false, default, ResultError.Unauthorized, message);

    public static Result<T> Forbidden(string message) => new(false, default, ResultError.Forbidden, message);

    public static Result<T> NotFound(string message) => new(false, default, ResultError.NotFound, message);

    public static Result<T> Conflict(string message) => new(false, default, ResultError.Conflict, message);
}
