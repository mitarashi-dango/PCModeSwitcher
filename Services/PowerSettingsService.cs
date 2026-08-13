using System.Runtime.InteropServices;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class PowerSettingsService
{
    public const uint MaximumTimeoutSeconds = 30u * 24u * 60u * 60u;

    internal static readonly Guid BalancedSchemeId = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    internal static readonly Guid HighPerformanceSchemeId = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    internal static readonly Guid VideoSubgroupId = new("7516b95f-f776-4464-8c53-06167f40cc99");
    internal static readonly Guid DisplayTimeoutId = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    internal static readonly Guid SleepSubgroupId = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    internal static readonly Guid SleepTimeoutId = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private readonly IPowerPolicyAccessor _policy;
    private readonly Func<bool> _hasBattery;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public PowerSettingsService() : this(new PowerPolicyAccessor(), DetectBattery) { }

    internal PowerSettingsService(IPowerPolicyAccessor policy, Func<bool>? hasBattery = null)
    {
        _policy = policy;
        _hasBattery = hasBattery ?? (() => false);
    }

    public bool HasBattery => _hasBattery();

    public async Task<OperationResult<IReadOnlyList<PowerPlan>>> GetAvailablePlansAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            return _policy.GetSchemes();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<OperationResult<ModeDetectionResult>> DetectCurrentModeAsync(
        IReadOnlyCollection<PcMode> modes,
        string? preferredModeId = null)
    {
        await _operationGate.WaitAsync();
        try
        {
            var activeScheme = _policy.GetActiveScheme();
            if (!activeScheme.IsSuccess)
            {
                return OperationResult<ModeDetectionResult>.Failure(
                    "現在のWindows電源設定を確認できませんでした。",
                    activeScheme.TechnicalDetails);
            }

            var displayAc = _policy.ReadValue(
                activeScheme.Value, VideoSubgroupId, DisplayTimeoutId, PowerSource.Ac);
            var sleepAc = _policy.ReadValue(
                activeScheme.Value, SleepSubgroupId, SleepTimeoutId, PowerSource.Ac);
            if (!displayAc.IsSuccess || !sleepAc.IsSuccess)
            {
                return OperationResult<ModeDetectionResult>.Failure(
                    "現在のWindows電源設定を確認できませんでした。",
                    CombineTechnicalDetails(displayAc, sleepAc));
            }

            var hasBattery = HasBattery;
            OperationResult<uint>? displayDc = null;
            OperationResult<uint>? sleepDc = null;
            if (hasBattery)
            {
                displayDc = _policy.ReadValue(
                    activeScheme.Value, VideoSubgroupId, DisplayTimeoutId, PowerSource.Dc);
                sleepDc = _policy.ReadValue(
                    activeScheme.Value, SleepSubgroupId, SleepTimeoutId, PowerSource.Dc);
                if (!displayDc.IsSuccess || !sleepDc.IsSuccess)
                {
                    return OperationResult<ModeDetectionResult>.Failure(
                        "現在のWindows電源設定を確認できませんでした。",
                        CombineTechnicalDetails(displayDc, sleepDc));
                }
            }

            var matches = modes.Where(mode =>
                mode.PowerPlanId == activeScheme.Value &&
                mode.DisplayTimeoutAc == displayAc.Value &&
                mode.SleepTimeoutAc == sleepAc.Value &&
                (!hasBattery ||
                    (mode.DisplayTimeoutBattery == displayDc!.Value &&
                     mode.SleepTimeoutBattery == sleepDc!.Value)))
                .ToList();
            var match = matches.FirstOrDefault(mode =>
                    string.Equals(mode.Id, preferredModeId, StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault();

            return OperationResult<ModeDetectionResult>.Success(
                new ModeDetectionResult(match?.Id));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ModeApplyResult> ApplyModeAsync(PcMode mode)
    {
        await _operationGate.WaitAsync();
        try
        {
            var plan = ApplyPowerPlan(mode.PowerPlanId);
            if (!plan.IsSuccess)
            {
                return new ModeApplyResult
                {
                    Steps =
                    [
                        new ApplyStepResult("電源モード", false, plan.UserMessage, plan.TechnicalDetails),
                        new ApplyStepResult("画面OFF", false, "電源モードを切り替えられなかったため変更していません。"),
                        new ApplyStepResult("スリープ", false, "電源モードを切り替えられなかったため変更していません。")
                    ]
                };
            }

            var hasBattery = HasBattery;
            var display = ApplySettingPair(
                mode.PowerPlanId,
                VideoSubgroupId,
                DisplayTimeoutId,
                mode.DisplayTimeoutAc,
                mode.DisplayTimeoutBattery,
                hasBattery,
                "画面OFF");
            var sleep = ApplySettingPair(
                mode.PowerPlanId,
                SleepSubgroupId,
                SleepTimeoutId,
                mode.SleepTimeoutAc,
                mode.SleepTimeoutBattery,
                hasBattery,
                "スリープ");

            return new ModeApplyResult
            {
                Steps =
                [
                    new ApplyStepResult("電源モード", plan.IsSuccess, plan.UserMessage, plan.TechnicalDetails),
                    new ApplyStepResult("画面OFF", display.IsSuccess, display.UserMessage, display.TechnicalDetails),
                    new ApplyStepResult("スリープ", sleep.IsSuccess, sleep.UserMessage, sleep.TechnicalDetails)
                ]
            };
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private OperationResult ApplyPowerPlan(Guid requestedSchemeId)
    {
        if (requestedSchemeId == Guid.Empty)
            return OperationResult.Failure("電源モードが選択されていません。");

        var schemes = _policy.GetSchemes();
        if (!schemes.IsSuccess || schemes.Value is null)
            return OperationResult.Failure(schemes.UserMessage, schemes.TechnicalDetails);
        if (!schemes.Value.Any(plan => plan.Id == requestedSchemeId))
            return OperationResult.Failure("選択した電源モードはこのPCで利用できません。");

        var activate = _policy.ActivateScheme(requestedSchemeId);
        if (!activate.IsSuccess)
            return activate;

        var active = _policy.GetActiveScheme();
        return active.IsSuccess && active.Value == requestedSchemeId
            ? OperationResult.Success("電源モードを切り替えました。")
            : OperationResult.Failure(
                "電源モードの切り替えを確認できませんでした。",
                active.TechnicalDetails);
    }

    private OperationResult ApplySettingPair(
        Guid schemeId,
        Guid subgroupId,
        Guid settingId,
        uint acSeconds,
        uint dcSeconds,
        bool includeBattery,
        string settingName)
    {
        if (acSeconds > MaximumTimeoutSeconds || dcSeconds > MaximumTimeoutSeconds)
            return OperationResult.Failure($"{settingName}時間が正しくありません。");

        var originalAc = _policy.ReadValue(schemeId, subgroupId, settingId, PowerSource.Ac);
        var originalDc = _policy.ReadValue(schemeId, subgroupId, settingId, PowerSource.Dc);
        if (!originalAc.IsSuccess || !originalDc.IsSuccess)
            return OperationResult.Failure(
                $"変更前の{settingName}時間を確認できなかったため変更していません。",
                originalAc.TechnicalDetails ?? originalDc.TechnicalDetails);

        var writeAc = _policy.WriteValue(schemeId, subgroupId, settingId, PowerSource.Ac, acSeconds);
        if (!writeAc.IsSuccess)
            return OperationResult.Failure($"{settingName}時間を変更できませんでした。", writeAc.TechnicalDetails);

        if (includeBattery)
        {
            var writeDc = _policy.WriteValue(schemeId, subgroupId, settingId, PowerSource.Dc, dcSeconds);
            if (!writeDc.IsSuccess)
                return RestorePairAfterFailure(
                    schemeId, subgroupId, settingId, originalAc.Value, originalDc.Value,
                    includeBattery, settingName, writeDc.TechnicalDetails);
        }

        var activation = _policy.ActivateScheme(schemeId);
        if (!activation.IsSuccess)
            return RestorePairAfterFailure(
                schemeId, subgroupId, settingId, originalAc.Value, originalDc.Value,
                includeBattery, settingName, activation.TechnicalDetails);

        var verifyAc = _policy.ReadValue(schemeId, subgroupId, settingId, PowerSource.Ac);
        var verifyDc = _policy.ReadValue(schemeId, subgroupId, settingId, PowerSource.Dc);
        if (!verifyAc.IsSuccess || verifyAc.Value != acSeconds ||
            (includeBattery && (!verifyDc.IsSuccess || verifyDc.Value != dcSeconds)))
        {
            return RestorePairAfterFailure(
                schemeId, subgroupId, settingId, originalAc.Value, originalDc.Value,
                includeBattery, settingName,
                $"AC expected={acSeconds}, actual={verifyAc.Value}; " +
                $"DC expected={dcSeconds}, actual={verifyDc.Value}");
        }

        return OperationResult.Success($"{settingName}時間を変更しました。");
    }

    private OperationResult RestorePairAfterFailure(
        Guid schemeId,
        Guid subgroupId,
        Guid settingId,
        uint originalAc,
        uint originalDc,
        bool includeBattery,
        string settingName,
        string? failureDetails)
    {
        var errors = new List<string>();
        var restoreAc = _policy.WriteValue(
            schemeId, subgroupId, settingId, PowerSource.Ac, originalAc);
        if (!restoreAc.IsSuccess)
            errors.Add(restoreAc.TechnicalDetails ?? restoreAc.UserMessage);

        if (includeBattery)
        {
            var restoreDc = _policy.WriteValue(
                schemeId, subgroupId, settingId, PowerSource.Dc, originalDc);
            if (!restoreDc.IsSuccess)
                errors.Add(restoreDc.TechnicalDetails ?? restoreDc.UserMessage);
        }

        var activate = _policy.ActivateScheme(schemeId);
        if (!activate.IsSuccess)
            errors.Add(activate.TechnicalDetails ?? activate.UserMessage);

        return errors.Count == 0
            ? OperationResult.Failure(
                $"{settingName}時間を変更できなかったため、変更前の値へ戻しました。",
                failureDetails)
            : OperationResult.Failure(
                $"{settingName}時間の変更に失敗し、元の値へ完全には戻せませんでした。Windowsの設定画面で確認してください。",
                string.Join("; ", new[] { failureDetails }.Concat(errors).Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string? CombineTechnicalDetails(params OperationResult<uint>[] results)
    {
        var details = results
            .Where(result => !result.IsSuccess)
            .Select(result => result.TechnicalDetails ?? result.UserMessage)
            .Where(detail => !string.IsNullOrWhiteSpace(detail));
        var combined = string.Join("; ", details);
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    private static bool DetectBattery() =>
        GetSystemPowerStatus(out var status) &&
        status.BatteryFlag != 255 &&
        (status.BatteryFlag & 128) == 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}
