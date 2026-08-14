using PCModeSwitcher.Models;
using PCModeSwitcher.Services;
using PCModeSwitcher.ViewModels;
using System.Text.Json;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("既定の9モードと5モード表示", TestDefaultModesAsync),
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
    ("通知領域モードの説明と編集時更新", TestTrayModeToolTipAsync),
    ("電源3設定の一括適用", TestModeApplyAsync),
    ("マイクミュート設定の適用と復元", TestMicrophoneMuteAsync),
    ("モードの4設定一括適用", TestModeMicrophoneIntegrationAsync),
    ("画面上部のマイクON・OFF切り替え", TestMicrophoneToggleButtonAsync),
    ("マイク失敗時も現在モードを実設定から更新", TestModeDetectionAfterMicrophoneFailureAsync),
    ("モード適用結果の表示", TestModeApplyResultDisplayAsync),
    ("途中失敗時の復元と結果表示", TestPartialFailureRollbackAsync)
    ,("モードを削除せず画面から非表示", TestHideModeAsync)
    ,("複製・新規モードだけを完全削除", TestDeleteAddedModeAsync)
    ,("5モード表示順の並べ替え保存", TestVisibleModeReorderAsync)
    ,("追加モードへ午〜亥アイコンを順番に割り当て", TestAdditionalCustomIconsAsync)
    ,("トランザクション適用と逆順復元", TestTransactionalModeEngineAsync)
    ,("破損JSONの退避", TestCorruptedSettingsQuarantineAsync)
    ,("動的モードのエクスポートとインポート", TestProfileExportImportAsync)
    ,("元に戻すショートカットの重複検出", TestRestoreHotkeyConflictAsync),
    ("英語・繁体字と既定言語の保存", TestLocalizationAsync)
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
    Assert(settings.Modes.Count == 9, "モード数が9ではありません。");
    Assert(settings.Modes.Select(mode => mode.Id).SequenceEqual(
            ["game", "work", "normal", "custom1", "custom2", "custom3", "custom4", "custom5", "custom6"]),
        "既定モードの並びが正しくありません。");
    Assert(settings.Modes[0].DisplayTimeoutAc == 0 && settings.Modes[0].SleepTimeoutAc == 0,
        "GAMEの既定タイムアウトが正しくありません。");
    var game = settings.Modes.Single(mode => mode.Id == "game");
    Assert(game.Power.PowerPlanId == PowerSettingsService.BalancedSchemeId &&
           game.Power.AcPowerMode == WindowsPowerMode.BestPerformance &&
           game.Power.DcPowerMode == WindowsPowerMode.BestPerformance &&
           game.Power.SleepPrevention == SleepPreventionMode.SystemAndDisplay,
        "GAMEの性能優先設定が正しくありません。");
    var work = settings.Modes.Single(mode => mode.Id == "work");
    Assert(work.Power.AcPowerMode == WindowsPowerMode.Balanced &&
           work.Power.DcPowerMode == WindowsPowerMode.BestEfficiency &&
           work.DisplayTimeoutAc == 10 * 60 && work.DisplayTimeoutBattery == 5 * 60 &&
           work.SleepTimeoutAc == 30 * 60 && work.SleepTimeoutBattery == 15 * 60,
        "WORKの作業向け設定が正しくありません。");
    var normal = settings.Modes.Single(mode => mode.Id == "normal");
    Assert(normal.Power.AcPowerMode == WindowsPowerMode.Balanced &&
           normal.Power.DcPowerMode == WindowsPowerMode.BestEfficiency &&
           normal.DisplayTimeoutAc == 5 * 60 && normal.DisplayTimeoutBattery == 3 * 60 &&
           normal.SleepTimeoutAc == 15 * 60 && normal.SleepTimeoutBattery == 10 * 60,
        "NORMALの普段使い設定が正しくありません。");
    Assert(settings.CloseButtonBehavior == CloseButtonBehavior.MinimizeToTray,
        "閉じるボタンの既定動作が通知領域への格納ではありません。");
    Assert(!settings.ShowTrayNotification,
        "通知領域への格納通知が既定で有効になっています。");
    Assert(!settings.StartWithWindows,
        "Windowsログイン時の自動起動が既定で有効になっています。");
    Assert(settings.ShowMicrophoneControls,
        "マイク関連の表示が既定で無効になっています。");
    Assert(settings.Modes.Skip(3).Select(mode => mode.Name)
            .SequenceEqual(["CUSTOM1", "CUSTOM2", "CUSTOM3", "CUSTOM4", "CUSTOM5", "CUSTOM6"]),
        "CUSTOM1〜6の既定名が正しくありません。");
    Assert(settings.Modes.Take(3).Select(mode => mode.Icon).SequenceEqual(["🎮", "💼", "🖥"]),
        "GAME・WORK・NORMALの既定アイコンが変わっています。");
    Assert(settings.Modes.Skip(3).All(mode => mode.Icon.EndsWith('\uFE0E')),
        "CUSTOM1〜6のアイコンがモノクロの文字表示指定になっていません。");
    Assert(settings.Modes.All(mode => mode.MicrophoneMute == MicrophoneMuteSetting.NoChange),
        "マイク設定の既定値が『変更しない』ではありません。");
    Assert(settings.Hotkeys.Count == 9 && settings.Hotkeys.All(hotkey => !hotkey.IsConfigured),
        "ショートカットの既定値が未設定ではありません。");
    Assert(settings.VisibleModeIds.SequenceEqual(["game", "work", "normal", "custom1", "custom2"]),
        "初期表示モードが5個ではありません。");
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
        settings.ShowMicrophoneControls = false;
        settings.Hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        settings.Hotkeys[0].VirtualKey = 0x47;
        settings.VisibleModeIds = ["game", "custom1", "custom3", "custom5"];

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
        Assert(!loaded.ShowMicrophoneControls, "マイク関連の表示設定が保持されていません。");
        Assert(loaded.Hotkeys[0].Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt) &&
               loaded.Hotkeys[0].VirtualKey == 0x47,
            "GAMEショートカットが保持されていません。");
        Assert(loaded.VisibleModeIds.SequenceEqual(["game", "custom1", "custom3", "custom5"]),
            "表示モードの選択が保持されていません。");
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
        var legacyCustom = defaults.Modes.Single(mode => mode.Id == "custom1").Copy();
        legacyCustom.Id = "custom";
        legacyCustom.Name = "CUSTOM";
        legacyCustom.Icon = "⚙";
        legacyCustom.DisplayTimeoutAc = 123;
        var legacyCustomHotkey = new ModeHotkey
        {
            ModeId = "custom",
            Modifiers = HotkeyModifiers.Control,
            VirtualKey = 0x31
        };
        var legacySettings = new
        {
            defaults.Version,
            Modes = defaults.Modes.Take(3).Select(mode => mode.Copy()).Append(legacyCustom).ToList(),
            LastAppliedModeId = "custom",
            defaults.CloseButtonBehavior,
            defaults.ShowTrayNotification,
            Hotkeys = defaults.Hotkeys.Take(3).Select(hotkey => hotkey.Copy()).Append(legacyCustomHotkey).ToList()
        };
        await File.WriteAllTextAsync(
            Path.Combine(testDirectory, "settings.json"),
            JsonSerializer.Serialize(legacySettings));

        var load = await new SettingsService(testDirectory).LoadAsync();
        Assert(load.IsSuccess && load.Value is not null, "旧設定ファイルを読み込めませんでした。");
        var migrated = load.Value ?? throw new InvalidOperationException("移行後の設定データがありません。");
        Assert(!migrated.StartWithWindows, "移行後の自動起動設定が既定値ではありません。");
        Assert(migrated.ShowMicrophoneControls,
            "旧設定からの移行でマイク関連の表示が既定のONになっていません。");
        Assert(migrated.Modes.Select(mode => mode.Id).SequenceEqual(
                ["game", "work", "normal", "custom1", "custom2", "custom3", "custom4", "custom5", "custom6"]),
            "旧設定をCUSTOM1〜6へ移行できませんでした。");
        Assert(migrated.Modes.Single(mode => mode.Id == "custom1").DisplayTimeoutAc == 123,
            "旧CUSTOMの設定値がCUSTOM1へ引き継がれませんでした。");
        Assert(migrated.LastAppliedModeId == "custom1",
            "最後に適用した旧CUSTOMがCUSTOM1へ移行されませんでした。");
        Assert(migrated.Hotkeys.Select(hotkey => hotkey.ModeId)
                .SequenceEqual(["game", "work", "normal", "custom1", "custom2", "custom3", "custom4", "custom5", "custom6"]),
            "旧設定を9モードのショートカット設定へ移行できませんでした。");
        Assert(migrated.Hotkeys.Single(hotkey => hotkey.ModeId == "custom1").IsConfigured,
            "旧CUSTOMのショートカットがCUSTOM1へ引き継がれませんでした。");
        Assert(migrated.VisibleModeIds.SequenceEqual(["game", "work", "normal", "custom1", "custom2"]),
            "旧設定の初期表示が5モードで補完されませんでした。");
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
        Assert(viewModel.VisibleModes.Count == SettingsService.MaximumVisibleModeCount,
            "起動時の表示モード数が5個ではありません。");

        var applyResult = await viewModel.ApplyModeByIdAsync("work");
        Assert(applyResult?.IsSuccess == true, "通知領域用のモード適用に失敗しました。");
        Assert(applyResult?.Steps.Single(step => step.Name == "マイク").IsSkipped == true,
            "『変更しない』のマイク設定がスキップ表示になっていません。");
        Assert(viewModel.CurrentModeId == "work" && viewModel.CurrentModeName == "WORK",
            "通知領域用のモード適用後に現在のモードが更新されていません。");

        var customApplyResult = await viewModel.ApplyModeByIdAsync(MainViewModel.CustomModeId);
        Assert(customApplyResult?.IsSuccess == true, "CUSTOMモードの適用に失敗しました。");
        Assert(viewModel.CurrentModeId == MainViewModel.CustomModeId &&
               viewModel.CurrentModeName == "CUSTOM1",
            "CUSTOM1モード適用後に現在のモードが更新されていません。");

        var hotkeys = SettingsService.CreateDefaultHotkeys();
        hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        hotkeys[0].VirtualKey = 0x47;
        var save = await viewModel.SetAppPreferencesAsync(
            CloseButtonBehavior.MinimizeToTray,
            false,
            true,
            hotkeys,
            ["game", "normal", "custom2", "custom4", "custom6"],
            false);
        Assert(save.IsSuccess, $"アプリ設定を保存できませんでした: {save.UserMessage}");
        Assert(startup.Enabled, "スタートアップ登録が有効になっていません。");
        Assert(hotkeyService.Bindings.Single(hotkey => hotkey.ModeId == "game").IsConfigured,
            "グローバルショートカットが登録されていません。");
        Assert(viewModel.VisibleModes.Select(mode => mode.Mode.Id)
                .SequenceEqual(["game", "normal", "custom2", "custom4", "custom6"]),
            "表示モードの選択が画面へ反映されていません。");
        Assert(!viewModel.ShowMicrophoneControls &&
               viewModel.Modes.All(mode => !mode.ShowMicrophoneControls) &&
               viewModel.IsMicrophoneOn is null,
            "マイク関連の表示を非表示にできませんでした。");

        var microphoneApplyCount = microphoneMuteService.ApplyCount;
        var microphoneGetCount = microphoneMuteService.GetCount;
        var hiddenMicrophoneResult = await viewModel.ApplyModeByIdAsync("game");
        Assert(hiddenMicrophoneResult?.Steps.All(step => step.Name != "マイク") == true,
            "非表示中の適用結果にマイク項目が表示されています。");
        Assert(microphoneMuteService.ApplyCount == microphoneApplyCount &&
               microphoneMuteService.GetCount == microphoneGetCount,
            "非表示中にマイクの確認または変更が行われました。");

        var tooManyVisibleModes = await viewModel.SetAppPreferencesAsync(
            CloseButtonBehavior.MinimizeToTray,
            false,
            true,
            hotkeys,
            ["game", "work", "normal", "custom1", "custom2", "custom3"]);
        Assert(!tooManyVisibleModes.IsSuccess,
            "6個のモードをアプリ画面へ表示できてしまいました。");
        Assert(viewModel.VisibleModes.Select(mode => mode.Mode.Id)
                .SequenceEqual(["game", "normal", "custom2", "custom4", "custom6"]),
            "不正な表示モード設定で保存済みの選択が変わりました。");

        var loaded = await new SettingsService(testDirectory).LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.StartWithWindows == true,
            "連携したアプリ設定がファイルへ保存されていません。");
        Assert(loaded.Value?.VisibleModeIds.SequenceEqual(
                ["game", "normal", "custom2", "custom4", "custom6"]) == true,
            "連携した表示モード設定がファイルへ保存されていません。");
        Assert(loaded.Value?.ShowMicrophoneControls == false,
            "マイク関連の非表示設定がファイルへ保存されていません。");

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
    var initialState = service.GetCurrentMuted();
    Assert(initialState.IsSuccess && !initialState.Value,
        "現在のミュート解除状態を取得できませんでした。");

    var mute = service.Apply(MicrophoneMuteSetting.Mute);
    Assert(mute.IsSuccess && accessor.Muted, "マイクをミュートできませんでした。");
    var mutedState = service.GetCurrentMuted();
    Assert(mutedState.IsSuccess && mutedState.Value,
        "現在のミュート状態を取得できませんでした。");

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
               result.Steps.Single(step => step.Name == "マイク") is
                   { IsSuccess: true, IsSkipped: false, DisplayName: "マイク：OFF" },
            "モード適用結果にマイクが含まれていません。");
        Assert(viewModel.StatusMessage.Contains("✓ マイク：OFF", StringComparison.Ordinal),
            "成功時のマイク設定が結果表示で分かりません。");
        Assert(microphoneMuteService.LastSetting == MicrophoneMuteSetting.Mute,
            "モードに保存したマイク設定が適用されていません。");

        microphoneMuteService.CurrentMuted = true;
        var noChangeResult = await viewModel.ApplyModeByIdAsync("normal")
            ?? throw new InvalidOperationException("NORMALモードの適用結果がありません。");
        Assert(noChangeResult.IsSuccess && viewModel.StatusMessage.Contains(
                "– マイク：変更しない（現在：OFF）", StringComparison.Ordinal),
            "変更しない場合に現在のマイク状態が表示されていません。");

        microphoneMuteService.CurrentStateResult =
            OperationResult<bool>.Failure("テスト用の読み取り失敗です。");
        var unknownStateResult = await viewModel.ApplyModeByIdAsync("normal")
            ?? throw new InvalidOperationException("NORMALモードの再適用結果がありません。");
        Assert(unknownStateResult.IsSuccess && viewModel.StatusMessage.Contains(
                "– マイク：変更しない（現在状態を確認できません）", StringComparison.Ordinal),
            "マイクがない場合の現在状態が分かる表示になっていません。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static async Task TestMicrophoneToggleButtonAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.Tests.{Guid.NewGuid():N}");
    try
    {
        var planId = Guid.NewGuid();
        var microphone = new FakeMicrophoneMuteService();
        var viewModel = new MainViewModel(
            new SettingsService(testDirectory),
            new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => false),
            microphone,
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        Assert(viewModel.IsMicrophoneOn == true && viewModel.MicrophoneButtonText == "マイク ON",
            "起動時のマイクON状態がボタンへ表示されていません。");

        microphone.CurrentMuted = true;
        viewModel.RefreshMicrophoneState();
        Assert(viewModel.IsMicrophoneOn == false && viewModel.MicrophoneButtonText == "マイク OFF",
            "Windows側で変更されたマイク状態がボタンへ反映されていません。");

        // 表示を更新せず実状態だけ変え、クリック時に実状態を読み直すことを確認する。
        microphone.CurrentMuted = false;
        await viewModel.ToggleMicrophoneAsync();
        Assert(microphone.LastSetting == MicrophoneMuteSetting.Mute &&
               microphone.CurrentMuted && viewModel.IsMicrophoneOn == false,
            "古い表示ではなく実際の状態からマイクをOFFにできませんでした。");

        await viewModel.ToggleMicrophoneAsync();
        Assert(microphone.LastSetting == MicrophoneMuteSetting.Unmute &&
               !microphone.CurrentMuted && viewModel.IsMicrophoneOn == true,
            "マイクをONへ戻せませんでした。");

        microphone.CurrentStateResult =
            OperationResult<bool>.Failure("テスト用の読み取り失敗です。");
        var previousSetting = microphone.LastSetting;
        await viewModel.ToggleMicrophoneAsync();
        Assert(viewModel.IsMicrophoneOn is null && viewModel.MicrophoneButtonText == "マイク ?",
            "状態を確認できないマイクが不明表示になっていません。");
        Assert(microphone.LastSetting == previousSetting,
            "状態を確認できないのにマイクを変更しました。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static async Task TestModeDetectionAfterMicrophoneFailureAsync()
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
        Assert((await settingsService.SaveAsync(settings)).IsSuccess,
            "一部失敗テスト用の設定を保存できませんでした。");

        var microphone = new FakeMicrophoneMuteService
        {
            NextApplyResult = OperationResult.Failure("テスト用のマイク適用失敗です。")
        };
        var viewModel = new MainViewModel(
            settingsService,
            new PowerSettingsService(new FakePowerPolicyAccessor(planId), () => true),
            microphone,
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var result = await viewModel.ApplyModeByIdAsync("game")
            ?? throw new InvalidOperationException("GAMEモードの適用結果がありません。");

        Assert(!result.IsSuccess && result.Steps.Single(step => step.Name == "マイク").IsSuccess == false,
            "マイクの失敗が一部失敗として扱われていません。");
        Assert(viewModel.CurrentModeId == "game" && viewModel.CurrentModeName == "GAME",
            "マイクだけ失敗した後に、反映済みの現在モードへ表示が更新されていません。");
        Assert(viewModel.StatusMessage.Contains("一部適用しました", StringComparison.Ordinal),
            "一部適用の結果表示が保持されていません。");

        var loaded = await settingsService.LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.LastAppliedModeId == "game",
            "実設定と一致した一部適用モードが次回判定用に保存されていません。");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
            Directory.Delete(testDirectory, true);
    }
}

static Task TestModeApplyResultDisplayAsync()
{
    var result = new ModeApplyResult
    {
        Steps =
        [
            new ApplyStepResult("電源モード", true, "変更しました。"),
            new ApplyStepResult("マイク", true, "変更しませんでした。", IsSkipped: true,
                DisplayName: "マイク：変更しない（現在：ON）"),
            new ApplyStepResult("マイク", false, "既定のマイクのミュート状態を確認できませんでした。",
                DisplayName: "マイク：OFF")
        ]
    };

    var message = result.ToUserMessage("テスト");
    Assert(message.Contains("✓ 電源モード", StringComparison.Ordinal),
        "成功した項目の表示が正しくありません。");
    Assert(message.Contains("– マイク：変更しない（現在：ON）", StringComparison.Ordinal),
        "変更しない場合のマイク設定が表示されていません。");
    Assert(message.Contains(
            "⚠ マイク：OFF（既定のマイクのミュート状態を確認できませんでした）",
            StringComparison.Ordinal),
        "失敗時のマイク設定と理由が表示されていません。");
    return Task.CompletedTask;
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
    Assert(desktopCard.DisplaySummary == $"電源接続時 {ModeCardViewModel.FormatTimeout(mode.DisplayTimeoutAc)}",
        "バッテリーなしPCの画面OFF概要が電源接続時の設定だけになっていません。");
    Assert(desktopCard.SleepSummary == $"電源接続時 {ModeCardViewModel.FormatTimeout(mode.SleepTimeoutAc)}",
        "バッテリーなしPCのスリープ概要が電源接続時の設定だけになっていません。");

    var result = await desktopService.ApplyModeAsync(mode);
    Assert(result.IsSuccess, result.ToUserMessage(mode.Name));
    Assert(policy.GetValue(PowerSettingsService.DisplayTimeoutId, PowerSource.Dc) == originalDisplayDc,
        "バッテリーなしPCでDC画面OFF時間が変更されました。");
    Assert(policy.GetValue(PowerSettingsService.SleepTimeoutId, PowerSource.Dc) == originalSleepDc,
        "バッテリーなしPCでDCスリープ時間が変更されました。");
}

static Task TestTrayModeToolTipAsync()
{
    var mode = CreateTestMode(Guid.NewGuid());
    var planName = "テストプラン";
    var card = new ModeCardViewModel(mode, _ => planName, true);
    var expected = string.Join(
        Environment.NewLine,
        "画面OFF: 電源接続時 5分 / バッテリー 2分",
        "スリープ: 電源接続時 15分 / バッテリー 10分",
        "電源モード: テストプラン",
        "マイク設定（適用時）: 変更しない");
    Assert(card.TrayToolTipText == expected,
        "通知領域のモード説明に4設定が正しく表示されていません。");

    var toolTipChanged = false;
    card.PropertyChanged += (_, args) =>
    {
        if (args.PropertyName == nameof(ModeCardViewModel.TrayToolTipText))
            toolTipChanged = true;
    };

    var editedMode = mode.Copy();
    editedMode.MicrophoneMute = MicrophoneMuteSetting.Mute;
    card.Replace(editedMode);
    Assert(toolTipChanged && card.TrayToolTipText.EndsWith(
            "マイク設定（適用時）: OFF", StringComparison.Ordinal),
        "モード編集後に通知領域の説明が更新されていません。");

    toolTipChanged = false;
    planName = "高パフォーマンス";
    card.RefreshPlanName();
    Assert(toolTipChanged && card.TrayToolTipText.Contains(
            "電源モード: 高パフォーマンス", StringComparison.Ordinal),
        "電源プラン名の更新が通知領域の説明へ反映されていません。");

    card.ShowMicrophoneControls = false;
    Assert(!card.TrayToolTipText.Contains("マイク", StringComparison.Ordinal),
        "マイク関連を非表示にしても通知領域の説明へ残っています。");
    return Task.CompletedTask;
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

static async Task TestVisibleModeReorderAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.ReorderModeTests.{Guid.NewGuid():N}");
    try
    {
        var service = new SettingsService(directory);
        var viewModel = new MainViewModel(
            service,
            new PowerSettingsService(new FakePowerPolicyAccessor(PowerSettingsService.BalancedSchemeId), () => false),
            new FakeMicrophoneMuteService(),
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var reorder = await viewModel.ReorderVisibleModeAsync("custom1", "custom2", true);
        Assert(reorder.IsSuccess, $"5モード表示時に並べ替えできませんでした: {reorder.UserMessage}");
        var expected = new[] { "game", "work", "normal", "custom2", "custom1" };
        Assert(viewModel.VisibleModeIds.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase) &&
               viewModel.VisibleModes.Select(card => card.Mode.Id).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
            "並べ替えた順序がアプリ画面へ反映されていません。");
        Assert(viewModel.AllProfiles.Select(mode => mode.Id).Take(5).SequenceEqual(
                new[] { "game", "work", "normal", "custom1", "custom2" },
                StringComparer.OrdinalIgnoreCase),
            "表示順の変更でモード本体の登録順まで変わりました。");

        var loaded = await service.LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.VisibleModeIds.SequenceEqual(
                expected,
                StringComparer.OrdinalIgnoreCase) == true,
            "並べ替えた表示順が設定へ保存されていません。");

        var restore = await viewModel.ReorderVisibleModeAsync("custom1", "custom2", false);
        Assert(restore.IsSuccess && viewModel.VisibleModeIds.SequenceEqual(
                new[] { "game", "work", "normal", "custom1", "custom2" },
                StringComparer.OrdinalIgnoreCase),
            "Custom1をCustom2の前へ戻せませんでした。");
        Assert(!(await viewModel.ReorderVisibleModeAsync("custom3", "game", false)).IsSuccess,
            "画面にないモードを表示順へ混ぜられてしまいました。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestHideModeAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.HideModeTests.{Guid.NewGuid():N}");
    try
    {
        var service = new SettingsService(directory);
        var viewModel = new MainViewModel(
            service,
            new PowerSettingsService(new FakePowerPolicyAccessor(PowerSettingsService.BalancedSchemeId), () => false),
            new FakeMicrophoneMuteService(),
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var hide = await viewModel.HideModeAsync("work");
        Assert(hide.IsSuccess, $"WORKを非表示にできませんでした: {hide.UserMessage}");
        Assert(viewModel.Modes.Any(mode => mode.Mode.Id == "work") &&
               viewModel.AllProfiles.Any(mode => mode.Id == "work"),
            "非表示操作でWORKのモード設定が削除されました。");
        Assert(viewModel.VisibleModes.All(mode => mode.Mode.Id != "work"),
            "非表示にしたWORKがアプリ画面へ残っています。");

        var loaded = await service.LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.Modes.Any(mode => mode.Id == "work") == true,
            "非表示後の保存データからWORKが削除されました。");
        Assert(loaded.Value?.VisibleModeIds.Contains("work", StringComparer.OrdinalIgnoreCase) == false,
            "非表示にしたWORKが表示対象として保存されています。");

        foreach (var modeId in new[] { "game", "normal", "custom1" })
            Assert((await viewModel.HideModeAsync(modeId)).IsSuccess, $"{modeId}を非表示にできませんでした。");
        Assert(!(await viewModel.HideModeAsync("custom2")).IsSuccess,
            "最後の表示モードまで非表示にできてしまいました。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestDeleteAddedModeAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.DeleteAddedModeTests.{Guid.NewGuid():N}");
    try
    {
        var service = new SettingsService(directory);
        var viewModel = new MainViewModel(
            service,
            new PowerSettingsService(new FakePowerPolicyAccessor(PowerSettingsService.BalancedSchemeId), () => false),
            new FakeMicrophoneMuteService(),
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var duplicate = await viewModel.DuplicateModeAsync("game");
        Assert(duplicate.IsSuccess, $"モードを複製できませんでした: {duplicate.UserMessage}");
        var addedMode = viewModel.AllProfiles.Single(mode => !SettingsService.IsBuiltInModeId(mode.Id));

        var rejectBuiltIn = await viewModel.SetAppPreferencesAsync(
            viewModel.CloseButtonBehavior,
            viewModel.ShowTrayNotification,
            viewModel.StartWithWindows,
            viewModel.Hotkeys,
            viewModel.VisibleModeIds,
            viewModel.ShowMicrophoneControls,
            viewModel.RestoreHotkey,
            viewModel.AllProfiles.Where(mode => mode.IsEnabled).Select(mode => mode.Id).ToList(),
            ["game"]);
        Assert(!rejectBuiltIn.IsSuccess && viewModel.AllProfiles.Any(mode => mode.Id == "game"),
            "標準モードを完全削除できてしまいました。");

        var delete = await viewModel.SetAppPreferencesAsync(
            viewModel.CloseButtonBehavior,
            viewModel.ShowTrayNotification,
            viewModel.StartWithWindows,
            viewModel.Hotkeys.Where(hotkey => !string.Equals(
                hotkey.ModeId, addedMode.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
            viewModel.VisibleModeIds.Where(id => !string.Equals(
                id, addedMode.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
            viewModel.ShowMicrophoneControls,
            viewModel.RestoreHotkey,
            viewModel.AllProfiles.Where(mode => mode.IsEnabled && !string.Equals(
                mode.Id, addedMode.Id, StringComparison.OrdinalIgnoreCase)).Select(mode => mode.Id).ToList(),
            [addedMode.Id]);
        Assert(delete.IsSuccess, $"追加モードを完全削除できませんでした: {delete.UserMessage}");
        Assert(viewModel.AllProfiles.All(mode => !string.Equals(
                   mode.Id, addedMode.Id, StringComparison.OrdinalIgnoreCase)) &&
               viewModel.Hotkeys.All(hotkey => !string.Equals(
                   hotkey.ModeId, addedMode.Id, StringComparison.OrdinalIgnoreCase)),
            "追加モード本体またはショートカットが残っています。");

        var loaded = await service.LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value is not null &&
               loaded.Value.Modes.All(mode => !string.Equals(
                   mode.Id, addedMode.Id, StringComparison.OrdinalIgnoreCase)) &&
               loaded.Value.Hotkeys.All(hotkey => !string.Equals(
                   hotkey.ModeId, addedMode.Id, StringComparison.OrdinalIgnoreCase)),
            "保存データに完全削除した追加モードが残っています。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
static async Task TestAdditionalCustomIconsAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.CustomIconTests.{Guid.NewGuid():N}");
    try
    {
        var viewModel = new MainViewModel(
            new SettingsService(directory),
            new PowerSettingsService(new FakePowerPolicyAccessor(PowerSettingsService.BalancedSchemeId), () => false),
            new FakeMicrophoneMuteService(),
            new FakeStartupService(),
            new FakeGlobalHotkeyService());
        await viewModel.InitializeAsync();

        var expected = new (string Id, string Icon)[]
        {
            ("custom7", "\U0001F434\uFE0E"),
            ("custom8", "\U0001F411\uFE0E"),
            ("custom9", "\U0001F412\uFE0E"),
            ("custom10", "\U0001F413\uFE0E"),
            ("custom11", "\U0001F415\uFE0E"),
            ("custom12", "\U0001F417\uFE0E")
        };
        foreach (var item in expected)
        {
            var mode = viewModel.CreateNewMode();
            Assert(mode.Id == item.Id && mode.Icon == item.Icon,
                $"追加モードへ{item.Id}の十二支アイコンが割り当てられませんでした。");
            Assert(ModeIconAssets.HasCustomIcon(mode.Id) &&
                   ModeIconAssets.GetCustomIconSource(mode.Id)?.EndsWith($"{char.ToUpperInvariant(item.Id[0])}{item.Id[1..]}Icon.png", StringComparison.Ordinal) == true,
                $"{item.Id}の画像素材が登録されていません。");
            mode.Name = item.Id.ToUpperInvariant();
            Assert((await viewModel.AddModeAsync(mode)).IsSuccess, $"{item.Id}を追加できませんでした。");
        }

        var fallback = viewModel.CreateNewMode();
        Assert(fallback.Id.StartsWith("user-", StringComparison.Ordinal) && fallback.Icon == "●",
            "13個目以降のモードが汎用アイコンへフォールバックしませんでした。");
        Assert(!ModeIconAssets.HasCustomIcon(fallback.Id),
            "汎用モードへ存在しない専用画像が割り当てられました。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task TestTransactionalModeEngineAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.EngineTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var calls = new List<string>();
        var handlers = new IModeActionHandler[]
        {
            new FakeModeActionHandler("first", calls),
            new FakeModeActionHandler("second", calls, failApply: true),
            new FakeModeActionHandler("third", calls)
        };
        var paths = new AppPaths(directory);
        using var engine = new ModeEngine(
            handlers,
            new SessionStore(paths),
            new AppLogger(paths),
            new ProcessMonitorService());
        var mode = SettingsService.CreateUserMode("テスト");
        var apply = await engine.ApplyAsync(mode);
        Assert(!apply.IsSuccess, "一部失敗が成功として扱われました。");
        Assert(calls.Where(value => value.StartsWith("apply:", StringComparison.Ordinal))
                .SequenceEqual(["apply:first", "apply:second", "apply:third"]),
            "一つの適用失敗後に残りのアクションが続行されていません。");
        Assert(File.Exists(paths.ActiveSessionPath), "適用後のactive-session.jsonがありません。");
        var persisted = await new SessionStore(paths).LoadAsync();
        Assert(persisted.IsSuccess && persisted.Value?.Actions.Count == 3 &&
               persisted.Value.Actions.All(action => action.StateCaptured),
            "アクションスナップショットが保存されていません。");

        var restore = await engine.RestoreAsync();
        Assert(restore.IsSuccess, "復元に失敗しました。");
        Assert(calls.Where(value => value.StartsWith("restore:", StringComparison.Ordinal))
                .SequenceEqual(["restore:third", "restore:second", "restore:first"]),
            "アクションが逆順に復元されていません。");
        Assert(!File.Exists(paths.ActiveSessionPath), "正常復元後にactive-session.jsonが残っています。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestCorruptedSettingsQuarantineAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.CorruptTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var settingsPath = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{ this is not json");
        var result = await new SettingsService(directory).LoadAsync();
        Assert(!result.IsSuccess, "破損JSONが正常な設定として読み込まれました。");
        Assert(!File.Exists(settingsPath), "破損JSONが元のファイル名のまま残っています。");
        Assert(Directory.EnumerateFiles(Path.Combine(directory, "Backups"), "corrupt-settings-*.json").Count() == 1,
            "破損JSONがバックアップフォルダーへ退避されていません。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static async Task TestProfileExportImportAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher.ImportTests.{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var service = new SettingsService(directory);
        var settings = SettingsService.CreateDefaults();
        var custom = SettingsService.CreateUserMode("配信用");
        custom.Audio.Output.VolumePercent = 35;
        settings.Modes.Add(custom);
        settings.Hotkeys.Add(new ModeHotkey { ModeId = custom.Id });
        var exportPath = Path.Combine(directory, "profiles.json");
        Assert((await service.ExportProfilesAsync(exportPath, settings)).IsSuccess,
            "プロファイルをエクスポートできませんでした。");
        var imported = await service.ImportProfilesAsync(exportPath, SettingsService.CreateDefaults());
        Assert(imported.IsSuccess && imported.Value is not null, "プロファイルをインポートできませんでした。");
        var importedSettings = imported.Value ?? throw new InvalidOperationException("インポート結果がありません。");
        var restored = importedSettings.Modes.Single(mode => mode.Name == "配信用");
        Assert(restored.Audio.Output.VolumePercent == 35 && restored.Id == custom.Id,
            "動的モードの構造化設定がインポートで保持されていません。");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static Task TestRestoreHotkeyConflictAsync()
{
    var hotkeys = SettingsService.CreateDefaultHotkeys();
    hotkeys[0].Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    hotkeys[0].VirtualKey = 0x47;
    var restore = new ModeHotkey
    {
        ModeId = "restore",
        Modifiers = hotkeys[0].Modifiers,
        VirtualKey = hotkeys[0].VirtualKey
    };
    Assert(!HotkeyValidator.Validate(hotkeys.Append(restore).ToList()).IsSuccess,
        "モードと元に戻すへ同じショートカットを割り当てられました。");
    restore.VirtualKey = 0x7B;
    Assert(!HotkeyValidator.Validate(hotkeys.Append(restore).ToList()).IsSuccess,
        "元に戻すへF12を割り当てられました。");
    return Task.CompletedTask;
}

static async Task TestLocalizationAsync()
{
    var directory = Path.Combine(Path.GetTempPath(), $"PCModeSwitcher-localization-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var settings = SettingsService.CreateDefaults();
        Assert(settings.Language == AppLanguages.System,
            "新規設定の表示言語が既定値ではありません。");
        settings.Language = AppLanguages.TraditionalChinese;
        var service = new SettingsService(directory);
        Assert((await service.SaveAsync(settings)).IsSuccess,
            "繁体字設定を保存できませんでした。");
        var loaded = await service.LoadAsync();
        Assert(loaded.IsSuccess && loaded.Value?.Language == AppLanguages.TraditionalChinese,
            "表示言語が再読み込み後に保持されていません。");

        LocalizationService.SetLanguage(AppLanguages.English);
        Assert(LocalizationService.Get("Settings.Title") == "App settings",
            "英語UIを読み込めませんでした。");
        Assert(OperationResult.Failure("モード設定を保存できませんでした。").UserMessage ==
               "Mode settings could not be saved.",
            "英語の結果メッセージへ切り替わりませんでした。");
        var card = new ModeCardViewModel(
            settings.Modes[0], _ => "Balanced", hasBattery: false);
        Assert(card.DisplaySummary.StartsWith("Plugged in", StringComparison.Ordinal),
            "英語のカード要約へ切り替わりませんでした。");

        LocalizationService.SetLanguage(AppLanguages.TraditionalChinese);
        Assert(LocalizationService.Get("Settings.Title") == "應用程式設定",
            "繁体字UIを読み込めませんでした。");
        Assert(card.DisplaySummary.StartsWith("插入電源", StringComparison.Ordinal),
            "繁体字のカード要約へ切り替わりませんでした。");
        Assert(LocalizationService.Normalize("invalid") == AppLanguages.System,
            "不正な表示言語が既定値へ戻りませんでした。");
    }
    finally
    {
        LocalizationService.SetLanguage(AppLanguages.Japanese);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

sealed class FakeModeActionHandler(
    string id,
    List<string> calls,
    bool failApply = false) : IModeActionHandler
{
    public string Id => id;
    public string DisplayName => id;
    public Task<ActionPreflightResult> PreflightAsync(ModeActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ActionPreflightResult.Ready());
    public Task<ActionCaptureResult> CaptureAsync(ModeActionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ActionCaptureResult.Success(new { id }));
    public Task<ActionExecutionResult> ApplyAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        calls.Add($"apply:{id}");
        return Task.FromResult(new ActionExecutionResult
        {
            ActionId = id, DisplayName = id,
            Status = failApply ? ActionExecutionStatus.ApplyFailed : ActionExecutionStatus.Succeeded,
            Message = failApply ? "テスト用失敗" : "成功"
        });
    }
    public Task<ActionExecutionResult> RestoreAsync(ModeActionContext context, ActionSnapshot snapshot, CancellationToken cancellationToken)
    {
        calls.Add($"restore:{id}");
        return Task.FromResult(new ActionExecutionResult
        {
            ActionId = id, DisplayName = id,
            Status = ActionExecutionStatus.RestoreSucceeded,
            Message = "復元成功"
        });
    }
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
    public int ApplyCount { get; private set; }
    public int GetCount { get; private set; }
    public bool CurrentMuted { get; set; }
    public OperationResult<bool>? CurrentStateResult { get; set; }
    public OperationResult? NextApplyResult { get; set; }

    public OperationResult Apply(MicrophoneMuteSetting setting)
    {
        ApplyCount++;
        LastSetting = setting;
        if (NextApplyResult is { } nextResult)
        {
            NextApplyResult = null;
            return nextResult;
        }

        if (setting == MicrophoneMuteSetting.Mute)
            CurrentMuted = true;
        else if (setting == MicrophoneMuteSetting.Unmute)
            CurrentMuted = false;
        return OperationResult.Success();
    }

    public OperationResult<bool> GetCurrentMuted()
    {
        GetCount++;
        return CurrentStateResult ?? OperationResult<bool>.Success(CurrentMuted);
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
