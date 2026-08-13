using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public interface IMicrophoneMuteService
{
    OperationResult Apply(MicrophoneMuteSetting setting);
}

internal interface IMicrophoneMuteAccessor
{
    OperationResult<bool> GetMuted();
    OperationResult SetMuted(bool muted);
}
