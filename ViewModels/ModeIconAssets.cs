using PCModeSwitcher.Models;

namespace PCModeSwitcher.ViewModels;

public static class ModeIconAssets
{
    private const string IiControllerSource = "/Assets/GameModeIcon.png";

    public static bool HasCustomIcon(string? modeId, string? icon = null) =>
        GetCustomIconSource(modeId, icon) is not null;

    public static string? GetCustomIconSource(string? modeId, string? icon = null)
    {
        if (ModeIconKeys.IsIiController(icon))
            return IiControllerSource;

        return modeId?.ToLowerInvariant() switch
        {
            "game" => IiControllerSource,
            "custom1" => "/Assets/FluentEmojiHighContrast/Custom1Icon.png",
            "custom2" => "/Assets/FluentEmojiHighContrast/Custom2Icon.png",
            "custom3" => "/Assets/FluentEmojiHighContrast/Custom3Icon.png",
            "custom4" => "/Assets/FluentEmojiHighContrast/Custom4Icon.png",
            "custom5" => "/Assets/FluentEmojiHighContrast/Custom5Icon.png",
            "custom6" => "/Assets/FluentEmojiHighContrast/Custom6Icon.png",
            "custom7" => "/Assets/FluentEmojiHighContrast/Custom7Icon.png",
            "custom8" => "/Assets/FluentEmojiHighContrast/Custom8Icon.png",
            "custom9" => "/Assets/FluentEmojiHighContrast/Custom9Icon.png",
            "custom10" => "/Assets/FluentEmojiHighContrast/Custom10Icon.png",
            "custom11" => "/Assets/FluentEmojiHighContrast/Custom11Icon.png",
            "custom12" => "/Assets/FluentEmojiHighContrast/Custom12Icon.png",
            _ => null
        };
    }
}
