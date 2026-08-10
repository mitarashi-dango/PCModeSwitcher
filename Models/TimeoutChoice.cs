namespace PCModeSwitcher.Models;

public sealed record TimeoutChoice(uint Seconds, string Label)
{
    public override string ToString() => Label;
}
