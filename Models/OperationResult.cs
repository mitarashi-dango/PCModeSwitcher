using PCModeSwitcher.Services;

namespace PCModeSwitcher.Models;

public sealed record OperationResult(bool IsSuccess, string UserMessage, string? TechnicalDetails = null)
{
    public static OperationResult Success(string message = "") => new(true, LocalizationService.Translate(message));
    public static OperationResult Failure(string message, string? details = null) =>
        new(false, LocalizationService.Translate(message), details);
}

public sealed record OperationResult<T>(bool IsSuccess, T? Value, string UserMessage, string? TechnicalDetails = null)
{
    public static OperationResult<T> Success(T value, string message = "") =>
        new(true, value, LocalizationService.Translate(message));
    public static OperationResult<T> Failure(string message, string? details = null) =>
        new(false, default, LocalizationService.Translate(message), details);
}
