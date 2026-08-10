using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;
using System.Text.Json;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("既定の3モード", TestDefaultModesAsync),
    ("設定の保存と再読み込み", TestSettingsRoundTripAsync),
    ("旧設定からのショートカット設定移行", TestLegacySettingsMigrationAsync),
    ("スタートアップ起動引数の判定", TestStartupLaunchArgumentAsync),
    ("ショートカットの入力検証", TestHotkeyValidationAsync),
    ("アプリ設定の連携と失敗時復元", TestAppPreferenceIntegrationAsync),
    ("多重起動の検出と既存画面への通知", TestSingleInstanceCoordinatorAsync),
    ("利用可能な電源プランの読み取り", TestPowerPlanEnumerationAsync),
    ("モードの3設定一括適用", TestModeApplyAsync),
    ("途中失敗時の復元と結果表示", TestPartialFailureRollbackAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL: {test.Name}: {ex.Message}");
        Console.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count}件のテストが失敗しました。");
    return 1;
}

Console.WriteLine($"{tests.Count}件のテストが成功しました。");
return 0;

static Task TestDefaultModesAsync()
{
    var settings = SettingsService.CreateDefaults();
    Assert(settings.Modes.Count == 3, "モード数が3ではありません。");
    Assert(settings.Modes.Select(mode => mode.Id).SequenceEqual(["game", "work", "normal"]),
        "既定モードの並びが正しくありません。");
    Assert(settings.Modes[0].DisplayTimeoutAc == 0 && settings.Modes[0].SleepTimeoutAc == 0,
        "GAMEの既定タイムアウトが正しくありません。");
    Assert(settings.CloseButtonBehavior == CloseButtonBehavior.MinimizeToTray,
        "閉じるボタンの既定動作が通知領域への格納ではありません。");
    Assert(!settings.ShowTrayNotification,
        "通知領域への格納通知が既定で有効になっています。");
    Assert(!settings.StartWithWindows,
        "Windowsログイン時の自動起動が既定で有効になっています。");
    Assert(settings.Hotkeys.Count == 3 && settings.Hotkeys.All(hotkey => !hotkey.IsConfigured),
        "ショートカットの既定値が未設定ではありません。");
    return Task.CompletedTask;
}

static async Task TestSettingsRoundTripAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        var service = new SettingsService(testDirectory);
        var settings = SettingsService.CreateDefaults();
        settings.LastAppliedModeId = "work";
        settings.Modes[1].DisplayTimeoutAc = 15 * 60;
        settings.CloseButtonBehavior = CloseButtonBehavior.ExitApplication;
        settings.ShowTrayNotification = true;
        settings.StartWithWindows = true;
        settings.Hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        settings.Hotkeys[0].VirtualKey = 0x47;

        var save = await service.SaveAsync(settings);
        Assert(save.IsSuccess, $"保存に失敗しました: {save.UserMessage}");

        var load = await service.LoadAsync();
        Assert(load.IsSuccess && load.Value is not null, $"読み込みに失敗しました: {load.UserMessage}");
        var loaded = load.Value ?? throw new InvalidOperationException("設定データがありません。");
        Assert(loaded.LastAppliedModeId == "work", "最後に適用したモードが保持されていません。");
        Assert(loaded.Modes[1].DisplayTimeoutAc == 15 * 60, "編集した時間が保持されていません。");
        Assert(loaded.CloseButtonBehavior == CloseButtonBehavior.ExitApplication,
            "閉じるボタンの動作が保持されていません。");
        Assert(loaded.ShowTrayNotification, "通知表示の設定が保持されていません。");
        Assert(loaded.StartWithWindows, "Windowsログイン時の自動起動設定が保持されていません。");
        Assert(loaded.Hotkeys[0].Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt) &&
               loaded.Hotkeys[0].VirtualKey == 0x47,
            "GAMEショートカットが保持されていません。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static async Task TestLegacySettingsMigrationAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(testDirectory);
        var defaults = SettingsService.CreateDefaults();
        var legacySettings = new
        {
            defaults.Version,
            defaults.Modes,
            defaults.LastAppliedModeId,
            defaults.CloseButtonBehavior,
            defaults.ShowTrayNotification
        };
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory, "settings.json"),
            JsonSerializer.Serialize(legacySettings));

        var load = await new SettingsService(testDirectory).LoadAsync();
        Assert(load.IsSuccess && load.Value is not null, "旧設定ファイルを読み込めませんでした。");
        var migrated = load.Value ?? throw new InvalidOperationException("移行後の設定データがありません。");
        Assert(!migrated.StartWithWindows, "移行後の自動起動設定が既定値ではありません。");
        Assert(migrated.Hotkeys.Select(hotkey => hotkey.ModeId)
                .SequenceEqual(["game", "work", "normal"]),
            "旧設定へ3モードのショートカット設定が補完されませんでした。");
        Assert(migrated.Hotkeys.All(hotkey => !hotkey.IsConfigured),
            "旧設定の移行時にショートカットが勝手に有効になりました。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static Task TestHotkeyValidationAsync()
{
    var hotkeys = SettingsService.CreateDefaultHotkeys();
    hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    hotkeys[0].VirtualKey = 0x47;
    Assert(HotkeyValidator.Validate(hotkeys).IsSuccess,
        "有効なショートカットが拒否されました。");
    Assert(HotkeyValidator.Format(hotkeys[0]) == "Ctrl + Alt + G",
        "ショートカットの表示形式が正しくありません。");

    hotkeys[1].Modifiers = hotkeys[0].Modifiers;
    hotkeys[1].VirtualKey = hotkeys[0].VirtualKey;
    Assert(!HotkeyValidator.Validate(hotkeys).IsSuccess,
        "重複したショートカットが許可されました。");

    hotkeys[1].VirtualKey = 0x57;
    hotkeys[2].Modifiers = HotkeyModifiers.Control;
    hotkeys[2].VirtualKey = 0x7B;
    Assert(!HotkeyValidator.Validate(hotkeys).IsSuccess,
        "Windowsで予約されているF12が許可されました。");
    return Task.CompletedTask;
}

static Task TestStartupLaunchArgumentAsync()
{
    Assert(PCModeSwitcher.App.IsStartupLaunch(["--startup"]),
        "スタートアップ起動引数を認識できませんでした。");
    Assert(PCModeSwitcher.App.IsStartupLaunch(["--STARTUP"]),
        "スタートアップ起動引数の大文字小文字を区別しています。");
    Assert(!PCModeSwitcher.App.IsStartupLaunch([]),
        "通常起動がスタートアップ起動として扱われました。");
    Assert(!PCModeSwitcher.App.IsStartupLaunch(["--unknown"]),
        "未対応の起動引数がスタートアップ起動として扱われました。");
    return Task.CompletedTask;
}

static async Task TestAppPreferenceIntegrationAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        var planId = Guid.NewGuid();
        var startup = new FakeStartupService();
        var hotkeyService = new FakeGlobalHotkeyService();
        var viewModel = new MainViewModel(
            new SettingsService(testDirectory),
            new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => true),
            startup,
            hotkeyService);
        await viewModel.InitializeAsync();

        var applyResult = await viewModel.ApplyModeByIdAsync("work");
        Assert(applyResult?.IsSuccess == true, "通知領域用のモード適用に失敗しました。");
        Assert(viewModel.CurrentModeId == "work" && viewModel.CurrentModeName == "WORK",
            "通知領域用のモード適用後に現在のモードが更新されていません。");

        var hotkeys = SettingsService.CreateDefaultHotkeys();
        hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        hotkeys[0].VirtualKey = 0x47;
        var save = await viewModel.SetAppPreferencesAsync(
            CloseButtonBehavior.MinimizeToTray,
            false,
            true,
            hotkeys);
        Assert(save.IsSuccess, $"アプリ設定を保存できませんでした: {save.UserMessage}");
        Assert(startup.Enabled, "スタートアップ登録が有効になっていません。");
        Assert(hotkeyService.Bindings.Single(hotkey => hotkey.ModeId == "game").IsConfigured,
            "グローバルショートカットが登録されていません。");

        var loaded = await new SettingsService(testDirectory).LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.StartWithWindows == true,
            "連携したアプリ設定がファイルへ保存されていません。");

        hotkeyService.NextResult = OperationResult.Failure("テスト用の登録失敗です。");
        var failedSave = await viewModel.SetAppPreferencesAsync(
            CloseButtonBehavior.ExitApplication,
            true,
            false,
            SettingsService.CreateDefaultHotkeys());
        Assert(!failedSave.IsSuccess, "ショートカット登録失敗が成功扱いになりました。");
        Assert(startup.Enabled, "ショートカット登録失敗後にスタートアップ設定が復元されていません。");
        Assert(viewModel.CloseButtonBehavior == CloseButtonBehavior.MinimizeToTray,
            "ショートカット登録失敗後に保存前の設定が変わっています。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static Task TestSingleInstanceCoordinatorAsync()
{
    var applicationId = $"PCModeSwitcher.Tests.{Guid.NewGuid():N}";
    using var activationRequested = new ManualResetEventSlim();

    using (var primary = new SingleInstanceCoordinator(applicationId))
    {
        primary.ActivationRequested += (_, _) => activationRequested.Set();
        Assert(primary.TryAcquire(), "最初のインスタンスを取得できませんでした。");

        bool? secondaryAcquired = null;
        Exception? secondaryException = null;
        var secondaryThread = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstanceCoordinator(applicationId);
                secondaryAcquired = secondary.TryAcquire();
            }
            catch (Exception ex)
            {
                secondaryException = ex;
            }
        })
        {
            IsBackground = true
        };
        secondaryThread.Start();

        Assert(secondaryThread.Join(TimeSpan.FromSeconds(3)),
            "2個目のインスタンスの検出が完了しませんでした。");
        if (secondaryException is not null)
        {
            throw new InvalidOperationException("2個目のインスタンスの検出中に失敗しました。", secondaryException);
        }

        Assert(secondaryAcquired == false, "2個目のインスタンスが起動可能になっています。");
        Assert(activationRequested.Wait(TimeSpan.FromSeconds(3)),
            "既存インスタンスへ表示要求が通知されませんでした。");
    }

    using var replacement = new SingleInstanceCoordinator(applicationId);
    Assert(replacement.TryAcquire(), "終了後に新しいインスタンスを取得できませんでした。");
    return Task.CompletedTask;
}

static async Task TestPowerPlanEnumerationAsync()
{
    var service = new PowerSettingsService();
    var result = await service.GetAvailablePlansAsync();
    Assert(result.IsSuccess && result.Value is { Count: > 0 },
        $"電源プランを読み取れませんでした: {result.UserMessage} {result.TechnicalDetails}");
    var plans = result.Value ?? throw new InvalidOperationException("電源プラン一覧がありません。");
    Assert(plans.Any(plan => plan.IsActive), "現在有効な電源プランを特定できませんでした。");
}

static async Task TestModeApplyAsync()
{
    var planId = Guid.NewGuid();
    var policy = new FakePowerPolicyAccessor(planId);
    var service = new PowerSettingsService(policy, () => true);
    var mode = CreateTestMode(planId);

    var result = await service.ApplyModeAsync(mode);

    Assert(result.IsSuccess, result.ToUserMessage(mode.Name));
    Assert(policy.ActiveScheme == planId, "指定した電源プランが有効になっていません。");
    Assert(policy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Ac) == mode.DisplayTimeoutAc,
        "AC画面OFF時間が適用されていません。");
    Assert(policy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc) == mode.DisplayTimeoutBattery,
        "DC画面OFF時間が適用されていません。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Ac) == mode.SleepTimeoutAc,
        "ACスリープ時間が適用されていません。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc) == mode.SleepTimeoutBattery,
        "DCスリープ時間が適用されていません。");
}

static async Task TestPartialFailureRollbackAsync()
{
    var planId = Guid.NewGuid();
    var policy = new FakePowerPolicyAccessor(planId)
    {
        FailOnceSettingId = PowerSettingsService.SleepTimeoutId,
        FailOnceSource = PowerSource.Dc
    };
    var originalSleepAc = policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Ac);
    var originalSleepDc = policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc);
    var service = new PowerSettingsService(policy, () => true);
    var mode = CreateTestMode(planId);

    var result = await service.ApplyModeAsync(mode);

    Assert(!result.IsSuccess, "一部失敗が成功として扱われました。");
    Assert(result.Steps.Single(step => step.Name == "電源モード").IsSuccess,
        "成功した電源モードが失敗扱いです。");
    Assert(result.Steps.Single(step => step.Name == "画面OFF").IsSuccess,
        "成功した画面OFFが失敗扱いです。");
    Assert(!result.Steps.Single(step => step.Name == "スリープ").IsSuccess,
        "失敗したスリープが成功扱いです。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Ac) == originalSleepAc,
        "失敗後にACスリープ時間が復元されていません。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc) == originalSleepDc,
        "失敗後にDCスリープ時間が復元されていません。");
}

static PCModeSwitcher.Models.PcMode CreateTestMode(Guid planId) => new()
{
    Id = "test",
    Name = "TEST",
    DisplayTimeoutAc = 300,
    DisplayTimeoutBattery = 120,
    SleepTimeoutAc = 900,
    SleepTimeoutBattery = 600,
    PowerPlanId = planId
};

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakePowerPolicyAccessor : IPowerPolicyAccessor
{
    private readonly Guid _schemeId;
    private readonly Dictionary<(Guid SettingId, PowerSource Source), uint> _values = new()
    {
        [(PowerSettingsService.DisplayTimeoutId, PowerSource.Ac)] = 60,
        [(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc)] = 60,
        [(PowerSettingsService.SleepTimeoutId, PowerSource.Ac)] = 120,
        [(PowerSettingsService.SleepTimeoutId, PowerSource.Dc)] = 120
    };

    public FakePowerPolicyAccessor(Guid schemeId)
    {
        _schemeId = schemeId;
        ActiveScheme = schemeId;
    }

    public Guid ActiveScheme { get; private set; }
    public Guid? FailOnceSettingId { get; init; }
    public PowerSource? FailOnceSource { get; init; }
    private bool HasFailed { get; set; }

    public OperationResult<Guid> GetActiveScheme() => OperationResult<Guid>.Success(ActiveScheme);

    public OperationResult<IReadOnlyList<PCModeSwitcher.Models.PowerPlan>> GetSchemes() =>
        OperationResult<IReadOnlyList<PCModeSwitcher.Models.PowerPlan>>.Success(
            [new PCModeSwitcher.Models.PowerPlan(_schemeId, "テストプラン", ActiveScheme == _schemeId)]);

    public OperationResult<uint> ReadValue(
        Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source) =>
        _values.TryGetValue((settingId, source), out var value)
            ? OperationResult<uint>.Success(value)
            : OperationResult<uint>.Failure("テスト値がありません。");

    public OperationResult WriteValue(
        Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source, uint seconds)
    {
        if (!HasFailed && FailOnceSettingId == settingId && FailOnceSource == source)
        {
            HasFailed = true;
            return OperationResult.Failure("テスト用の書き込み失敗です。");
        }

        _values[(settingId, source)] = seconds;
        return OperationResult.Success();
    }

    public OperationResult ActivateScheme(Guid schemeId)
    {
        ActiveScheme = schemeId;
        return OperationResult.Success();
    }

    public uint GetValue(Guid settingId, PowerSource source) => _values[(settingId, source)];
}

sealed class FakeStartupService : IStartupService
{
    public bool Enabled { get; private set; }

    public OperationResult SetEnabled(bool enabled)
    {
        Enabled = enabled;
        return OperationResult.Success();
    }
}

sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
{
    public event EventHandler<ModeHotkeyPressedEventArgs>? HotkeyPressed
    {
        add { }
        remove { }
    }
    public IReadOnlyList<ModeHotkey> Bindings { get; private set; } = [];
    public OperationResult? NextResult { get; set; }

    public OperationResult ReplaceBindings(IReadOnlyCollection<ModeHotkey> hotkeys)
    {
        if (NextResult is { } nextResult)
        {
            NextResult = null;
            return nextResult;
        }

        Bindings = hotkeys.Select(hotkey => hotkey.Copy()).ToList();
        return OperationResult.Success();
    }
}
