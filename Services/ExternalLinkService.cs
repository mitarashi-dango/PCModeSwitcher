using System.ComponentModel;
using System.Diagnostics;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

internal static class ExternalLinkService
{
    public static OperationResult Open(
        Uri uri,
        Func<ProcessStartInfo, Process?>? launcher = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
            _ = launcher is null ? Process.Start(startInfo) : launcher(startInfo);
            return OperationResult.Success();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return OperationResult.Failure(
                LocalizationService.Get("Error.ExternalBrowser"),
                ex.ToString());
        }
    }
}
