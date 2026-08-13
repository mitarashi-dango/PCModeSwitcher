namespace PCModeSwitcher.Models;

public sealed record MicrophoneMuteChoice(MicrophoneMuteSetting Setting, string Label)
{
    public override string ToString() => Label;
}
