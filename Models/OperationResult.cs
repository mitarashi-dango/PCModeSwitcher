namespace PCModeSwitcher.Models;

public sealed record OperationResult(bool IsSuccess, string UserMessage, string? TechnicalDetails = null)
{
    public static OperationResult Success(string message = "") => new(true, message);
    public static OperationResult Failure(string message, string? details = null) =>
        new(false, message, details);
}

public sealed record OperationResult<T>(bool IsSuccess, T? Value, string UserMessage, string? TechnicalDetails = null)
{
    public static OperationResult<T> Success(T value, string message = "") => new(true, value, message);
    public static OperationResult<T> Failure(string message, string? details = null) =>
        new(false, default, message, details);
}
