using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class ModeActionContext
{
    public required PcMode Mode { get; init; }
    public required ModeSessionSnapshot Session { get; init; }
}

public sealed record ActionPreflightResult(
    bool CanContinue,
    ActionExecutionStatus Status,
    string Message,
    string? TechnicalDetails = null)
{
    public static ActionPreflightResult Ready() =>
        new(true, ActionExecutionStatus.Pending, "利用できます。");

    public static ActionPreflightResult Skip(
        ActionExecutionStatus status,
        string message,
        string? details = null) => new(true, status, message, details);

    public static ActionPreflightResult Fatal(string message, string? details = null) =>
        new(false, ActionExecutionStatus.ApplyFailed, message, details);
}

public sealed record ActionCaptureResult(
    bool CanApply,
    JsonElement State,
    ActionExecutionStatus Status,
    string Message,
    string? TechnicalDetails = null)
{
    public static ActionCaptureResult Success<T>(T state) =>
        new(true, JsonSerializer.SerializeToElement(state), ActionExecutionStatus.Pending, "現在状態を記録しました。");

    public static ActionCaptureResult Skip(
        ActionExecutionStatus status,
        string message,
        string? details = null) =>
        new(false, JsonSerializer.SerializeToElement(new { }), status, message, details);
}

public interface IModeActionHandler
{
    string Id { get; }
    string DisplayName { get; }
    Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken);
    Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken);
    Task<ActionExecutionResult> ApplyAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken);
    Task<ActionExecutionResult> RestoreAsync(
        ModeActionContext context,
        ActionSnapshot snapshot,
        CancellationToken cancellationToken);
}

internal static class ActionResults
{
    public static ActionExecutionResult Create(
        IModeActionHandler handler,
        ActionExecutionStatus status,
        string message,
        string? technicalDetails = null) => new()
    {
        ActionId = handler.Id,
        DisplayName = handler.DisplayName,
        Status = status,
        Message = message,
        TechnicalDetails = technicalDetails
    };

    public static bool IsApplied(this ActionExecutionStatus status) =>
        status == ActionExecutionStatus.Succeeded;
}
