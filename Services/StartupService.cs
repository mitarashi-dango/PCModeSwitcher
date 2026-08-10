using System.IO;
using System.Security;
using Microsoft.Win32;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PCModeSwitcher";
    private readonly Func<string?> _executablePathProvider;

    public StartupService(Func<string?>? executablePathProvider = null)
    {
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public OperationResult SetEnabled(bool enabled)
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
}
