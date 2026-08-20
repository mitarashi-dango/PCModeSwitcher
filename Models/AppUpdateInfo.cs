namespace PCModeSwitcher.Models;

public sealed record AppUpdateInfo(
    Version Version,
    string DisplayVersion,
    Uri ReleaseUri,
    bool IsNewer);
