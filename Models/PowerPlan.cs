namespace PCModeSwitcher.Models;

public sealed record PowerPlan(Guid Id, string Name, bool IsActive = false)
{
    public override string ToString() => Name;
}
