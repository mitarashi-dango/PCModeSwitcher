using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using PCModeSwitcher.Models;
using Windows.ApplicationModel;

namespace PCModeSwitcher.Services;

public sealed class StartupService : IStartupService
{
    internal const string PackagedStartupTaskId = "PCModeSwitcherStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PCModeSwitcher";
    private readonly Func<string?> _executablePathProvider;

    public StartupService(Func<string?>? executablePathProvider = null)
    {
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public async Task<OperationResult> SetEnabledAsync(bool enabled)
    {
        if (HasPackageIdentity())
            return await SetPackagedStartupEnabledAsync(enabled);

        return SetUnpackagedStartupEnabled(enabled);
    }

    private OperationResult SetUnpackagedStartupEnabled(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                return OperationResult.Failure("スタートアップ設定を開けませんでした。");
            }

            if (!enabled)
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
                return OperationResult.Success();
            }

            var executablePath = _executablePathProvider();
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return OperationResult.Failure("アプリの実行ファイルを特定できないため、スタートアップへ登録できませんでした。");
            }

            runKey.SetValue(
                ValueName,
                $"\"{executablePath}\" --startup",
                RegistryValueKind.String);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return OperationResult.Failure(
                "スタートアップ設定を変更できませんでした。",
                ex.Message);
        }
    }

    private static async Task<OperationResult> SetPackagedStartupEnabledAsync(bool enabled)
    {
        try
        {
            var startupTask = await StartupTask.GetAsync(PackagedStartupTaskId);
            if (!enabled)
            {
                startupTask.Disable();
                return OperationResult.Success();
            }

            var state = startupTask.State == StartupTaskState.Enabled
                ? startupTask.State
                : await startupTask.RequestEnableAsync();
            return state switch
            {
                StartupTaskState.Enabled => OperationResult.Success(),
                StartupTaskState.EnabledByPolicy => OperationResult.Success(),
                StartupTaskState.DisabledByUser => OperationResult.Failure(
                    "Windowsの［設定］→［アプリ］→［スタートアップ］でPC Mode Switcherを有効にしてください。"),
                StartupTaskState.DisabledByPolicy => OperationResult.Failure(
                    "組織のポリシーにより、スタートアップを有効にできません。"),
                _ => OperationResult.Failure("スタートアップを有効にできませんでした。")
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
        {
            return OperationResult.Failure(
                "スタートアップ設定を変更できませんでした。",
                ex.Message);
        }
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.FullName;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return false;
        }
    }

}
