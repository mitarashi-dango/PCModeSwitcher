using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public interface IStartupService
{
    OperationResult SetEnabled(bool enabled);
}
