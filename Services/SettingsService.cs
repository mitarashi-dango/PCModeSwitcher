using System.IO;
using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class SettingsService
{
    public const int MaximumVisibleModeCount = 5;

    private const string LegacyCustomModeId = "custom";
    private static readonly string[] RequiredModeIds =
        ["game", "work", "normal", "custom1", "custom2", "custom3", "custom4", "custom5", "custom6"];
    private static readonly string[] DefaultVisibleModeIds =
        ["game", "work", "normal", "custom1", "custom2"];

    public static IReadOnlyList<string> SupportedModeIds => RequiredModeIds;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsService(string? settingsDirectory = null)
    {
        _settingsDirectory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCModeSwitcher");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
    }

    public async Task<OperationResult<AppSettings>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return OperationResult<AppSettings>.Success(CreateDefaults());

            await using var stream = new FileStream(
                _settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
            if (settings is null)
                return OperationResult<AppSettings>.Failure("保存済みのモード設定を読み込めませんでした。");

            Normalize(settings);
            if (!IsValid(settings))
                return OperationResult<AppSettings>.Failure("保存済みのモード設定を読み込めませんでした。");

            return OperationResult<AppSettings>.Success(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult<AppSettings>.Failure(
                "保存済みのモード設定を読み込めませんでした。", ex.Message);
        }
    }

    public async Task<OperationResult> SaveAsync(AppSettings settings)
    {
        Normalize(settings);
        if (!IsValid(settings))
            return OperationResult.Failure("モード設定が正しくないため保存しませんでした。");

        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryPath = Path.Combine(_settingsDirectory, $"settings.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
                    await stream.FlushAsync();
                }

                File.Move(temporaryPath, _settingsPath, true);
                return OperationResult.Success("モード設定を保存しました。");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("モード設定を保存できませんでした。", ex.Message);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public static AppSettings CreateDefaults() => new()
    {
        Modes = CreateDefaultModes(),
        Hotkeys = CreateDefaultHotkeys(),
        VisibleModeIds = [.. DefaultVisibleModeIds]
    };

    private static List<PcMode> CreateDefaultModes() =>
        [
            new PcMode
            {
                Id = "game", Name = "GAME", Icon = "🎮",
                DisplayTimeoutAc = 0, DisplayTimeoutBattery = 0,
                SleepTimeoutAc = 0, SleepTimeoutBattery = 0,
                PowerPlanId = PowerSettingsService.HighPerformanceSchemeId,
                MicrophoneMute = MicrophoneMuteSetting.NoChange
            },
            new PcMode
            {
                Id = "work", Name = "WORK", Icon = "💼",
                DisplayTimeoutAc = 10 * 60, DisplayTimeoutBattery = 10 * 60,
                SleepTimeoutAc = 30 * 60, SleepTimeoutBattery = 30 * 60,
                PowerPlanId = PowerSettingsService.BalancedSchemeId,
                MicrophoneMute = MicrophoneMuteSetting.NoChange
            },
            new PcMode
            {
                Id = "normal", Name = "NORMAL", Icon = "🖥",
                DisplayTimeoutAc = 5 * 60, DisplayTimeoutBattery = 5 * 60,
                SleepTimeoutAc = 15 * 60, SleepTimeoutBattery = 15 * 60,
                PowerPlanId = PowerSettingsService.BalancedSchemeId,
                MicrophoneMute = MicrophoneMuteSetting.NoChange
            },
            CreateCustomMode(1, "\U0001F42D\uFE0E"),
            CreateCustomMode(2, "\U0001F42E\uFE0E"),
            CreateCustomMode(3, "\U0001F42F\uFE0E"),
            CreateCustomMode(4, "\U0001F430\uFE0E"),
            CreateCustomMode(5, "\U0001F432\uFE0E"),
            CreateCustomMode(6, "\U0001F40D\uFE0E")
        ];

    private static PcMode CreateCustomMode(int number, string icon) => new()
    {
        Id = $"custom{number}", Name = $"CUSTOM{number}", Icon = icon,
        DisplayTimeoutAc = 15 * 60, DisplayTimeoutBattery = 15 * 60,
        SleepTimeoutAc = 60 * 60, SleepTimeoutBattery = 60 * 60,
        PowerPlanId = PowerSettingsService.BalancedSchemeId,
        MicrophoneMute = MicrophoneMuteSetting.NoChange
    };

    public static List<ModeHotkey> CreateDefaultHotkeys() =>
    [
        new() { ModeId = "game" },
        new() { ModeId = "work" },
        new() { ModeId = "normal" },
        new() { ModeId = "custom1" },
        new() { ModeId = "custom2" },
        new() { ModeId = "custom3" },
        new() { ModeId = "custom4" },
        new() { ModeId = "custom5" },
        new() { ModeId = "custom6" }
    ];

    private static void Normalize(AppSettings settings)
    {
        settings.Modes ??= [];
        settings.Hotkeys ??= [];
        settings.VisibleModeIds ??= [];

        MigrateLegacyCustomMode(settings);

        foreach (var defaultMode in CreateDefaultModes())
        {
            if (!settings.Modes.Any(mode =>
                string.Equals(mode.Id, defaultMode.Id, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Modes.Add(defaultMode);
            }
        }

        foreach (var defaultHotkey in CreateDefaultHotkeys())
        {
            if (!settings.Hotkeys.Any(hotkey =>
                string.Equals(hotkey.ModeId, defaultHotkey.ModeId, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Hotkeys.Add(defaultHotkey);
            }
        }

        // 名前とアイコンは固定。保存済みの各モード設定値はそのまま引き継ぐ。
        foreach (var defaultMode in CreateDefaultModes())
        {
            var mode = settings.Modes.First(value =>
                string.Equals(value.Id, defaultMode.Id, StringComparison.OrdinalIgnoreCase));
            mode.Id = defaultMode.Id;
            mode.Name = defaultMode.Name;
            mode.Icon = defaultMode.Icon;
        }

        settings.Modes = RequiredModeIds
            .Select(modeId => settings.Modes.First(mode =>
                string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        settings.Hotkeys = RequiredModeIds
            .Select(modeId => settings.Hotkeys.First(hotkey =>
                string.Equals(hotkey.ModeId, modeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        settings.VisibleModeIds = NormalizeVisibleModeIds(settings.VisibleModeIds);
    }

    private static void MigrateLegacyCustomMode(AppSettings settings)
    {
        var legacyMode = settings.Modes.FirstOrDefault(mode =>
            string.Equals(mode.Id, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase));
        var custom1Exists = settings.Modes.Any(mode =>
            string.Equals(mode.Id, "custom1", StringComparison.OrdinalIgnoreCase));
        if (legacyMode is not null)
        {
            if (custom1Exists)
                settings.Modes.Remove(legacyMode);
            else
                legacyMode.Id = "custom1";
        }

        var legacyHotkey = settings.Hotkeys.FirstOrDefault(hotkey =>
            string.Equals(hotkey.ModeId, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase));
        var custom1HotkeyExists = settings.Hotkeys.Any(hotkey =>
            string.Equals(hotkey.ModeId, "custom1", StringComparison.OrdinalIgnoreCase));
        if (legacyHotkey is not null)
        {
            if (custom1HotkeyExists)
                settings.Hotkeys.Remove(legacyHotkey);
            else
                legacyHotkey.ModeId = "custom1";
        }

        if (string.Equals(settings.LastAppliedModeId, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase))
            settings.LastAppliedModeId = "custom1";

        for (var index = 0; index < settings.VisibleModeIds.Count; index++)
        {
            if (string.Equals(settings.VisibleModeIds[index], LegacyCustomModeId, StringComparison.OrdinalIgnoreCase))
                settings.VisibleModeIds[index] = "custom1";
        }
    }

    private static List<string> NormalizeVisibleModeIds(IEnumerable<string> modeIds)
    {
        var normalized = modeIds
            .Where(modeId => RequiredModeIds.Contains(modeId, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumVisibleModeCount)
            .Select(modeId => RequiredModeIds.First(requiredModeId =>
                string.Equals(requiredModeId, modeId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return normalized.Count > 0 ? normalized : [.. DefaultVisibleModeIds];
    }

    private static bool IsValid(AppSettings settings) =>
        settings.Version == 1 &&
        Enum.IsDefined(settings.CloseButtonBehavior) &&
        HotkeyValidator.Validate(settings.Hotkeys).IsSuccess &&
        settings.VisibleModeIds.Count is > 0 and <= MaximumVisibleModeCount &&
        settings.VisibleModeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
            settings.VisibleModeIds.Count &&
        settings.VisibleModeIds.All(modeId =>
            RequiredModeIds.Contains(modeId, StringComparer.OrdinalIgnoreCase)) &&
        settings.Modes.Count == RequiredModeIds.Length &&
        settings.Modes.Select(mode => mode.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == RequiredModeIds.Length &&
        RequiredModeIds.All(modeId => settings.Modes.Any(mode =>
            string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase))) &&
        settings.Modes.All(mode =>
            !string.IsNullOrWhiteSpace(mode.Id) &&
            !string.IsNullOrWhiteSpace(mode.Name) &&
            mode.DisplayTimeoutAc <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.DisplayTimeoutBattery <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.SleepTimeoutAc <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.SleepTimeoutBattery <= PowerSettingsService.MaximumTimeoutSeconds &&
            Enum.IsDefined(mode.MicrophoneMute));
}
