namespace PCModeSwitcher.Models;

public sealed record ApplyStepResult(
    string Name,
    bool IsSuccess,
    string Message,
    string? TechnicalDetails = null,
    bool IsSkipped = false,
    string? DisplayName = null);

public sealed class ModeApplyResult
{
    public required IReadOnlyList<ApplyStepResult> Steps { get; init; }
    public bool IsSuccess => Steps.All(step => step.IsSuccess);

    public string ToUserMessage(string modeName)
    {
        var heading = IsSuccess
            ? $"{modeName}モードに切り替えました"
            : Steps.Any(step => step.IsSuccess && !step.IsSkipped)
                ? $"{modeName}モードを一部適用しました"
                : $"{modeName}モードを適用できませんでした";
        var details = string.Join(Environment.NewLine, Steps.Select(FormatStep));
        return $"{heading}{Environment.NewLine}{Environment.NewLine}{details}";
    }

    private static string FormatStep(ApplyStepResult step)
    {
        var name = step.DisplayName ?? step.Name;
        if (step.IsSkipped)
            return $"– {name}";
        if (step.IsSuccess)
            return $"✓ {name}";

        var reason = step.Message.Trim().TrimEnd('。');
        return string.IsNullOrWhiteSpace(reason)
            ? $"⚠ {name}"
            : $"⚠ {name}（{reason}）";
    }
}
