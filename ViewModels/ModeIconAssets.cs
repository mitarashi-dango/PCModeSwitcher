namespace PCModeSwitcher.ViewModels;

public static class ModeIconAssets
{
    public static bool HasCustomIcon(string? modeId) =>
        GetCustomIconSource(modeId) is not null;

    public static string? GetCustomIconSource(string? modeId) => modeId?.ToLowerInvariant() switch
    {
        "custom1" => "/Assets/FluentEmojiHighContrast/Custom1Icon.png",
        "custom2" => "/Assets/FluentEmojiHighContrast/Custom2Icon.png",
        "custom3" => "/Assets/FluentEmojiHighContrast/Custom3Icon.png",
        "custom4" => "/Assets/FluentEmojiHighContrast/Custom4Icon.png",
        "custom5" => "/Assets/FluentEmojiHighContrast/Custom5Icon.png",
        "custom6" => "/Assets/FluentEmojiHighContrast/Custom6Icon.png",
        _ => null
    };
}
