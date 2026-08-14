using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using PCModeSwitcher.Models;

namespace PCModeSwitcher.Services;

public sealed class DiagnosticsService
{
    private readonly AppPaths _paths = new();

    public async Task<string> CreateReportAsync()
    {
        var builder = new StringBuilder();
        builder.AppendLine(LocalizationService.Get("Diagnostics.ReportTitle"));
        builder.AppendLine(LocalizationService.Format("Diagnostics.GeneratedUtc", DateTimeOffset.UtcNow.ToString("O")));
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($".NET: {Environment.Version}");
        builder.AppendLine(LocalizationService.Format("Diagnostics.AppVersion", Assembly.GetExecutingAssembly().GetName().Version));
        builder.AppendLine(LocalizationService.Format("Diagnostics.WindowsPowerMode",
            LocalizationService.Get(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
                ? "Diagnostics.Available" : "Diagnostics.Unavailable")));

        var policy = new PowerPolicyAccessor();
        var active = policy.GetActiveScheme();
        builder.AppendLine(LocalizationService.Format("Diagnostics.CurrentPowerPlan", active.IsSuccess ? active.Value : active.UserMessage));
        var plans = policy.GetSchemes();
        builder.AppendLine(LocalizationService.Get("Diagnostics.AvailablePowerPlans"));
        if (plans.Value is not null)
            foreach (var plan in plans.Value) builder.AppendLine($"  - {plan.Name} ({plan.Id:D}){(plan.IsActive ? " " + LocalizationService.Get("Diagnostics.Active") : "")}");
        if (active.IsSuccess)
        {
            AppendPowerValue(builder, policy, active.Value, PowerSettingsService.VideoSubgroupId, PowerSettingsService.DisplayTimeoutId, LocalizationService.Get("Main.DisplayOff"));
            AppendPowerValue(builder, policy, active.Value, PowerSettingsService.SleepSubgroupId, PowerSettingsService.SleepTimeoutId, LocalizationService.Get("Main.Sleep"));
        }
        builder.AppendLine(LocalizationService.Get("Diagnostics.SleepPreventionAvailable"));

        var displays = new DisplayModeService().GetDisplays();
        builder.AppendLine(LocalizationService.Get("Diagnostics.ConnectedMonitors"));
        if (displays.Value is not null)
            foreach (var display in displays.Value)
                builder.AppendLine(LocalizationService.Format("Diagnostics.MonitorLine", display.FriendlyName, display.Width, display.Height, display.CurrentRefreshRate, string.Join(", ", display.SupportedRefreshRates)));
        else builder.AppendLine($"  {displays.UserMessage}");

        var audio = new AudioEndpointService();
        AppendAudio(builder, LocalizationService.Get("Diagnostics.DefaultOutput"), audio.GetDefaultState(AudioDataFlow.Render));
        AppendAudio(builder, LocalizationService.Get("Diagnostics.DefaultMicrophone"), audio.GetDefaultState(AudioDataFlow.Capture));

        var session = await new SessionStore(_paths).LoadAsync();
        builder.AppendLine(session.Value is null
            ? LocalizationService.Get("Diagnostics.RestorableNone")
            : LocalizationService.Format("Diagnostics.RestorableSession", session.Value.ModeName, session.Value.SessionId.ToString("D"), session.Value.StartedUtc.ToString("O")));
        builder.AppendLine(LocalizationService.Format("Diagnostics.LogFolder", Redact(_paths.LogDirectory)));
        builder.AppendLine(LocalizationService.Format("Diagnostics.SettingsFolder", Redact(_paths.RootDirectory)));
        return builder.ToString();
    }

    public async Task<OperationResult<string>> SaveReportAsync(string path)
    {
        try
        {
            await File.WriteAllTextAsync(path, await CreateReportAsync(), new UTF8Encoding(false));
            return OperationResult<string>.Success(path, "診断レポートを保存しました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Failure("診断レポートを保存できませんでした。", ex.ToString());
        }
    }

    public OperationResult<string> BackupAllSettings()
    {
        try
        {
            var destination = Path.Combine(_paths.BackupDirectory, $"manual-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(destination);
            foreach (var name in new[] { "settings.json", "profiles.json", "active-session.json" })
            {
                var source = Path.Combine(_paths.RootDirectory, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(destination, name));
            }
            return OperationResult<string>.Success(destination, "設定をバックアップしました。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Failure("設定をバックアップできませんでした。", ex.ToString());
        }
    }

    public void OpenLogs() => OpenDirectory(_paths.LogDirectory);
    public void OpenSettings() => OpenDirectory(_paths.RootDirectory);

    public static string CreateDryRun(PcMode mode, IReadOnlyCollection<PowerPlan> plans)
    {
        var lines = new List<string> { LocalizationService.Format("Diagnostics.DryRunHeading", mode.Name) };
        lines.Add(mode.Power.ChangePowerPlan
            ? plans.Any(plan => plan.Id == mode.Power.PowerPlanId)
                ? LocalizationService.Format("Diagnostics.ChangePlan", plans.First(plan => plan.Id == mode.Power.PowerPlanId).Name)
                : LocalizationService.Get("Diagnostics.PlanUnavailable")
            : LocalizationService.Get("Diagnostics.PlanNoChange"));
        lines.Add(mode.Power.SleepPrevention == SleepPreventionMode.None
            ? LocalizationService.Get("Diagnostics.SleepPreventionDisabled")
            : LocalizationService.Format("Diagnostics.SleepPrevention", FormatSleepPrevention(mode.Power.SleepPrevention)));
        lines.Add(mode.Display.RefreshRate is null
            ? LocalizationService.Get("Diagnostics.RefreshNoChange")
            : LocalizationService.Format("Diagnostics.RefreshChange", mode.Display.IsTrusted ? "✓" : "⚠", mode.Display.DeviceName, mode.Display.RefreshRate));
        lines.Add(FormatAudio(LocalizationService.Get("Diagnostics.DefaultOutput"), mode.Audio.Output));
        lines.Add(FormatAudio(LocalizationService.Get("Main.Microphone"), mode.Audio.Microphone));
        foreach (var rule in mode.CloseProcessRules) lines.Add(File.Exists(rule.ExecutablePath)
            ? LocalizationService.Format("Diagnostics.CloseTarget", rule.ExecutablePath)
            : LocalizationService.Format("Diagnostics.CloseMissing", rule.ExecutablePath));
        foreach (var item in mode.LaunchItems) lines.Add(Uri.IsWellFormedUriString(item.Target, UriKind.Absolute) || File.Exists(item.Target)
            ? LocalizationService.Format("Diagnostics.Launch", item.Target)
            : LocalizationService.Format("Diagnostics.LaunchMissing", item.Target));
        if (mode.WindowPlacements.Count > 0) lines.Add(LocalizationService.Format("Diagnostics.WindowPlacements", mode.WindowPlacements.Count));
        if (mode.MonitorRules.Count > 0) lines.Add(LocalizationService.Format("Diagnostics.AutoRestore", mode.MonitorRules.Count));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatAudio(string name, AudioEndpointConfiguration value) =>
        value.VolumePercent is null && value.Mute == AudioMuteSetting.NoChange
            ? LocalizationService.Format("Diagnostics.AudioNoChange", name)
            : LocalizationService.Format("Diagnostics.AudioChange", name, value.VolumePercent?.ToString() ?? LocalizationService.Get("Common.NoChange"), FormatMute(value.Mute));

    private static string FormatSleepPrevention(SleepPreventionMode value) => value switch
    {
        SleepPreventionMode.System => LocalizationService.Get("Diagnostics.PreventSleep"),
        SleepPreventionMode.SystemAndDisplay => LocalizationService.Get("Diagnostics.PreventSleepDisplay"),
        _ => LocalizationService.Get("Choice.Disabled")
    };

    private static string FormatMute(AudioMuteSetting value) => value switch
    {
        AudioMuteSetting.Mute => LocalizationService.Get("Choice.Mute"),
        AudioMuteSetting.Unmute => LocalizationService.Get("Choice.Unmute"),
        _ => LocalizationService.Get("Common.NoChange")
    };

    private static void AppendPowerValue(StringBuilder builder, IPowerPolicyAccessor policy, Guid scheme, Guid subgroup, Guid setting, string name)
    {
        var ac = policy.ReadValue(scheme, subgroup, setting, PowerSource.Ac);
        var dc = policy.ReadValue(scheme, subgroup, setting, PowerSource.Dc);
        builder.AppendLine(LocalizationService.Format("Diagnostics.PowerValue", name, ac.IsSuccess ? ac.Value : ac.UserMessage, dc.IsSuccess ? dc.Value : dc.UserMessage));
    }
    private static void AppendAudio(StringBuilder builder, string name, OperationResult<AudioEndpointState> result)
    {
        builder.AppendLine(result.IsSuccess && result.Value is not null
            ? LocalizationService.Format("Diagnostics.AudioState", name, result.Value.EndpointId, result.Value.VolumeScalar.ToString("P0"), result.Value.Muted)
            : $"{name}: {result.UserMessage}");
    }
    private static string Redact(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? path : path.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }
    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}
