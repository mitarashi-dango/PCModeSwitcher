using System.IO;
using System.Text.Json;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class SettingsService
{
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
        Modes =
        [
            new PcMode
            {
                Id = "game", Name = "GAME", Icon = "🎮",
                DisplayTimeoutAc = 0, DisplayTimeoutBattery = 0,
                SleepTimeoutAc = 0, SleepTimeoutBattery = 0,
                PowerPlanId = PowerSettingsService.HighPerformanceSchemeId
            },
            new PcMode
            {
                Id = "work", Name = "WORK", Icon = "💼",
                DisplayTimeoutAc = 10 * 60, DisplayTimeoutBattery = 10 * 60,
                SleepTimeoutAc = 30 * 60, SleepTimeoutBattery = 30 * 60,
                PowerPlanId = PowerSettingsService.BalancedSchemeId
            },
            new PcMode
            {
                Id = "normal", Name = "NORMAL", Icon = "🖥",
                DisplayTimeoutAc = 5 * 60, DisplayTimeoutBattery = 5 * 60,
                SleepTimeoutAc = 15 * 60, SleepTimeoutBattery = 15 * 60,
                PowerPlanId = PowerSettingsService.BalancedSchemeId
            }
        ],
        Hotkeys = CreateDefaultHotkeys()
    };

    public static List<ModeHotkey> CreateDefaultHotkeys() =>
    [
        new() { ModeId = "game" },
        new() { ModeId = "work" },
        new() { ModeId = "normal" }
    ];

    private static void Normalize(AppSettings settings)
    {
        settings.Modes ??= [];
        settings.Hotkeys ??= [];
        foreach (var defaultHotkey in CreateDefaultHotkeys())
        {
            if (!settings.Hotkeys.Any(hotkey =>
                string.Equals(hotkey.ModeId, defaultHotkey.ModeId, StringComparison.OrdinalIgnoreCase)))
            {
                settings.Hotkeys.Add(defaultHotkey);
            }
        }
    }

    private static bool IsValid(AppSettings settings) =>
        settings.Version == 1 &&
        Enum.IsDefined(settings.CloseButtonBehavior) &&
        HotkeyValidator.Validate(settings.Hotkeys).IsSuccess &&
        settings.Modes.Count == 3 &&
        settings.Modes.All(mode =>
            !string.IsNullOrWhiteSpace(mode.Id) &&
            !string.IsNullOrWhiteSpace(mode.Name) &&
            mode.DisplayTimeoutAc <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.DisplayTimeoutBattery <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.SleepTimeoutAc <= PowerSettingsService.MaximumTimeoutSeconds &&
            mode.SleepTimeoutBattery <= PowerSettingsService.MaximumTimeoutSeconds);
}
