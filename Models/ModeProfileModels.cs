using System.Text.Json.Serialization;

namespace PCModeSwitcher.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WindowsPowerMode
{
    NoChange,
    BestEfficiency,
    Balanced,
    BestPerformance
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SleepPreventionMode
{
    None,
    System,
    SystemAndDisplay
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AudioMuteSetting
{
    NoChange,
    Mute,
    Unmute
}

public sealed class PowerConfiguration
{
    public bool ChangePowerPlan { get; set; } = true;
    public Guid PowerPlanId { get; set; }
    public WindowsPowerMode AcPowerMode { get; set; }
    public WindowsPowerMode DcPowerMode { get; set; }
    public uint? DisplayTimeoutAcSeconds { get; set; }
    public uint? DisplayTimeoutDcSeconds { get; set; }
    public uint? SleepTimeoutAcSeconds { get; set; }
    public uint? SleepTimeoutDcSeconds { get; set; }
    public SleepPreventionMode SleepPrevention { get; set; }

    public PowerConfiguration Copy() => (PowerConfiguration)MemberwiseClone();
}

public sealed class DisplayConfiguration
{
    public string? DeviceName { get; set; }
    public uint? RefreshRate { get; set; }
    public bool IsTrusted { get; set; }
    public string? HardwareSignature { get; set; }

    public DisplayConfiguration Copy() => (DisplayConfiguration)MemberwiseClone();
}

public sealed class AudioEndpointConfiguration
{
    public int? VolumePercent { get; set; }
    public AudioMuteSetting Mute { get; set; }

    public AudioEndpointConfiguration Copy() => (AudioEndpointConfiguration)MemberwiseClone();
}

public sealed class AudioConfiguration
{
    public AudioEndpointConfiguration Output { get; set; } = new();
    public AudioEndpointConfiguration Microphone { get; set; } = new();

    public AudioConfiguration Copy() => new()
    {
        Output = Output.Copy(),
        Microphone = Microphone.Copy()
    };
}

public sealed class LaunchItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Target { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public string? WorkingDirectory { get; set; }
    public int DelayMilliseconds { get; set; }
    public int Order { get; set; }
    public bool AllowAdditionalInstance { get; set; }
    public bool CloseOnRestore { get; set; }

    public LaunchItem Copy() => new()
    {
        Id = Id,
        Target = Target,
        Arguments = [.. Arguments],
        WorkingDirectory = WorkingDirectory,
        DelayMilliseconds = DelayMilliseconds,
        Order = Order,
        AllowAdditionalInstance = AllowAdditionalInstance,
        CloseOnRestore = CloseOnRestore
    };
}

public sealed class CloseProcessRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ExecutablePath { get; set; } = "";
    public List<string> RestartArguments { get; set; } = [];
    public string? RestartWorkingDirectory { get; set; }
    public int GracePeriodSeconds { get; set; } = 5;
    public bool AllowForceKill { get; set; }
    public bool RestartOnRestore { get; set; }

    public CloseProcessRule Copy() => new()
    {
        Id = Id,
        ExecutablePath = ExecutablePath,
        RestartArguments = [.. RestartArguments],
        RestartWorkingDirectory = RestartWorkingDirectory,
        GracePeriodSeconds = GracePeriodSeconds,
        AllowForceKill = AllowForceKill,
        RestartOnRestore = RestartOnRestore
    };
}

public sealed class ProcessMonitorRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ExecutablePathOrName { get; set; } = "";

    public ProcessMonitorRule Copy() => (ProcessMonitorRule)MemberwiseClone();
}

public sealed class WindowPlacementRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ExecutablePath { get; set; }
    public string? ProcessName { get; set; }
    public string? WindowClassName { get; set; }
    public string? TitleContains { get; set; }
    public string? MonitorDeviceName { get; set; }
    public WindowPlacementData Placement { get; set; } = new();

    public WindowPlacementRule Copy() => new()
    {
        Id = Id,
        ExecutablePath = ExecutablePath,
        ProcessName = ProcessName,
        WindowClassName = WindowClassName,
        TitleContains = TitleContains,
        MonitorDeviceName = MonitorDeviceName,
        Placement = Placement.Copy()
    };
}

public sealed class WindowPlacementData
{
    public int ShowCommand { get; set; } = 1;
    public int NormalLeft { get; set; }
    public int NormalTop { get; set; }
    public int NormalRight { get; set; }
    public int NormalBottom { get; set; }

    public WindowPlacementData Copy() => (WindowPlacementData)MemberwiseClone();
}
