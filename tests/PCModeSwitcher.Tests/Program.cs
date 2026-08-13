using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;
using System.Text.Json;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("既定の4モード", TestDefaultModesAsync),
    ("設定の保存と再読み込み", TestSettingsRoundTripAsync),
    ("旧設定からのショートカット設定移行", TestLegacySettingsMigrationAsync),
    ("スタートアップ起動引数の判定", TestStartupLaunchArgumentAsync),
    ("ショートカットの入力検証", TestHotkeyValidationAsync),
    ("アプリ設定の連携と失敗時復元", TestAppPreferenceIntegrationAsync),
    ("起動時に前回値より実際のWindows設定を優先", TestInitialModeDetectionAsync),
    ("多重起動の検出と既存画面への通知", TestSingleInstanceCoordinatorAsync),
    ("利用可能な電源プランの読み取り", TestPowerPlanEnumerationAsync),
    ("Windows設定からの現在モード自動判定", TestCurrentModeDetectionAsync),
    ("バッテリー有無による表示と適用の切り替え", TestBatteryAwareBehaviorAsync),
    ("電源3設定の一括適用", TestModeApplyAsync),
    ("マイクミュート設定の適用と復元", TestMicrophoneMuteAsync),
    ("モードの4設定一括適用", TestModeMicrophoneIntegrationAsync),
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
    Assert(settings.Modes.Count == 4, "モード数が4ではありません。");
    Assert(settings.Modes.Select(mode => mode.Id).SequenceEqual(["game", "work", "normal", "custom"]),
        "既定モードの並びが正しくありません。");
    Assert(settings.Modes[0].DisplayTimeoutAc == 0 && settings.Modes[0].SleepTimeoutAc == 0,
        "GAMEの既定タイムアウトが正しくありません。");
    Assert(settings.CloseButtonBehavior == CloseButtonBehavior.MinimizeToTray,
        "閉じるボタンの既定動作が通知領域への格納ではありません。");
    Assert(!settings.ShowTrayNotification,
        "通知領域への格納通知が既定で有効になっています。");
    Assert(!settings.StartWithWindows,
        "Windowsログイン時の自動起動が既定で有効になっています。");
    Assert(settings.Modes[3].Name == "CUSTOM" && settings.Modes[3].Icon == "⚙",
        "CUSTOMの既定表示が正しくありません。");
    Assert(settings.Modes.All(mode => mode.MicrophoneMute == MicrophoneMuteSetting.NoChange),
        "マイク設定の既定値が『変更しない』ではありません。");
    Assert(settings.Hotkeys.Count == 4 && settings.Hotkeys.All(hotkey => !hotkey.IsConfigured),
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
        settings.Modes[1].MicrophoneMute = MicrophoneMuteSetting.Mute;
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
        Assert(loaded.Modes[1].MicrophoneMute == MicrophoneMuteSetting.Mute,
            "編集したマイク設定が保持されていません。");
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
            Modes = defaults.Modes
                .Where(mode => mode.Id != "custom")
                .Select(mode => new
                {
                    mode.Id,
                    mode.Name,
                    mode.Icon,
                    mode.DisplayTimeoutAc,
                    mode.DisplayTimeoutBattery,
                    mode.SleepTimeoutAc,
                    mode.SleepTimeoutBattery,
                    mode.PowerPlanId
                })
                .ToList(),
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
        Assert(migrated.Modes.Select(mode => mode.Id)
                .SequenceEqual(["game", "work", "normal", "custom"]),
            "旧設定へCUSTOMモードが補完されませんでした。");
        Assert(migrated.Hotkeys.Select(hotkey => hotkey.ModeId)
                .SequenceEqual(["game", "work", "normal", "custom"]),
            "旧設定へ4モードのショートカット設定が補完されませんでした。");
        Assert(migrated.Hotkeys.All(hotkey => !hotkey.IsConfigured),
            "旧設定の移行時にショートカットが勝手に有効になりました。");
        Assert(migrated.Modes.All(mode => mode.MicrophoneMute == MicrophoneMuteSetting.NoChange),
            "旧設定のマイク設定が『変更しない』で補完されませんでした。");
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
        var microphoneMuteService = new FakeMicrophoneMuteService();
        var viewModel = new MainViewModel(
            new SettingsService(testDirectory),
            new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => true),
            microphoneMuteService,
            startup,
            hotkeyService);
        await viewModel.InitializeAsync();

        var applyResult = await viewModel.ApplyModeByIdAsync("work");
        Assert(applyResult?.IsSuccess == true, "通知領域用のモード適用に失敗しました。");
        Assert(applyResult?.Steps.Single(step => step.Name == "マイク").IsSkipped == true,
            "『変更しない』のマイク設定がスキップ表示になっていません。");
        Assert(viewModel.CurrentModeId == "work" && viewModel.CurrentModeName == "WORK",
            "通知領域用のモード適用後に現在のモードが更新されていません。");

        var customApplyResult = await viewModel.ApplyModeByIdAsync(MainViewModel.CustomModeId);
        Assert(customApplyResult?.IsSuccess == true, "CUSTOMモードの適用に失敗しました。");
        Assert(viewModel.CurrentModeId == MainViewModel.CustomModeId &&
               viewModel.CurrentModeName == "CUSTOM",
            "CUSTOMモード適用後に現在のモードが更新されていません。");

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

static async Task TestInitialModeDetectionAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        var planId = Guid.NewGuid();
        var settingsService = new SettingsService(testDirectory);
        var settings = SettingsService.CreateDefaults();
        foreach (var mode in settings.Modes)
            mode.PowerPlanId = planId;
        settings.LastAppliedModeId = "game";
        var save = await settingsService.SaveAsync(settings);
        Assert(save.IsSuccess, "自動判定テスト用の設定を保存できませんでした。");

        var actualMode = settings.Modes.Single(mode => mode.Id == "work");
        var policy = new FakePowerPolicyAccessor(planId);
        policy.SetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Ac, actualMode.DisplayTimeoutAc);
        policy.SetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc, actualMode.DisplayTimeoutBattery);
        policy.SetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Ac, actualMode.SleepTimeoutAc);
        policy.SetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc, actualMode.SleepTimeoutBattery);

        var viewModel = new MainViewModel(
            settingsService,
            new PowerSettingsService(policy, () => true),
            new FakeMicrophoneMuteService(),
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        Assert(viewModel.CurrentModeId == "work" && viewModel.CurrentModeName == "WORK",
            "起動時に前回適用したモードではなく実際のWindows設定が優先されていません。");

        policy.SetValue(
            PowerSettingsService.SleepTimeoutId,
            PowerSource.Ac,
            actualMode.SleepTimeoutAc + 1);
        await viewModel.RefreshCurrentModeAsync();
        Assert(viewModel.CurrentModeId == MainViewModel.UnregisteredModeId &&
               viewModel.CurrentModeName == "未登録の設定",
            "外部で変更されたWindows設定が未登録表示へ反映されませんでした。");
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

static Task TestMicrophoneMuteAsync()
{
    var accessor = new FakeMicrophoneMuteAccessor();
    var service = new MicrophoneMuteService(accessor);

    var noChange = service.Apply(MicrophoneMuteSetting.NoChange);
    Assert(noChange.IsSuccess, "『変更しない』が失敗しました。");
    Assert(accessor.GetCount == 0 && accessor.SetCount == 0,
        "『変更しない』でマイクへアクセスしました。");

    var mute = service.Apply(MicrophoneMuteSetting.Mute);
    Assert(mute.IsSuccess && accessor.Muted, "マイクをミュートできませんでした。");

    var unmute = service.Apply(MicrophoneMuteSetting.Unmute);
    Assert(unmute.IsSuccess && !accessor.Muted, "マイクのミュートを解除できませんでした。");

    accessor.FailNextVerification = true;
    var failedVerification = service.Apply(MicrophoneMuteSetting.Mute);
    Assert(!failedVerification.IsSuccess, "反映確認失敗が成功扱いになりました。");
    Assert(!accessor.Muted, "反映確認失敗後に変更前のミュート状態へ戻っていません。");
    return Task.CompletedTask;
}

static async Task TestModeMicrophoneIntegrationAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        var planId = Guid.NewGuid();
        var settingsService = new SettingsService(testDirectory);
        var settings = SettingsService.CreateDefaults();
        foreach (var mode in settings.Modes)
            mode.PowerPlanId = planId;
        settings.Modes.Single(mode => mode.Id == "game").MicrophoneMute = MicrophoneMuteSetting.Mute;
        var save = await settingsService.SaveAsync(settings);
        Assert(save.IsSuccess, "マイク連携テスト用の設定を保存できませんでした。");

        var microphoneMuteService = new FakeMicrophoneMuteService();
        var viewModel = new MainViewModel(
            settingsService,
            new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => true),
            microphoneMuteService,
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var result = await viewModel.ApplyModeByIdAsync("game")
            ?? throw new InvalidOperationException("GAMEモードの適用結果がありません。");
        Assert(result.IsSuccess, "マイクを含むモード適用に失敗しました。");
        Assert(result.Steps.Count == 4 &&
               result.Steps.Single(step => step.Name == "マイク") is { IsSuccess: true, IsSkipped: false },
            "モード適用結果にマイクが含まれていません。");
        Assert(microphoneMuteService.LastSetting == MicrophoneMuteSetting.Mute,
            "モードに保存したマイク設定が適用されていません。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static async Task TestCurrentModeDetectionAsync()
{
    var planId = Guid.NewGuid();
    var policy = new FakePowerPolicyAccessor(planId);
    var service = new PowerSettingsService(policy, () => true);
    var mode = CreateTestMode(planId);

    var apply = await service.ApplyModeAsync(mode);
    Assert(apply.IsSuccess, "自動判定用のモードを適用できませんでした。");

    var detected = await service.DetectCurrentModeAsync([mode]);
    Assert(detected.IsSuccess && detected.Value?.ModeId == mode.Id,
        "実際のWindows設定と一致するモードを判定できませんでした。");

    policy.SetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Ac, mode.DisplayTimeoutAc + 1);
    var unregistered = await service.DetectCurrentModeAsync([mode]);
    Assert(unregistered.IsSuccess && unregistered.Value?.IsUnregistered == true,
        "登録モードと一致しない設定が未登録として判定されませんでした。");

    var desktopPolicy = new FakePowerPolicyAccessor(planId);
    var desktopService = new PowerSettingsService(desktopPolicy, () => false);
    var desktopApply = await desktopService.ApplyModeAsync(mode);
    Assert(desktopApply.IsSuccess, "デスクトップPC用のモードを適用できませんでした。");
    Assert(desktopPolicy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc) !=
           mode.DisplayTimeoutBattery,
        "判定テストのDC値が偶然一致しています。");

    var desktopDetected = await desktopService.DetectCurrentModeAsync([mode]);
    Assert(desktopDetected.IsSuccess && desktopDetected.Value?.ModeId == mode.Id,
        "バッテリーなしPCで不要なDC設定が現在モード判定へ影響しています。");
}

static async Task TestBatteryAwareBehaviorAsync()
{
    var planId = Guid.NewGuid();
    var mode = CreateTestMode(planId);
    var batteryService = new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => true);
    Assert(batteryService.HasBattery, "バッテリー搭載PCがバッテリーなしとして判定されました。");

    var batteryCard = new ModeCardViewModel(mode, _ => "テストプラン", true);
    Assert(batteryCard.DisplaySummary.Contains("バッテリー", StringComparison.Ordinal),
        "バッテリー搭載PCでバッテリー設定が概要から消えています。");
    Assert(batteryCard.SleepSummary.Contains("バッテリー", StringComparison.Ordinal),
        "バッテリー搭載PCでバッテリー設定が概要から消えています。");

    var policy = new FakePowerPolicyAccessor(planId);
    var originalDisplayDc = policy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc);
    var originalSleepDc = policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc);
    var desktopService = new PowerSettingsService(policy, () => false);
    Assert(!desktopService.HasBattery, "バッテリーなしPCがバッテリーありとして判定されました。");

    var desktopCard = new ModeCardViewModel(mode, _ => "テストプラン", false);
    Assert(desktopCard.DisplaySummary == $"AC {ModeCardViewModel.FormatTimeout(mode.DisplayTimeoutAc)}",
        "バッテリーなしPCの画面OFF概要がAC設定だけになっていません。");
    Assert(desktopCard.SleepSummary == $"AC {ModeCardViewModel.FormatTimeout(mode.SleepTimeoutAc)}",
        "バッテリーなしPCのスリープ概要がAC設定だけになっていません。");

    var result = await desktopService.ApplyModeAsync(mode);
    Assert(result.IsSuccess, result.ToUserMessage(mode.Name));
    Assert(policy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc) == originalDisplayDc,
        "バッテリーなしPCでDC画面OFF時間が変更されました。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc) == originalSleepDc,
        "バッテリーなしPCでDCスリープ時間が変更されました。");
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

    public void SetValue(Guid settingId, PowerSource source, uint value) =>
        _values[(settingId, source)] = value;
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

sealed class FakeMicrophoneMuteAccessor : IMicrophoneMuteAccessor
{
    public bool Muted { get; private set; }
    public int GetCount { get; private set; }
    public int SetCount { get; private set; }
    public bool FailNextVerification { get; set; }
    private bool HasSetSinceLastRead { get; set; }

    public OperationResult<bool> GetMuted()
    {
        GetCount++;
        if (FailNextVerification && HasSetSinceLastRead)
        {
            FailNextVerification = false;
            HasSetSinceLastRead = false;
            return OperationResult<bool>.Failure("テスト用の反映確認失敗です。");
        }

        HasSetSinceLastRead = false;
        return OperationResult<bool>.Success(Muted);
    }

    public OperationResult SetMuted(bool muted)
    {
        SetCount++;
        Muted = muted;
        HasSetSinceLastRead = true;
        return OperationResult.Success();
    }
}

sealed class FakeMicrophoneMuteService : IMicrophoneMuteService
{
    public MicrophoneMuteSetting? LastSetting { get; private set; }

    public OperationResult Apply(MicrophoneMuteSetting setting)
    {
        LastSetting = setting;
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
