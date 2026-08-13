namespace PCModeSwitcher.Models;

public sealed record ModeDetectionResult(string? ModeId)
{
    public bool IsUnregistered => ModeId is null;
}
