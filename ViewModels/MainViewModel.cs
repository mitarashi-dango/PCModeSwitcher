using System.Collections.ObjectModel;
using System.Windows;
using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.Views;

namespace PCModeSwitcher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly PowerSettingsService _powerService;
    private AppSettings _settings = SettingsService.CreateDefaults();
    private bool _isBusy;
    private string _currentModeName = "未選択";
    private string _currentModeIcon = "—";
    private string _statusMessage = "モードを選ぶと、3つの設定をまとめて切り替えます。";

    public ObservableCollection<ModeCardViewModel> Modes { get; } = [];
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
        }
    }
    public bool IsInteractionEnabled => !IsBusy;
    public string CurrentModeName { get => _currentModeName; private set => SetProperty(ref _currentModeName, value); }
    public string CurrentModeIcon { get => _currentModeIcon; private set => SetProperty(ref _currentModeIcon, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public AsyncRelayCommand ApplyModeCommand { get; }
    public AsyncRelayCommand EditModeCommand { get; }

    public MainViewModel(SettingsService settingsService, PowerSettingsService powerService)
    {
        _settingsService = settingsService;
        _powerService = powerService;
        ApplyModeCommand = new AsyncRelayCommand(ApplyModeAsync, _ => !IsBusy);
        EditModeCommand = new AsyncRelayCommand(EditModeAsync, _ => !IsBusy);
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
                StatusMessage = $"{settingsResult.UserMessage} 初期設定で開始しました。";
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

            foreach (var mode in _settings.Modes)
                Modes.Add(new ModeCardViewModel(mode, GetPowerPlanName));
            SetCurrentMode(_settings.LastAppliedModeId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyModeAsync(object? parameter)
    {
        if (parameter is not ModeCardViewModel card)
            return;

        IsBusy = true;
        StatusMessage = $"{card.Name}モードを適用しています…";
        try
        {
            var result = await _powerService.ApplyModeAsync(card.Mode);
            StatusMessage = result.ToUserMessage(card.Name);

            if (result.IsSuccess)
            {
                _settings.LastAppliedModeId = card.Mode.Id;
                SetCurrentMode(card.Mode.Id);
                var save = await _settingsService.SaveAsync(_settings);
                if (!save.IsSuccess)
                    StatusMessage += $"{Environment.NewLine}{Environment.NewLine}※ {save.UserMessage}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EditModeAsync(object? parameter)
    {
        if (parameter is not ModeCardViewModel card)
            return;

        var editor = new ModeEditorWindow(
            card.Mode.Copy(),
            PowerPlans.ToList(),
            Application.Current.MainWindow);
        if (editor.ShowDialog() != true || editor.EditedMode is null)
            return;

        var index = _settings.Modes.FindIndex(mode => mode.Id == card.Mode.Id);
        if (index < 0)
            return;

        _settings.Modes[index] = editor.EditedMode;
        card.Replace(editor.EditedMode);
        var result = await _settingsService.SaveAsync(_settings);
        StatusMessage = result.IsSuccess
            ? $"{card.Name}モードの設定を保存しました。"
            : result.UserMessage;
        SetCurrentMode(_settings.LastAppliedModeId);
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

    private void SetCurrentMode(string? modeId)
    {
        var mode = _settings.Modes.FirstOrDefault(value => value.Id == modeId);
        CurrentModeName = mode?.Name ?? "未選択";
        CurrentModeIcon = mode?.Icon ?? "—";
    }

    private string GetPowerPlanName(Guid planId) =>
        PowerPlans.FirstOrDefault(plan => plan.Id == planId)?.Name ?? "利用不可";
}
