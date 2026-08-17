namespace PCModeSwitcher.Models;

public static class ModeIconKeys
{
    public const string IiController = "ii-controller";

    public static bool IsIiController(string? icon) =>
        string.Equals(icon?.Trim(), IiController, StringComparison.Ordinal);
}
