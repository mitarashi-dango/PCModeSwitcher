using PCModeSwitcher.Services;

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
            ? LocalizationService.Format("Status.ModeApplied", modeName)
            : Steps.Any(step => step.IsSuccess && !step.IsSkipped)
                ? LocalizationService.Format("Status.ModePartiallyApplied", modeName)
                : LocalizationService.Format("Status.ModeFailed", modeName);
        var details = string.Join(Environment.NewLine, Steps.Select(FormatStep));
        return $"{heading}{Environment.NewLine}{Environment.NewLine}{details}";
    }

    private static string FormatStep(ApplyStepResult step)
    {
        var name = LocalizationService.Translate(step.DisplayName ?? step.Name);
        if (step.IsSkipped)
            return $"– {name}";
        if (step.IsSuccess)
            return $"✓ {name}";

        var reason = LocalizationService.Translate(step.Message).Trim().TrimEnd('。', '.');
        return string.IsNullOrWhiteSpace(reason)
            ? $"⚠ {name}"
            : LocalizationService.Current.ResolvedLanguage == AppLanguages.Japanese
                ? $"⚠ {name}（{reason}）"
                : $"⚠ {name} ({reason})";
    }
}
