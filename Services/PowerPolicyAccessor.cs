using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

internal enum PowerSource
{
    Ac,
    Dc
}

internal interface IPowerPolicyAccessor
{
    OperationResult<Guid> GetActiveScheme();
    OperationResult<IReadOnlyList<PowerPlan>> GetSchemes();
    OperationResult<uint> ReadValue(Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source);
    OperationResult WriteValue(Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source, uint seconds);
    OperationResult ActivateScheme(Guid schemeId);
}

internal sealed class PowerPolicyAccessor : IPowerPolicyAccessor
{
    private const uint AccessScheme = 16;

    public OperationResult<Guid> GetActiveScheme()
    {
        IntPtr schemePointer = IntPtr.Zero;
        try
        {
            var status = PowerGetActiveScheme(IntPtr.Zero, out schemePointer);
            if (status != 0 || schemePointer == IntPtr.Zero)
                return OperationResult<Guid>.Failure(
                    "有効な電源プランを確認できませんでした。", ErrorDetails(status));

            return OperationResult<Guid>.Success(Marshal.PtrToStructure<Guid>(schemePointer));
        }
        catch (Exception ex)
        {
            return OperationResult<Guid>.Failure(
                "有効な電源プランを確認できませんでした。", ex.ToString());
        }
        finally
        {
            if (schemePointer != IntPtr.Zero)
                _ = LocalFree(schemePointer);
        }
    }

    public OperationResult<IReadOnlyList<PowerPlan>> GetSchemes()
    {
        try
        {
            var active = GetActiveScheme();
            var plans = new List<PowerPlan>();

            for (uint index = 0; ; index++)
            {
                uint size = 16;
                var schemeBytes = new byte[size];
                var status = PowerEnumerate(
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, schemeBytes, ref size);
                if (status == 259) // ERROR_NO_MORE_ITEMS
                    break;
                if (status != 0)
                    return OperationResult<IReadOnlyList<PowerPlan>>.Failure(
                        "利用可能な電源プランを取得できませんでした。", ErrorDetails(status));

                var schemeId = new Guid(schemeBytes);
                var name = ReadFriendlyName(schemeId);
                plans.Add(new PowerPlan(
                    schemeId,
                    name.IsSuccess && !string.IsNullOrWhiteSpace(name.Value)
                        ? name.Value
                        : schemeId.ToString("D"),
                    active.IsSuccess && active.Value == schemeId));
            }

            return plans.Count > 0
                ? OperationResult<IReadOnlyList<PowerPlan>>.Success(plans)
                : OperationResult<IReadOnlyList<PowerPlan>>.Failure("利用可能な電源プランがありません。");
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<PowerPlan>>.Failure(
                "利用可能な電源プランを取得できませんでした。", ex.ToString());
        }
    }

    public OperationResult<uint> ReadValue(
        Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source)
    {
        try
        {
            var status = source == PowerSource.Ac
                ? PowerReadACValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, out var value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, out value);
            return status == 0
                ? OperationResult<uint>.Success(value)
                : OperationResult<uint>.Failure("電源設定の値を読み取れませんでした。", ErrorDetails(status));
        }
        catch (Exception ex)
        {
            return OperationResult<uint>.Failure("電源設定の値を読み取れませんでした。", ex.ToString());
        }
    }

    public OperationResult WriteValue(
        Guid schemeId, Guid subgroupId, Guid settingId, PowerSource source, uint seconds)
    {
        try
        {
            var status = source == PowerSource.Ac
                ? PowerWriteACValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, seconds)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref schemeId, ref subgroupId, ref settingId, seconds);
            return status == 0
                ? OperationResult.Success()
                : OperationResult.Failure("電源設定を書き込めませんでした。", ErrorDetails(status));
        }
        catch (Exception ex)
        {
            return OperationResult.Failure("電源設定を書き込めませんでした。", ex.ToString());
        }
    }

    public OperationResult ActivateScheme(Guid schemeId)
    {
        try
        {
            var status = PowerSetActiveScheme(IntPtr.Zero, ref schemeId);
            return status == 0
                ? OperationResult.Success()
                : OperationResult.Failure("電源プランを切り替えられませんでした。", ErrorDetails(status));
        }
        catch (Exception ex)
        {
            return OperationResult.Failure("電源プランを切り替えられませんでした。", ex.ToString());
        }
    }

    private static OperationResult<string> ReadFriendlyName(Guid schemeId)
    {
        uint size = 0;
        var status = PowerReadFriendlyName(
            IntPtr.Zero, ref schemeId, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (status != 0 && status != 234) // ERROR_MORE_DATA
            return OperationResult<string>.Failure("電源プラン名を読み取れませんでした。", ErrorDetails(status));

        var buffer = new byte[size];
        status = PowerReadFriendlyName(
            IntPtr.Zero, ref schemeId, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
        if (status != 0)
            return OperationResult<string>.Failure("電源プラン名を読み取れませんでした。", ErrorDetails(status));

        return OperationResult<string>.Success(Encoding.Unicode.GetString(buffer).TrimEnd('\0'));
    }

    private static string ErrorDetails(uint status) =>
        $"Power API error 0x{status:X8}: {new Win32Exception(unchecked((int)status)).Message}";

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subgroupOfPowerSettingsGuid,
        uint accessFlags, uint index, [Out] byte[] buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subgroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid, [Out] byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
        ref Guid settingGuid, out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
        ref Guid settingGuid, out uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
        ref Guid settingGuid, uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
        ref Guid settingGuid, uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
