using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class SettingsService
{
    public const int CurrentSchemaVersion = 2;
    public const int MaximumVisibleModeCount = 5;

    private const string LegacyCustomModeId = "custom";
    private static readonly string[] BuiltInModeIds =
        ["game", "work", "normal", "custom1", "custom2", "custom3", "custom4", "custom5", "custom6"];
    private static readonly string[] DefaultVisibleModeIds =
        ["game", "work", "normal", "custom1", "custom2"];

    public static IReadOnlyList<string> SupportedModeIds => BuiltInModeIds;
    public static bool IsBuiltInModeId(string? modeId) =>
        modeId is not null && BuiltInModeIds.Contains(modeId, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public SettingsService(string? settingsDirectory = null)
    {
        _paths = new AppPaths(settingsDirectory);
    }

    public string SettingsDirectory => _paths.RootDirectory;
    public string SettingsPath => _paths.SettingsPath;

    public async Task<OperationResult<AppSettings>> LoadAsync()
    {
        try
        {
            if (!File.Exists(_paths.SettingsPath))
                return OperationResult<AppSettings>.Success(CreateDefaults());

            await using var stream = new FileStream(
                _paths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
            if (settings is null)
                return OperationResult<AppSettings>.Failure("保存済みのモード設定を読み込めませんでした。");

            if (settings.SchemaVersion > CurrentSchemaVersion)
                return OperationResult<AppSettings>.Failure("設定ファイルはこのアプリより新しい形式です。上書きしません。");
            if (settings.SchemaVersion < CurrentSchemaVersion)
            {
                var backup = BackupFile(_paths.SettingsPath, "settings-before-migration");
                if (!backup.IsSuccess)
                    return OperationResult<AppSettings>.Failure("移行前バックアップを作成できないため設定を読み込みません。", backup.TechnicalDetails);
            }

            Normalize(settings);
            if (!IsValid(settings, out var validationMessage))
                return OperationResult<AppSettings>.Failure(validationMessage);
            return OperationResult<AppSettings>.Success(settings);
        }
        catch (JsonException ex)
        {
            var quarantine = QuarantineCorruptedSettings();
            return OperationResult<AppSettings>.Failure(
                quarantine.IsSuccess
                    ? $"設定ファイルが破損していたため別名で退避しました。{quarantine.UserMessage}"
                    : "設定ファイルが破損しています。上書きせず終了します。",
                ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<AppSettings>.Failure("保存済みのモード設定を読み込めませんでした。", ex.ToString());
        }
    }

    public async Task<OperationResult> SaveAsync(AppSettings settings)
    {
        Normalize(settings);
        if (!IsValid(settings, out var validationMessage))
            return OperationResult.Failure(validationMessage);

        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            return await SaveJsonAtomicAsync(_paths.SettingsPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("モード設定を保存できませんでした。", ex.ToString());
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<OperationResult> ExportProfilesAsync(
        string destinationPath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Normalize(settings);
            if (!IsValid(settings, out var validationMessage))
                return OperationResult.Failure(validationMessage);
            var document = new ProfilesDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                ExportedUtc = DateTimeOffset.UtcNow,
                Modes = settings.Modes.Select(mode => mode.Copy()).ToList(),
                Hotkeys = settings.Hotkeys.Select(hotkey => hotkey.Copy()).ToList(),
                RestoreHotkey = settings.RestoreHotkey.Copy()
            };
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            return OperationResult.Success("モード設定を書き出しました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Failure("モード設定を書き出せませんでした。", ex.ToString());
        }
    }

    public async Task<OperationResult<AppSettings>> ImportProfilesAsync(
        string sourcePath,
        AppSettings current,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = await JsonSerializer.DeserializeAsync<ProfilesDocument>(stream, JsonOptions, cancellationToken);
            if (document is null || document.SchemaVersion is < 1 or > CurrentSchemaVersion || document.Modes.Count == 0)
                return OperationResult<AppSettings>.Failure("読み込むファイルの形式が正しくありません。");

            var imported = Clone(current);
            imported.Modes = document.Modes.Select(mode => mode.Copy()).ToList();
            imported.Hotkeys = document.Hotkeys.Select(hotkey => hotkey.Copy()).ToList();
            imported.RestoreHotkey = document.RestoreHotkey?.Copy() ?? new ModeHotkey { ModeId = "restore" };
            imported.VisibleModeIds = imported.Modes.Where(mode => mode.IsEnabled)
                .Take(MaximumVisibleModeCount).Select(mode => mode.Id).ToList();
            imported.SchemaVersion = CurrentSchemaVersion;
            Normalize(imported);
            if (!IsValid(imported, out var validationMessage))
                return OperationResult<AppSettings>.Failure(validationMessage);

            if (File.Exists(_paths.SettingsPath))
            {
                var backup = BackupFile(_paths.SettingsPath, "settings-before-import");
                if (!backup.IsSuccess)
                    return OperationResult<AppSettings>.Failure("読み込み前のバックアップを作成できませんでした。", backup.TechnicalDetails);
            }
            return OperationResult<AppSettings>.Success(imported, "モード設定を読み込みました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult<AppSettings>.Failure("モード設定を読み込めませんでした。", ex.ToString());
        }
    }

    public static PcMode CreateUserMode(string? name = null) => new()
    {
        Id = $"user-{Guid.NewGuid():N}",
        Name = name ?? LocalizationService.Get("Mode.NewName"),
        Icon = "●",
        PowerPlanId = PowerSettingsService.BalancedSchemeId,
        DisplayTimeoutAc = 15 * 60,
        DisplayTimeoutBattery = 15 * 60,
        SleepTimeoutAc = 60 * 60,
        SleepTimeoutBattery = 60 * 60,
        Power = new PowerConfiguration
        {
            ChangePowerPlan = true,
            PowerPlanId = PowerSettingsService.BalancedSchemeId,
            DisplayTimeoutAcSeconds = 15 * 60,
            DisplayTimeoutDcSeconds = 15 * 60,
            SleepTimeoutAcSeconds = 60 * 60,
            SleepTimeoutDcSeconds = 60 * 60
        }
    };

    public static AppSettings CreateDefaults() => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Modes = CreateDefaultModes(),
        Hotkeys = CreateDefaultHotkeys(),
        VisibleModeIds = [.. DefaultVisibleModeIds],
        RestoreHotkey = new ModeHotkey { ModeId = "restore" }
    };

    public static AppSettings Clone(AppSettings source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Version = source.Version,
        Modes = source.Modes.Select(mode => mode.Copy()).ToList(),
        LastAppliedModeId = source.LastAppliedModeId,
        CloseButtonBehavior = source.CloseButtonBehavior,
        ShowTrayNotification = source.ShowTrayNotification,
        StartWithWindows = source.StartWithWindows,
        ShowMicrophoneControls = source.ShowMicrophoneControls,
        CheckForUpdatesAutomatically = source.CheckForUpdatesAutomatically,
        LastUpdateCheckUtc = source.LastUpdateCheckUtc,
        DismissedUpdateVersion = source.DismissedUpdateVersion,
        NotifiedUpdateVersion = source.NotifiedUpdateVersion,
        Language = source.Language,
        Hotkeys = source.Hotkeys.Select(hotkey => hotkey.Copy()).ToList(),
        VisibleModeIds = [.. source.VisibleModeIds],
        RestoreHotkey = source.RestoreHotkey.Copy()
    };

    private static List<PcMode> CreateDefaultModes() =>
    [
        CreateOptimizedMode(
            "game", "GAME", ModeIconKeys.IiController,
            0, 0, 0, 0,
            PowerSettingsService.BalancedSchemeId,
            WindowsPowerMode.BestPerformance,
            WindowsPowerMode.BestPerformance,
            SleepPreventionMode.SystemAndDisplay),
        CreateOptimizedMode(
            "work", "WORK", "💼",
            10 * 60, 5 * 60, 30 * 60, 15 * 60,
            PowerSettingsService.BalancedSchemeId,
            WindowsPowerMode.Balanced,
            WindowsPowerMode.BestEfficiency),
        CreateOptimizedMode(
            "normal", "NORMAL", "🖥",
            5 * 60, 3 * 60, 15 * 60, 10 * 60,
            PowerSettingsService.BalancedSchemeId,
            WindowsPowerMode.Balanced,
            WindowsPowerMode.BestEfficiency),
        CreateMode("custom1", "CUSTOM1", "\U0001F42D\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId),
        CreateMode("custom2", "CUSTOM2", "\U0001F42E\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId),
        CreateMode("custom3", "CUSTOM3", "\U0001F42F\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId),
        CreateMode("custom4", "CUSTOM4", "\U0001F430\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId),
        CreateMode("custom5", "CUSTOM5", "\U0001F432\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId),
        CreateMode("custom6", "CUSTOM6", "\U0001F40D\uFE0E", 15 * 60, 60 * 60, PowerSettingsService.BalancedSchemeId)
    ];

    private static PcMode CreateMode(
        string id, string name, string icon, uint display, uint sleep, Guid plan) => new()
    {
        Id = id,
        Name = name,
        Icon = icon,
        DisplayTimeoutAc = display,
        DisplayTimeoutBattery = display,
        SleepTimeoutAc = sleep,
        SleepTimeoutBattery = sleep,
        PowerPlanId = plan,
        Power = new PowerConfiguration
        {
            ChangePowerPlan = true,
            PowerPlanId = plan,
            DisplayTimeoutAcSeconds = display,
            DisplayTimeoutDcSeconds = display,
            SleepTimeoutAcSeconds = sleep,
            SleepTimeoutDcSeconds = sleep
        }
    };

    private static PcMode CreateOptimizedMode(
        string id,
        string name,
        string icon,
        uint displayAc,
        uint displayDc,
        uint sleepAc,
        uint sleepDc,
        Guid plan,
        WindowsPowerMode acPowerMode,
        WindowsPowerMode dcPowerMode,
        SleepPreventionMode sleepPrevention = SleepPreventionMode.None) => new()
    {
        Id = id,
        Name = name,
        Icon = icon,
        DisplayTimeoutAc = displayAc,
        DisplayTimeoutBattery = displayDc,
        SleepTimeoutAc = sleepAc,
        SleepTimeoutBattery = sleepDc,
        PowerPlanId = plan,
        Power = new PowerConfiguration
        {
            ChangePowerPlan = true,
            PowerPlanId = plan,
            AcPowerMode = acPowerMode,
            DcPowerMode = dcPowerMode,
            DisplayTimeoutAcSeconds = displayAc,
            DisplayTimeoutDcSeconds = displayDc,
            SleepTimeoutAcSeconds = sleepAc,
            SleepTimeoutDcSeconds = sleepDc,
            SleepPrevention = sleepPrevention
        }
    };

    public static List<ModeHotkey> CreateDefaultHotkeys() =>
        BuiltInModeIds.Select(id => new ModeHotkey { ModeId = id }).ToList();

    private static void Normalize(AppSettings settings)
    {
        var isLegacy = settings.SchemaVersion < CurrentSchemaVersion;
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Modes ??= [];
        settings.Hotkeys ??= [];
        settings.VisibleModeIds ??= [];
        settings.RestoreHotkey ??= new ModeHotkey { ModeId = "restore" };
        settings.RestoreHotkey.ModeId = "restore";
        settings.Language = LocalizationService.Normalize(settings.Language);
        MigrateLegacyCustomMode(settings);

        if (settings.Modes.Count == 0)
            settings.Modes = CreateDefaultModes();
        else if (isLegacy)
        {
            foreach (var defaultMode in CreateDefaultModes())
            {
                if (!settings.Modes.Any(mode => string.Equals(mode.Id, defaultMode.Id, StringComparison.OrdinalIgnoreCase)))
                    settings.Modes.Add(defaultMode);
            }
            settings.Modes = BuiltInModeIds.Select(id => settings.Modes.First(mode =>
                string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        foreach (var mode in settings.Modes)
        {
            mode.Id = string.IsNullOrWhiteSpace(mode.Id) ? $"user-{Guid.NewGuid():N}" : mode.Id.Trim();
            mode.Name = mode.Name?.Trim() ?? "";
            mode.Icon = NormalizeModeIcon(mode.Icon);
            mode.Power ??= new PowerConfiguration();
            mode.Display ??= new DisplayConfiguration();
            mode.Audio ??= new AudioConfiguration();
            mode.Audio.Output ??= new AudioEndpointConfiguration();
            mode.Audio.Microphone ??= new AudioEndpointConfiguration();
            mode.LaunchItems ??= [];
            mode.CloseProcessRules ??= [];
            mode.MonitorRules ??= [];
            mode.WindowPlacements ??= [];

            if ((isLegacy || mode.Power.ChangePowerPlan) && mode.PowerPlanId != Guid.Empty)
                mode.Power.PowerPlanId = mode.PowerPlanId;
            if (isLegacy || (mode.Power.DisplayTimeoutAcSeconds is null &&
                mode.Power.DisplayTimeoutDcSeconds is null &&
                mode.Power.SleepTimeoutAcSeconds is null &&
                mode.Power.SleepTimeoutDcSeconds is null))
            {
                mode.Power.DisplayTimeoutAcSeconds = mode.DisplayTimeoutAc;
                mode.Power.DisplayTimeoutDcSeconds = mode.DisplayTimeoutBattery;
                mode.Power.SleepTimeoutAcSeconds = mode.SleepTimeoutAc;
                mode.Power.SleepTimeoutDcSeconds = mode.SleepTimeoutBattery;
            }
            else
            {
                if (mode.Power.DisplayTimeoutAcSeconds is not null) mode.Power.DisplayTimeoutAcSeconds = mode.DisplayTimeoutAc;
                if (mode.Power.DisplayTimeoutDcSeconds is not null) mode.Power.DisplayTimeoutDcSeconds = mode.DisplayTimeoutBattery;
                if (mode.Power.SleepTimeoutAcSeconds is not null) mode.Power.SleepTimeoutAcSeconds = mode.SleepTimeoutAc;
                if (mode.Power.SleepTimeoutDcSeconds is not null) mode.Power.SleepTimeoutDcSeconds = mode.SleepTimeoutBattery;
            }
            if (mode.Audio.Microphone.Mute == AudioMuteSetting.NoChange &&
                mode.MicrophoneMute != MicrophoneMuteSetting.NoChange)
            {
                mode.Audio.Microphone.Mute = mode.MicrophoneMute == MicrophoneMuteSetting.Mute
                    ? AudioMuteSetting.Mute : AudioMuteSetting.Unmute;
            }

            mode.PowerPlanId = mode.Power.PowerPlanId;
            mode.DisplayTimeoutAc = mode.Power.DisplayTimeoutAcSeconds ?? mode.DisplayTimeoutAc;
            mode.DisplayTimeoutBattery = mode.Power.DisplayTimeoutDcSeconds ?? mode.DisplayTimeoutBattery;
            mode.SleepTimeoutAc = mode.Power.SleepTimeoutAcSeconds ?? mode.SleepTimeoutAc;
            mode.SleepTimeoutBattery = mode.Power.SleepTimeoutDcSeconds ?? mode.SleepTimeoutBattery;
            mode.MicrophoneMute = mode.Audio.Microphone.Mute switch
            {
                AudioMuteSetting.Mute => MicrophoneMuteSetting.Mute,
                AudioMuteSetting.Unmute => MicrophoneMuteSetting.Unmute,
                _ => MicrophoneMuteSetting.NoChange
            };
        }

        var modeIds = settings.Modes.Select(mode => mode.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.Hotkeys = settings.Hotkeys
            .Where(hotkey => modeIds.Contains(hotkey.ModeId))
            .GroupBy(hotkey => hotkey.ModeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        foreach (var mode in settings.Modes)
        {
            if (!settings.Hotkeys.Any(hotkey => string.Equals(hotkey.ModeId, mode.Id, StringComparison.OrdinalIgnoreCase)))
                settings.Hotkeys.Add(new ModeHotkey { ModeId = mode.Id });
        }
        settings.VisibleModeIds = NormalizeVisibleModeIds(settings.VisibleModeIds, settings.Modes);
        if (settings.LastAppliedModeId is not null && !modeIds.Contains(settings.LastAppliedModeId))
            settings.LastAppliedModeId = null;
    }

    private static List<string> NormalizeVisibleModeIds(
        IEnumerable<string> ids,
        IReadOnlyCollection<PcMode> modes)
    {
        var enabled = modes.Where(mode => mode.IsEnabled).Select(mode => mode.Id).ToList();
        var result = ids.Where(id => enabled.Contains(id, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumVisibleModeCount)
            .Select(id => enabled.First(value => string.Equals(value, id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return result.Count > 0 ? result : enabled.Take(MaximumVisibleModeCount).ToList();
    }

    private static bool IsValid(AppSettings settings, out string message)
    {
        message = "モード設定が正しくないため保存しませんでした。";
        if (settings.SchemaVersion != CurrentSchemaVersion || !Enum.IsDefined(settings.CloseButtonBehavior)) return false;
        if (settings.Modes.Count == 0 || settings.Modes.Any(mode => string.IsNullOrWhiteSpace(mode.Id) || string.IsNullOrWhiteSpace(mode.Name))) return false;
        if (settings.Modes.Select(mode => mode.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Modes.Count) return false;
        if (settings.VisibleModeIds.Count is < 1 or > MaximumVisibleModeCount ||
            settings.VisibleModeIds.Any(id => !settings.Modes.Any(mode => mode.IsEnabled && string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase)))) return false;
        if (settings.Modes.Any(mode =>
            new[] { mode.Power.DisplayTimeoutAcSeconds, mode.Power.DisplayTimeoutDcSeconds, mode.Power.SleepTimeoutAcSeconds, mode.Power.SleepTimeoutDcSeconds }
                .Any(value => value > PowerSettingsService.MaximumTimeoutSeconds) ||
            !ValidAudio(mode.Audio.Output) || !ValidAudio(mode.Audio.Microphone))) return false;
        if (!HotkeyValidator.Validate(settings.Hotkeys.Append(settings.RestoreHotkey).ToList()).IsSuccess) return false;
        return true;
    }

    private static bool ValidAudio(AudioEndpointConfiguration value) =>
        value.VolumePercent is null or >= 0 and <= 100 && Enum.IsDefined(value.Mute);

    private static string NormalizeModeIcon(string? icon)
    {
        var value = string.IsNullOrWhiteSpace(icon) ? "●" : icon;
        return ModeIconKeys.IsIiController(value) ||
               value.Contains("\U0001F3AE", StringComparison.Ordinal)
            ? ModeIconKeys.IiController
            : value;
    }

    private async Task<OperationResult> SaveJsonAtomicAsync<T>(string path, T value)
    {
        var temporaryPath = Path.Combine(_paths.RootDirectory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
                await stream.FlushAsync();
            }
            File.Move(temporaryPath, path, true);
            return OperationResult.Success("モード設定を保存しました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Failure("モード設定を保存できませんでした。", ex.ToString());
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private OperationResult BackupFile(string sourcePath, string prefix)
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupDirectory);
            var destination = Path.Combine(_paths.BackupDirectory, $"{prefix}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
            File.Copy(sourcePath, destination, false);
            return OperationResult.Success(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("バックアップを作成できませんでした。", ex.ToString());
        }
    }

    private OperationResult QuarantineCorruptedSettings()
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupDirectory);
            var destination = Path.Combine(_paths.BackupDirectory, $"corrupt-settings-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
            File.Move(_paths.SettingsPath, destination, false);
            return OperationResult.Success(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure("破損した設定を退避できませんでした。", ex.ToString());
        }
    }

    private static void MigrateLegacyCustomMode(AppSettings settings)
    {
        var legacy = settings.Modes.FirstOrDefault(mode => string.Equals(mode.Id, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase));
        if (legacy is not null)
        {
            if (settings.Modes.Any(mode => string.Equals(mode.Id, "custom1", StringComparison.OrdinalIgnoreCase))) settings.Modes.Remove(legacy);
            else legacy.Id = "custom1";
        }
        foreach (var hotkey in settings.Hotkeys.Where(value => string.Equals(value.ModeId, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase))) hotkey.ModeId = "custom1";
        if (string.Equals(settings.LastAppliedModeId, LegacyCustomModeId, StringComparison.OrdinalIgnoreCase)) settings.LastAppliedModeId = "custom1";
        for (var index = 0; index < settings.VisibleModeIds.Count; index++)
            if (string.Equals(settings.VisibleModeIds[index], LegacyCustomModeId, StringComparison.OrdinalIgnoreCase)) settings.VisibleModeIds[index] = "custom1";
    }

    private sealed class ProfilesDocument
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset ExportedUtc { get; set; }
        public List<PcMode> Modes { get; set; } = [];
        public List<ModeHotkey> Hotkeys { get; set; } = [];
        public ModeHotkey? RestoreHotkey { get; set; }
    }
}
