using System.IO;

namespace PCModeSwitcher.Services;

public sealed class AppPaths
{
    public string RootDirectory { get; }
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    public string ProfilesPath => Path.Combine(RootDirectory, "profiles.json");
    public string ActiveSessionPath => Path.Combine(RootDirectory, "active-session.json");
    public string BackupDirectory => Path.Combine(RootDirectory, "Backups");
    public string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCModeSwitcher");
    }
}
