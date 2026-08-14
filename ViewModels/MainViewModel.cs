using System.Collections.ObjectModel;
using System.Windows;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.Views;

namespace PCModeSwitcher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string CustomModeId = "custom1";
    public const string UnregisteredModeId = "unregistered";

    private static readonly (string Id, string Icon)[] AdditionalCustomModeIdentities =
    [
        ("custom7", "\U0001F434\uFE0E"),
        ("custom8", "\U0001F411\uFE0E"),
        ("custom9", "\U0001F412\uFE0E"),
        ("custom10", "\U0001F413\uFE0E"),
        ("custom11", "\U0001F415\uFE0E"),
        ("custom12", "\U0001F417\uFE0E")
    ];
    private readonly SettingsService _settingsService;
    private readonly PowerSettingsService _powerService;
    private readonly IMicrophoneMuteService _microphoneMuteService;
    private readonly IStartupService _startupService;
    private readonly IGlobalHotkeyService _globalHotkeyService;
    private readonly ModeEngine? _modeEngine;
    private AppSettings _settings = SettingsService.CreateDefaults();
    private bool _isBusy;
    private string? _currentModeId;
    private string _currentModeName = LocalizationService.Get("Status.Checking");
    private string _currentModeIcon = "…";
    private bool? _isMicrophoneOn;
    private string _statusMessage = LocalizationService.Get("Status.Initial");

    public ObservableCollection<ModeCardViewModel> Modes { get; } = [];
    public ObservableCollection<ModeCardViewModel> VisibleModes { get; } = [];
    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            OnPropertyChanged(nameof(IsInteractionEnabled));
            ApplyModeCommand.RaiseCanExecuteChanged();
            EditModeCommand.RaiseCanExecuteChanged();
            ToggleMicrophoneCommand.RaiseCanExecuteChanged();
            RestoreModeCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsInteractionEnabled => !IsBusy;
    public string? CurrentModeId
    {
        get => _currentModeId;
        private set
        {
            if (!SetProperty(ref _currentModeId, value))
                return;
            OnPropertyChanged(nameof(CurrentModeHasCustomIcon));
            OnPropertyChanged(nameof(CurrentModeCustomIconSource));
        }
    }
    public string CurrentModeName { get => _currentModeName; private set => SetProperty(ref _currentModeName, value); }
    public string CurrentModeIcon { get => _currentModeIcon; private set => SetProperty(ref _currentModeIcon, value); }
    public bool CurrentModeHasCustomIcon => ModeIconAssets.HasCustomIcon(CurrentModeId);
    public string? CurrentModeCustomIconSource => ModeIconAssets.GetCustomIconSource(CurrentModeId);
    public bool? IsMicrophoneOn
    {
        get => _isMicrophoneOn;
        private set
        {
            if (!SetProperty(ref _isMicrophoneOn, value))
                return;
            OnPropertyChanged(nameof(MicrophoneButtonText));
            OnPropertyChanged(nameof(MicrophoneButtonToolTip));
        }
    }
    public string MicrophoneButtonText => IsMicrophoneOn switch
    {
        true => LocalizationService.Get("Status.MicrophoneOn"),
        false => LocalizationService.Get("Status.MicrophoneOff"),
        null => LocalizationService.Get("Status.MicrophoneUnknown")
    };
    public string MicrophoneButtonToolTip => IsMicrophoneOn switch
    {
        true => LocalizationService.Get("Status.MicrophoneOnTip"),
        false => LocalizationService.Get("Status.MicrophoneOffTip"),
        null => LocalizationService.Get("Status.MicrophoneUnknownTip")
    };
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public CloseButtonBehavior CloseButtonBehavior => _settings.CloseButtonBehavior;
    public bool ShowTrayNotification => _settings.ShowTrayNotification;
    public bool StartWithWindows => _settings.StartWithWindows;
    public bool ShowMicrophoneControls => _settings.ShowMicrophoneControls;
    public string Language => _settings.Language;
    public string AppVersion { get; } = GetAppVersion();
    public IReadOnlyList<ModeHotkey> Hotkeys => _settings.Hotkeys.Select(hotkey => hotkey.Copy()).ToList();
    public ModeHotkey RestoreHotkey => _settings.RestoreHotkey.Copy();
    public IReadOnlyList<string> VisibleModeIds => [.. _settings.VisibleModeIds];
    public IReadOnlyList<PcMode> AllProfiles => _settings.Modes.Select(mode => mode.Copy()).ToList();
    public AsyncRelayCommand ApplyModeCommand { get; }
    public AsyncRelayCommand EditModeCommand { get; }
    public AsyncRelayCommand ToggleMicrophoneCommand { get; }
    public AsyncRelayCommand RestoreModeCommand { get; }
    public bool HasActiveSession => _modeEngine?.HasActiveSession == true;

    public MainViewModel(
        SettingsService settingsService,
        PowerSettingsService powerService,
        IMicrophoneMuteService microphoneMuteService,
        IStartupService startupService,
        IGlobalHotkeyService globalHotkeyService,
        ModeEngine? modeEngine = null)
    {
        _settingsService = settingsService;
        _powerService = powerService;
        _microphoneMuteService = microphoneMuteService;
        _startupService = startupService;
        _globalHotkeyService = globalHotkeyService;
        _modeEngine = modeEngine;
        ApplyModeCommand = new AsyncRelayCommand(ApplyModeAsync, _ => !IsBusy);
        EditModeCommand = new AsyncRelayCommand(EditModeAsync, _ => !IsBusy);
        ToggleMicrophoneCommand = new AsyncRelayCommand(
            _ => ToggleMicrophoneAsync(),
            _ => !IsBusy && ShowMicrophoneControls);
        RestoreModeCommand = new AsyncRelayCommand(
            _ => RestoreModeAsync(),
            _ => !IsBusy && HasActiveSession);
        if (_modeEngine is not null)
            _modeEngine.SessionChanged += OnModeEngineSessionChanged;
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var settingsResult = await _settingsService.LoadAsync();
            if (settingsResult.IsSuccess && settingsResult.Value is not null)
            {
                _settings = settingsResult.Value;
            }
            else
            {
                _settings = SettingsService.CreateDefaults();
            }
            LocalizationService.SetLanguage(_settings.Language);
            CurrentModeName = LocalizationService.Get("Status.Checking");
            StatusMessage = settingsResult.IsSuccess
                ? LocalizationService.Get("Status.Initial")
                : $"{LocalizationService.Translate(settingsResult.UserMessage)} {LocalizationService.Translate("初期設定で開始しました。")}";
            OnPropertyChanged(nameof(CloseButtonBehavior));
            OnPropertyChanged(nameof(ShowTrayNotification));
            OnPropertyChanged(nameof(StartWithWindows));
            OnPropertyChanged(nameof(ShowMicrophoneControls));
            OnPropertyChanged(nameof(Hotkeys));
            OnPropertyChanged(nameof(VisibleModeIds));

            var startupResult = _startupService.SetEnabled(_settings.StartWithWindows);
            if (!startupResult.IsSuccess)
            {
                AppendStatusWarning(startupResult.UserMessage);
            }

            var hotkeyResult = _globalHotkeyService.ReplaceBindings(
                GetRegisteredHotkeys(_settings));
            if (!hotkeyResult.IsSuccess)
            {
                AppendStatusWarning(hotkeyResult.UserMessage);
            }

            var plansResult = await _powerService.GetAvailablePlansAsync();
            if (plansResult.IsSuccess && plansResult.Value is not null)
            {
                foreach (var plan in plansResult.Value)
                    PowerPlans.Add(plan);
                RepairUnavailableDefaultPlans();
            }
            else
            {
                StatusMessage = plansResult.UserMessage;
            }

            var hasBattery = _powerService.HasBattery;
            foreach (var mode in _settings.Modes)
            {
                if (!mode.IsEnabled) continue;
                Modes.Add(new ModeCardViewModel(
                    mode,
                    GetPowerPlanName,
                    hasBattery,
                    _settings.ShowMicrophoneControls));
            }
            RebuildVisibleModes();
            var detection = await RefreshCurrentModeCoreAsync();
            if (!detection.IsSuccess)
                AppendStatusWarning(detection.UserMessage);
            if (_settings.ShowMicrophoneControls)
                RefreshMicrophoneStateCore();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<OperationResult> SetAppPreferencesAsync(
        CloseButtonBehavior behavior,
        bool showTrayNotification,
        bool startWithWindows,
        IReadOnlyCollection<ModeHotkey> hotkeys,
        IReadOnlyCollection<string>? visibleModeIds = null,
        bool? showMicrophoneControls = null,
        ModeHotkey? restoreHotkey = null,
        IReadOnlyCollection<string>? enabledModeIds = null,
        IReadOnlyCollection<string>? deletedModeIds = null,
        string? language = null)
    {
        if (!Enum.IsDefined(behavior))
            return OperationResult.Failure("閉じるボタンの動作が正しくありません。");
        if (language is not null && !LocalizationService.IsSupported(language))
            return OperationResult.Failure("表示言語が正しくありません。");
        var newLanguage = language is null ? _settings.Language : LocalizationService.Normalize(language);

        var deletedModeIdSet = deletedModeIds?.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (deletedModeIdSet.Any(SettingsService.IsBuiltInModeId))
            return OperationResult.Failure("標準モードは完全削除できません。非表示または無効にしてください。");
        if (deletedModeIdSet.Any(id => !_settings.Modes.Any(mode =>
                string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase))))
            return OperationResult.Failure("削除するモードが見つかりません。");

        var newHotkeys = hotkeys
            .Where(hotkey => !deletedModeIdSet.Contains(hotkey.ModeId))
            .Select(hotkey => hotkey.Copy()).ToList();
        var newRestoreHotkey = restoreHotkey?.Copy() ?? _settings.RestoreHotkey.Copy();
        newRestoreHotkey.ModeId = "restore";
        var validation = HotkeyValidator.Validate(newHotkeys.Append(newRestoreHotkey).ToList());
        if (!validation.IsSuccess)
        {
            return validation;
        }

        var newVisibleModeIds = visibleModeIds is null
            ? [.. _settings.VisibleModeIds]
            : visibleModeIds.ToList();
        var newEnabledModeIds = enabledModeIds?.ToList()
            ?? _settings.Modes.Where(mode => mode.IsEnabled).Select(mode => mode.Id).ToList();
        if (newEnabledModeIds.Count == 0 || newEnabledModeIds.Any(id =>
            deletedModeIdSet.Contains(id) ||
            !_settings.Modes.Any(mode => string.Equals(mode.Id, id, StringComparison.OrdinalIgnoreCase))))
            return OperationResult.Failure("有効なモードを1個以上選んでください。");
        if (newVisibleModeIds.Count is < 1 or > SettingsService.MaximumVisibleModeCount ||
            newVisibleModeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != newVisibleModeIds.Count ||
            newVisibleModeIds.Any(modeId => !newEnabledModeIds.Contains(modeId, StringComparer.OrdinalIgnoreCase)))
        {
            return OperationResult.Failure("アプリ画面に表示するモードは1〜5個で選んでください。");
        }

        newVisibleModeIds = newVisibleModeIds.Select(modeId => _settings.Modes.First(mode =>
            string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase)).Id).ToList();

        var previousBehavior = _settings.CloseButtonBehavior;
        var previousShowTrayNotification = _settings.ShowTrayNotification;
        var previousStartWithWindows = _settings.StartWithWindows;
        var previousShowMicrophoneControls = _settings.ShowMicrophoneControls;
        var previousLanguage = _settings.Language;
        var previousModes = _settings.Modes.ToList();
        var previousLastAppliedModeId = _settings.LastAppliedModeId;
        var previousHotkeys = _settings.Hotkeys.Select(hotkey => hotkey.Copy()).ToList();
        var previousRestoreHotkey = _settings.RestoreHotkey.Copy();
        var previousVisibleModeIds = _settings.VisibleModeIds.ToList();
        var previousEnabledModeIds = _settings.Modes.Where(mode => mode.IsEnabled)
            .Select(mode => mode.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var startupResult = _startupService.SetEnabled(startWithWindows);
        if (!startupResult.IsSuccess)
        {
            StatusMessage = startupResult.UserMessage;
            return startupResult;
        }

        var hotkeyResult = _globalHotkeyService.ReplaceBindings(
            newHotkeys.Where(hotkey => newEnabledModeIds.Contains(hotkey.ModeId, StringComparer.OrdinalIgnoreCase))
                .Append(newRestoreHotkey).ToList());
        if (!hotkeyResult.IsSuccess)
        {
            var startupRollback = _startupService.SetEnabled(previousStartWithWindows);
            StatusMessage = startupRollback.IsSuccess
                ? hotkeyResult.UserMessage
                : $"{hotkeyResult.UserMessage} {startupRollback.UserMessage}";
            return OperationResult.Failure(StatusMessage, hotkeyResult.TechnicalDetails);
        }

        _settings.CloseButtonBehavior = behavior;
        _settings.ShowTrayNotification = showTrayNotification;
        _settings.StartWithWindows = startWithWindows;
        _settings.ShowMicrophoneControls =
            showMicrophoneControls ?? _settings.ShowMicrophoneControls;
        _settings.Language = newLanguage;
        _settings.Hotkeys = newHotkeys;
        _settings.RestoreHotkey = newRestoreHotkey;
        _settings.VisibleModeIds = newVisibleModeIds;
        _settings.Modes.RemoveAll(mode => deletedModeIdSet.Contains(mode.Id));
        if (_settings.LastAppliedModeId is not null && deletedModeIdSet.Contains(_settings.LastAppliedModeId))
            _settings.LastAppliedModeId = null;
        foreach (var mode in _settings.Modes)
            mode.IsEnabled = newEnabledModeIds.Contains(mode.Id, StringComparer.OrdinalIgnoreCase);
        RebuildVisibleModes();
        RefreshMicrophonePreference();
        NotifyAppPreferencesChanged();

        var result = await _settingsService.SaveAsync(_settings);
        if (!result.IsSuccess)
        {
            _settings.CloseButtonBehavior = previousBehavior;
            _settings.ShowTrayNotification = previousShowTrayNotification;
            _settings.StartWithWindows = previousStartWithWindows;
            _settings.ShowMicrophoneControls = previousShowMicrophoneControls;
            _settings.Language = previousLanguage;
            _settings.Modes = previousModes;
            _settings.LastAppliedModeId = previousLastAppliedModeId;
            _settings.Hotkeys = previousHotkeys;
            _settings.RestoreHotkey = previousRestoreHotkey;
            _settings.VisibleModeIds = previousVisibleModeIds;
            foreach (var mode in _settings.Modes)
                mode.IsEnabled = previousEnabledModeIds.Contains(mode.Id);
            RebuildVisibleModes();
            RefreshMicrophonePreference();
            NotifyAppPreferencesChanged();

            var startupRollback = _startupService.SetEnabled(previousStartWithWindows);
            var hotkeyRollback = _globalHotkeyService.ReplaceBindings(
                previousHotkeys.Where(hotkey => previousEnabledModeIds.Contains(hotkey.ModeId))
                    .Append(previousRestoreHotkey).ToList());
            var rollbackMessages = new[] { startupRollback, hotkeyRollback }
                .Where(rollback => !rollback.IsSuccess)
                .Select(rollback => rollback.UserMessage)
                .ToList();
            StatusMessage = rollbackMessages.Count == 0
                ? result.UserMessage
                : $"{result.UserMessage} {string.Join(" ", rollbackMessages)}";
            return OperationResult.Failure(StatusMessage, result.TechnicalDetails);
        }

        var deletedCurrentMode = CurrentModeId is not null && deletedModeIdSet.Contains(CurrentModeId);
        LocalizationService.SetLanguage(_settings.Language);
        RefreshLocalizedProperties();
        RebuildModeCards();
        if (deletedCurrentMode)
            await RefreshCurrentModeCoreAsync();
        StatusMessage = deletedModeIdSet.Count == 0
            ? LocalizationService.Translate("アプリ設定を保存しました。")
            : LocalizationService.Format("Status.SettingsSavedDeleted", deletedModeIdSet.Count);
        return OperationResult.Success(StatusMessage);
    }

    public Task<ModeApplyResult?> ApplyModeByIdAsync(string modeId)
    {
        var card = Modes.FirstOrDefault(mode =>
            string.Equals(mode.Mode.Id, modeId, StringComparison.OrdinalIgnoreCase));
        return card is null
            ? Task.FromResult<ModeApplyResult?>(null)
            : ApplyModeCardAsync(card);
    }

    public async Task RefreshCurrentModeAsync()
    {
        if (IsBusy || Modes.Count == 0)
            return;

        IsBusy = true;
        try
        {
            var result = await RefreshCurrentModeCoreAsync();
            if (_settings.ShowMicrophoneControls)
                RefreshMicrophoneStateCore();
            if (!result.IsSuccess)
                StatusMessage = result.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ToggleMicrophoneAsync()
    {
        if (IsBusy || !_settings.ShowMicrophoneControls)
            return Task.CompletedTask;

        return ToggleMicrophoneCoreAsync();
    }

    public void RefreshMicrophoneState()
    {
        if (!IsBusy && _settings.ShowMicrophoneControls)
            RefreshMicrophoneStateCore();
    }

    private Task ToggleMicrophoneCoreAsync()
    {
        IsBusy = true;
        try
        {
            // 表示が古い可能性があるため、クリック時の実際の状態から反転先を決める。
            var current = RefreshMicrophoneStateCore();
            if (!current.IsSuccess)
            {
                StatusMessage = $"⚠ {LocalizationService.Translate("既定のマイクの状態を確認できないため、切り替えませんでした。")}";
                return Task.CompletedTask;
            }

            var requestedSetting = current.Value
                ? MicrophoneMuteSetting.Unmute
                : MicrophoneMuteSetting.Mute;
            var result = _microphoneMuteService.Apply(requestedSetting);
            var refreshed = RefreshMicrophoneStateCore();
            if (!result.IsSuccess)
            {
                StatusMessage = $"⚠ {result.UserMessage}";
            }
            else if (!refreshed.IsSuccess)
            {
                StatusMessage = $"✓ {result.UserMessage} {LocalizationService.Translate("現在の状態は再確認できませんでした。")}";
            }
            else
            {
                StatusMessage = $"✓ {LocalizationService.Format("Status.MicrophoneChanged", refreshed.Value ? "OFF" : "ON")}";
            }

            return Task.CompletedTask;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyModeAsync(object? parameter)
    {
        if (parameter is ModeCardViewModel card)
        {
            await ApplyModeCardAsync(card);
        }
    }

    private async Task<ModeApplyResult?> ApplyModeCardAsync(ModeCardViewModel card)
    {
        if (IsBusy)
            return null;

        if (_modeEngine is not null)
            return await ApplyModeWithEngineAsync(card);

        IsBusy = true;
        StatusMessage = LocalizationService.Format("Status.ApplyingMode", card.Name);
        try
        {
            var powerResult = await _powerService.ApplyModeAsync(card.Mode);
            var steps = powerResult.Steps.ToList();
            if (_settings.ShowMicrophoneControls)
            {
                var microphoneResult = _microphoneMuteService.Apply(card.Mode.MicrophoneMute);
                var currentMicrophone = RefreshMicrophoneStateCore();
                var microphoneDisplayName = GetMicrophoneResultDisplayName(card, currentMicrophone);
                steps.Add(new ApplyStepResult(
                    LocalizationService.Get("Main.Microphone"),
                    microphoneResult.IsSuccess,
                    microphoneResult.UserMessage,
                    microphoneResult.TechnicalDetails,
                    card.Mode.MicrophoneMute == MicrophoneMuteSetting.NoChange,
                    microphoneDisplayName));
            }

            var result = new ModeApplyResult
            {
                Steps = steps
            };
            StatusMessage = result.ToUserMessage(card.Name);

            // マイクなど一部だけ失敗しても、実際に反映された電源設定から表示を更新する。
            var detection = await RefreshCurrentModeCoreAsync(card.Mode.Id);
            if (!detection.IsSuccess)
                AppendStatusWarning(detection.UserMessage);

            if (result.IsSuccess || string.Equals(
                    CurrentModeId, card.Mode.Id, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LastAppliedModeId = card.Mode.Id;
                var save = await _settingsService.SaveAsync(_settings);
                if (!save.IsSuccess)
                    AppendStatusWarning(save.UserMessage);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ModeApplyResult?> ApplyModeWithEngineAsync(ModeCardViewModel card)
    {
        IsBusy = true;
        StatusMessage = LocalizationService.Format("Status.ApplyingMode", card.Name);
        try
        {
            var result = await _modeEngine!.ApplyAsync(card.Mode);
            StatusMessage = result.ToUserMessage(card.Name);
            if (_modeEngine.HasActiveSession)
            {
                SetDetectedMode(card.Mode.Id);
                _settings.LastAppliedModeId = card.Mode.Id;
                var save = await _settingsService.SaveAsync(_settings);
                if (!save.IsSuccess) AppendStatusWarning(save.UserMessage);
            }
            OnPropertyChanged(nameof(HasActiveSession));
            RestoreModeCommand.RaiseCanExecuteChanged();
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<ModeApplyResult?> RestoreModeAsync()
    {
        if (_modeEngine is null || IsBusy)
            return null;
        IsBusy = true;
        StatusMessage = LocalizationService.Translate("モード適用前の状態へ戻しています…");
        try
        {
            var result = await _modeEngine.RestoreAsync();
            var restored = result.Steps.Count(step => step.IsSuccess && !step.IsSkipped);
            var skipped = result.Steps.Count(step => step.IsSkipped);
            var failed = result.Steps.Count(step => !step.IsSuccess);
            StatusMessage = LocalizationService.Format("Status.RestoredCount", restored) +
                (skipped > 0 ? $"{Environment.NewLine}{LocalizationService.Format("Status.SkippedCount", skipped)}" : "") +
                (failed > 0 ? $"{Environment.NewLine}{LocalizationService.Format("Status.RestoreFailedCount", failed)}" : "");
            _settings.LastAppliedModeId = null;
            await _settingsService.SaveAsync(_settings);
            await RefreshCurrentModeCoreAsync();
            OnPropertyChanged(nameof(HasActiveSession));
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<OperationResult<ModeSessionSnapshot?>> GetIncompleteSessionAsync() =>
        _modeEngine is null
            ? Task.FromResult(OperationResult<ModeSessionSnapshot?>.Success(null))
            : _modeEngine.GetIncompleteSessionAsync();

    public OperationResult IgnoreIncompleteSession() =>
        _modeEngine?.IgnoreIncompleteSession() ?? OperationResult.Success();

    public async Task<OperationResult> AddModeAsync(PcMode mode)
    {
        if (_settings.Modes.Any(value => string.Equals(value.Id, mode.Id, StringComparison.OrdinalIgnoreCase)))
            mode.Id = $"user-{Guid.NewGuid():N}";
        _settings.Modes.Add(mode);
        _settings.Hotkeys.Add(new ModeHotkey { ModeId = mode.Id });
        if (mode.IsEnabled && _settings.VisibleModeIds.Count < SettingsService.MaximumVisibleModeCount)
            _settings.VisibleModeIds.Add(mode.Id);
        var save = await _settingsService.SaveAsync(_settings);
        if (save.IsSuccess) RebuildModeCards();
        return save;
    }

    public PcMode CreateNewMode()
    {
        var mode = SettingsService.CreateUserMode();
        AssignNextAdditionalCustomIdentity(mode);
        return mode;
    }

    public async Task<OperationResult> DuplicateModeAsync(string modeId)
    {
        var source = _settings.Modes.FirstOrDefault(mode => string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase));
        if (source is null) return OperationResult.Failure("複製元のモードが見つかりません。");
        var copy = source.Copy();
        copy.Id = $"user-{Guid.NewGuid():N}";
        copy.Name = LocalizationService.Format("Status.ModeCopyName", source.Name);
        AssignNextAdditionalCustomIdentity(copy);
        return await AddModeAsync(copy);
    }

    public async Task<OperationResult> HideModeAsync(string modeId)
    {
        var visible = _settings.VisibleModeIds.FirstOrDefault(id =>
            string.Equals(id, modeId, StringComparison.OrdinalIgnoreCase));
        if (visible is null)
            return OperationResult.Failure("非表示にするモードが画面に見つかりません。");
        if (_settings.VisibleModeIds.Count <= 1)
            return OperationResult.Failure("最後の表示モードは非表示にできません。");

        var previousVisibleModeIds = _settings.VisibleModeIds.ToList();
        _settings.VisibleModeIds.Remove(visible);
        var save = await _settingsService.SaveAsync(_settings);
        if (!save.IsSuccess)
        {
            _settings.VisibleModeIds = previousVisibleModeIds;
            return save;
        }

        RebuildVisibleModes();
        OnPropertyChanged(nameof(VisibleModeIds));
        return OperationResult.Success("モードをアプリ画面から非表示にしました。設定から再表示できます。");
    }
    public async Task<OperationResult> ReorderVisibleModeAsync(
        string modeId,
        string targetModeId,
        bool insertAfter)
    {
        var previousVisibleModeIds = _settings.VisibleModeIds.ToList();
        var sourceIndex = previousVisibleModeIds.FindIndex(id =>
            string.Equals(id, modeId, StringComparison.OrdinalIgnoreCase));
        var targetIndex = previousVisibleModeIds.FindIndex(id =>
            string.Equals(id, targetModeId, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0)
            return OperationResult.Failure("並べ替えるモードが画面に見つかりません。");
        if (sourceIndex == targetIndex)
            return OperationResult.Success();

        var reordered = previousVisibleModeIds.ToList();
        var movingModeId = reordered[sourceIndex];
        reordered.RemoveAt(sourceIndex);
        targetIndex = reordered.FindIndex(id =>
            string.Equals(id, targetModeId, StringComparison.OrdinalIgnoreCase));
        var insertIndex = targetIndex + (insertAfter ? 1 : 0);
        reordered.Insert(insertIndex, movingModeId);
        if (reordered.SequenceEqual(previousVisibleModeIds, StringComparer.OrdinalIgnoreCase))
            return OperationResult.Success();

        _settings.VisibleModeIds = reordered;
        var save = await _settingsService.SaveAsync(_settings);
        if (!save.IsSuccess)
        {
            _settings.VisibleModeIds = previousVisibleModeIds;
            return save;
        }

        RebuildVisibleModes();
        OnPropertyChanged(nameof(VisibleModeIds));
        StatusMessage = LocalizationService.Translate("モードの表示順を変更しました。");
        return OperationResult.Success(StatusMessage);
    }

    public Task<OperationResult> ExportProfilesAsync(string destinationPath) =>
        _settingsService.ExportProfilesAsync(destinationPath, _settings);

    public async Task<OperationResult> ImportProfilesAsync(string sourcePath)
    {
        var imported = await _settingsService.ImportProfilesAsync(sourcePath, _settings);
        if (!imported.IsSuccess || imported.Value is null)
            return OperationResult.Failure(imported.UserMessage, imported.TechnicalDetails);
        var hotkeyResult = _globalHotkeyService.ReplaceBindings(
            GetRegisteredHotkeys(imported.Value));
        if (!hotkeyResult.IsSuccess) return hotkeyResult;
        var save = await _settingsService.SaveAsync(imported.Value);
        if (!save.IsSuccess)
        {
            _globalHotkeyService.ReplaceBindings(GetRegisteredHotkeys(_settings));
            return save;
        }
        _settings = imported.Value;
        RebuildModeCards();
        NotifyAppPreferencesChanged();
        return OperationResult.Success("モード設定を読み込みました。");
    }

    private static string GetMicrophoneResultDisplayName(
        ModeCardViewModel card,
        OperationResult<bool> current)
    {
        if (card.Mode.MicrophoneMute != MicrophoneMuteSetting.NoChange)
            return LocalizationService.Format("Status.MicrophoneSummary", card.MicrophoneSummary);

        return current.IsSuccess
            ? LocalizationService.Format("Status.MicrophoneNoChangeCurrent", current.Value ? "OFF" : "ON")
            : LocalizationService.Get("Status.MicrophoneNoChangeUnknown");
    }

    private async Task EditModeAsync(object? parameter)
    {
        if (parameter is not ModeCardViewModel card)
            return;

        var editor = new AdvancedModeEditorWindow(
            card.Mode.Copy(),
            PowerPlans.ToList(),
            card.HasBattery,
            Application.Current.MainWindow);
        if (editor.ShowDialog() != true || editor.EditedMode is null)
            return;

        var index = _settings.Modes.FindIndex(mode => mode.Id == card.Mode.Id);
        if (index < 0)
            return;

        _settings.Modes[index] = editor.EditedMode;
        var result = await _settingsService.SaveAsync(_settings);
        if (result.IsSuccess) RebuildModeCards();
        else card.Replace(editor.EditedMode);
        StatusMessage = result.IsSuccess
            ? LocalizationService.Format("Status.ModeSettingsSaved", card.Name)
            : result.UserMessage;
        var detection = await RefreshCurrentModeCoreAsync();
        if (!detection.IsSuccess)
            AppendStatusWarning(detection.UserMessage);
    }

    private void RepairUnavailableDefaultPlans()
    {
        if (PowerPlans.Count == 0)
            return;

        var balanced = PowerPlans.FirstOrDefault(plan => plan.Id == PowerSettingsService.BalancedSchemeId);
        var active = PowerPlans.FirstOrDefault(plan => plan.IsActive) ?? PowerPlans[0];
        foreach (var mode in _settings.Modes)
        {
            if (PowerPlans.Any(plan => plan.Id == mode.PowerPlanId))
                continue;
            mode.PowerPlanId = balanced?.Id ?? active.Id;
        }
    }

    private async Task<OperationResult> RefreshCurrentModeCoreAsync(string? preferredModeId = null)
    {
        var detection = await _powerService.DetectCurrentModeAsync(
            _settings.Modes,
            preferredModeId ?? _settings.LastAppliedModeId);
        if (!detection.IsSuccess || detection.Value is null)
        {
            SetUnconfirmedMode();
            return OperationResult.Failure(
                detection.UserMessage,
                detection.TechnicalDetails);
        }

        SetDetectedMode(detection.Value.ModeId);
        return OperationResult.Success();
    }

    private OperationResult<bool> RefreshMicrophoneStateCore()
    {
        var current = _microphoneMuteService.GetCurrentMuted();
        IsMicrophoneOn = current.IsSuccess ? !current.Value : null;
        return current;
    }

    private void SetDetectedMode(string? modeId)
    {
        var mode = _settings.Modes.FirstOrDefault(value => value.Id == modeId);
        if (mode is not null)
        {
            CurrentModeId = mode.Id;
            CurrentModeName = mode.Name;
            CurrentModeIcon = mode.Icon;
            return;
        }

        CurrentModeId = UnregisteredModeId;
        CurrentModeName = LocalizationService.Translate("未登録の設定");
        CurrentModeIcon = "?";
    }

    private void SetUnconfirmedMode()
    {
        CurrentModeId = null;
        CurrentModeName = LocalizationService.Translate("確認できません");
        CurrentModeIcon = "?";
    }

    private string GetPowerPlanName(Guid planId) =>
        PowerPlans.FirstOrDefault(plan => plan.Id == planId)?.Name ?? LocalizationService.Translate("利用不可");

    private static string GetAppVersion()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null ? LocalizationService.Translate("バージョン不明") : $"v{version.ToString(3)}";
    }

    private void NotifyAppPreferencesChanged()
    {
        OnPropertyChanged(nameof(CloseButtonBehavior));
        OnPropertyChanged(nameof(ShowTrayNotification));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(ShowMicrophoneControls));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Hotkeys));
        OnPropertyChanged(nameof(RestoreHotkey));
        OnPropertyChanged(nameof(VisibleModeIds));
        OnPropertyChanged(nameof(AllProfiles));
        ToggleMicrophoneCommand.RaiseCanExecuteChanged();
    }

    private void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(MicrophoneButtonText));
        OnPropertyChanged(nameof(MicrophoneButtonToolTip));
        if (CurrentModeId == UnregisteredModeId)
            CurrentModeName = LocalizationService.Translate("未登録の設定");
        else if (CurrentModeId is null)
            CurrentModeName = LocalizationService.Translate("確認できません");
    }

    private void RefreshMicrophonePreference()
    {
        foreach (var mode in Modes)
            mode.ShowMicrophoneControls = _settings.ShowMicrophoneControls;

        if (_settings.ShowMicrophoneControls)
            RefreshMicrophoneStateCore();
        else
            IsMicrophoneOn = null;
    }

    private void RebuildVisibleModes()
    {
        VisibleModes.Clear();
        foreach (var modeId in _settings.VisibleModeIds)
        {
            var mode = Modes.FirstOrDefault(card =>
                string.Equals(card.Mode.Id, modeId, StringComparison.OrdinalIgnoreCase));
            if (mode is not null)
                VisibleModes.Add(mode);
        }
    }

    private void RebuildModeCards()
    {
        Modes.Clear();
        var hasBattery = _powerService.HasBattery;
        foreach (var mode in _settings.Modes)
        {
            if (!mode.IsEnabled) continue;
            Modes.Add(new ModeCardViewModel(
                mode,
                GetPowerPlanName,
                hasBattery,
                _settings.ShowMicrophoneControls));
        }
        RebuildVisibleModes();
        OnPropertyChanged(nameof(Hotkeys));
        OnPropertyChanged(nameof(RestoreHotkey));
        OnPropertyChanged(nameof(VisibleModeIds));
        OnPropertyChanged(nameof(AllProfiles));
    }

    private void AssignNextAdditionalCustomIdentity(PcMode mode)
    {
        var identity = AdditionalCustomModeIdentities.FirstOrDefault(candidate =>
            !_settings.Modes.Any(existing => string.Equals(
                existing.Id,
                candidate.Id,
                StringComparison.OrdinalIgnoreCase)));
        if (identity == default)
            return;

        mode.Id = identity.Id;
        mode.Icon = identity.Icon;
    }

    private void OnModeEngineSessionChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            OnPropertyChanged(nameof(HasActiveSession));
            RestoreModeCommand.RaiseCanExecuteChanged();
        }));
    }

    private void AppendStatusWarning(string message)
    {
        StatusMessage = string.IsNullOrWhiteSpace(StatusMessage)
            ? message
            : $"{StatusMessage}{Environment.NewLine}{Environment.NewLine}※ {message}";
    }

    private static IReadOnlyCollection<ModeHotkey> GetRegisteredHotkeys(AppSettings settings) =>
        settings.Hotkeys.Where(hotkey => settings.Modes.Any(mode => mode.IsEnabled &&
                string.Equals(mode.Id, hotkey.ModeId, StringComparison.OrdinalIgnoreCase)))
            .Append(settings.RestoreHotkey).ToList();
}
