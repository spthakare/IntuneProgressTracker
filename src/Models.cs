namespace IntuneProgressTracker;

public enum WorkloadKind { Win32, Msi, PowerShell }
public enum WorkloadStatus { Unknown, Pending, Downloading, Installing, RebootRequired, Failed, Installed, NotApplicable }

public sealed class ManifestWorkload
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public WorkloadKind Type { get; set; }
    public string? ProductCode { get; set; }
    public bool Required { get; set; } = true;
    public string? DependsOn { get; set; }
    public string? MinimumVersion { get; set; }
}

public sealed class TrackerConfig
{
    public int PollSeconds { get; set; } = 5;
    public int BootstrapWaitSeconds { get; set; } = 900;
    public int DiscoveryStabilizationSeconds { get; set; } = 90;
    public int MinimumObservationSeconds { get; set; } = 60;
    public int NoStateGraceSeconds { get; set; } = 180;
    public int PolicyQuietPeriodSeconds { get; set; } = 30;
    public bool CloseOnlyWhenAllTrackedWorkloadsComplete { get; set; } = true;
    public bool RequireManifestWhenPresent { get; set; } = true;
    public bool TreatRebootRequiredAsComplete { get; set; } = false;
    public bool ShowOptionalWorkloads { get; set; } = false;
    public bool LaunchAtUserLogon { get; set; } = true;
    public bool RemoveRunEntryAfterCompletion { get; set; } = true;
    public bool AllowCloseWhenIntuneNeverArrived { get; set; } = false;
    public string[] LogFiles { get; set; } = [
        @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\IntuneManagementExtension.log",
        @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\AppWorkload.log",
        @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\AgentExecutor.log",
        @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\AppActionProcessor.log"
    ];
}

public sealed class ManifestRoot
{
    public string Version { get; set; } = "4.0";
    public List<ManifestWorkload> Workloads { get; set; } = [];
}

public sealed record WorkloadItem
{
    public string Key { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "Unknown workload";
    public WorkloadKind Kind { get; init; }
    public WorkloadStatus Status { get; init; }
    public int? ExitCode { get; init; }
    public string Details { get; init; } = "";
    public string Version { get; init; } = "";
    public bool IsManifestItem { get; init; }
    public bool IsDuplicateState { get; init; }
    public bool DependencyBlocked { get; init; }
    public bool IsAssignmentDiscovered { get; init; }
    public string Type => Kind.ToString();
    public string StatusText => Status switch
    {
        WorkloadStatus.Installed => "Installed",
        WorkloadStatus.Downloading => "Downloading",
        WorkloadStatus.Installing => "Installing",
        WorkloadStatus.RebootRequired => "Restart required",
        WorkloadStatus.Failed => "Failed",
        WorkloadStatus.NotApplicable => "Not applicable",
        WorkloadStatus.Pending => DependencyBlocked ? "Waiting for dependency" : "Waiting",
        _ => "Checking"
    };
}

public sealed class TrackerSnapshot
{
    public List<WorkloadItem> Items { get; init; } = [];
    public bool IntuneDetected { get; init; }
    public bool ImeServiceRunning { get; init; }
    public bool PolicyEvaluationSeen { get; init; }
    public bool ManifestLoaded { get; init; }
    public bool RebootPending { get; init; }
    public DateTime? LastImeActivityUtc { get; init; }
    public string Phase { get; init; } = "Starting";
    public string? Diagnostic { get; init; }
}
