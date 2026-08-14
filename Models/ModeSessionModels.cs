using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCModeSwitcher.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionExecutionStatus
{
    Pending,
    Succeeded,
    UnsupportedSkipped,
    TargetNotFoundSkipped,
    UserSkipped,
    ApplyFailed,
    RestoreSucceeded,
    RestoreFailed,
    Cancelled
}

public sealed class ActionExecutionResult
{
    public string ActionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ActionExecutionStatus Status { get; set; }
    public string Message { get; set; } = "";
    public string? TechnicalDetails { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ActionSnapshot
{
    public string ActionId { get; set; } = "";
    public JsonElement OriginalState { get; set; }
    public bool StateCaptured { get; set; }
    public ActionExecutionResult? ApplyResult { get; set; }
    public ActionExecutionResult? RestoreResult { get; set; }
    public bool Restored { get; set; }
}

public sealed class TrackedProcess
{
    public int ProcessId { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public string ExecutablePath { get; set; } = "";
    public bool CloseOnRestore { get; set; }
    public string? RuleId { get; set; }
}

public sealed class ClosedProcessRecord
{
    public int ProcessId { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public string ExecutablePath { get; set; } = "";
    public bool RestartOnRestore { get; set; }
    public List<string> Arguments { get; set; } = [];
    public string? WorkingDirectory { get; set; }
    public string? RuleId { get; set; }
}

public sealed class ModeSessionSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string ModeId { get; set; } = "";
    public string ModeName { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsApplying { get; set; } = true;
    public bool IsAwaitingRestore { get; set; }
    public List<ActionSnapshot> Actions { get; set; } = [];
    public List<TrackedProcess> LaunchedProcesses { get; set; } = [];
    public List<ClosedProcessRecord> ClosedProcesses { get; set; } = [];
}

public sealed class CapabilityItem
{
    public string Name { get; set; } = "";
    public bool IsSupported { get; set; }
    public string Details { get; set; } = "";
}

public sealed class CapabilityReport
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<CapabilityItem> Items { get; set; } = [];
}
