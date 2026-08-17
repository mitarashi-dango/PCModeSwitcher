using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public interface IStartupService
{
    Task<OperationResult> SetEnabledAsync(bool enabled);
}
