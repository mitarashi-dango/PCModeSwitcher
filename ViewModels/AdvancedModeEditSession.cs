using PCModeSwitcher.Models;

namespace PCModeSwitcher.ViewModels;

internal sealed class AdvancedModeEditSession
{
    public AdvancedModeEditSession(PcMode source) => Draft = source.Copy();

    public PcMode Draft { get; }

    public void ConfirmDisplay(string deviceName, uint refreshRate, string hardwareSignature)
    {
        Draft.Display.DeviceName = deviceName;
        Draft.Display.RefreshRate = refreshRate;
        Draft.Display.HardwareSignature = hardwareSignature;
        Draft.Display.IsTrusted = true;
    }

    public bool IsDisplayTrusted(string? deviceName, uint? refreshRate, string? hardwareSignature) =>
        Draft.Display.IsTrusted &&
        !string.IsNullOrWhiteSpace(deviceName) &&
        refreshRate is not null &&
        string.Equals(Draft.Display.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase) &&
        Draft.Display.RefreshRate == refreshRate &&
        string.Equals(Draft.Display.HardwareSignature, hardwareSignature, StringComparison.Ordinal);

    public void ReplaceWindowPlacements(IEnumerable<WindowPlacementRule> placements) =>
        Draft.WindowPlacements = placements.Select(value => value.Copy()).ToList();
}
