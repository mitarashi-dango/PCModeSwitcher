using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class MicrophoneMuteService : IMicrophoneMuteService
{
    private readonly IMicrophoneMuteAccessor _accessor;

    public MicrophoneMuteService() : this(new CoreAudioMicrophoneMuteAccessor()) { }

    internal MicrophoneMuteService(IMicrophoneMuteAccessor accessor)
    {
        _accessor = accessor;
    }

    public OperationResult<bool> GetCurrentMuted() => _accessor.GetMuted();

    public OperationResult Apply(MicrophoneMuteSetting setting)
    {
        if (!Enum.IsDefined(setting))
            return OperationResult.Failure("マイク設定が正しくありません。");

        if (setting == MicrophoneMuteSetting.NoChange)
            return OperationResult.Success("マイクのミュート状態は変更しませんでした。");

        var current = _accessor.GetMuted();
        if (!current.IsSuccess)
        {
            return OperationResult.Failure(
                "既定のマイクのミュート状態を確認できませんでした。",
                current.TechnicalDetails);
        }

        var requestedMuted = setting == MicrophoneMuteSetting.Mute;
        if (current.Value == requestedMuted)
        {
            return OperationResult.Success(
                requestedMuted ? "マイクはすでにミュートです。" : "マイクはすでにミュート解除されています。");
        }

        var set = _accessor.SetMuted(requestedMuted);
        if (!set.IsSuccess)
        {
            return OperationResult.Failure(
                requestedMuted ? "マイクをミュートできませんでした。" : "マイクのミュートを解除できませんでした。",
                set.TechnicalDetails);
        }

        var verify = _accessor.GetMuted();
        if (verify.IsSuccess && verify.Value == requestedMuted)
        {
            return OperationResult.Success(
                requestedMuted ? "マイクをミュートしました。" : "マイクのミュートを解除しました。");
        }

        var rollback = _accessor.SetMuted(current.Value);
        return rollback.IsSuccess
            ? OperationResult.Failure(
                "マイク設定の反映を確認できなかったため、変更前の状態へ戻しました。",
                verify.TechnicalDetails)
            : OperationResult.Failure(
                "マイク設定の反映を確認できず、変更前の状態へ戻せませんでした。Windowsのサウンド設定で確認してください。",
                string.Join("; ", new[] { verify.TechnicalDetails, rollback.TechnicalDetails }
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))));
    }
}
