using Microsoft.Win32;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntuneProgressTracker;

public sealed class IntuneStateReader
{
    private const string Win32Root = @"SOFTWARE\Microsoft\IntuneManagementExtension\Win32Apps";
    private const string EspRoot = @"SOFTWARE\Microsoft\Windows\Autopilot\EnrollmentStatusTracking";
    private static readonly string LogRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "IntuneManagementExtension", "Logs");
    private static readonly Regex AppMapRegex = new(@"""Id"":""(?<id>[^""]+)"",""Name"":""(?<name>[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GuidRegex = new(@"(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})", RegexOptions.Compiled);
    private static readonly RegistryView[] RegistryViews = [RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default];
    private static readonly string[] RebootKeys = [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
        @"SYSTEM\CurrentControlSet\Control\Session Manager"
    ];

    public TrackerSnapshot Read(ManifestRoot manifest, TrackerConfig config)
    {
        var discovered = new Dictionary<string, WorkloadItem>(StringComparer.OrdinalIgnoreCase);
        var names = GetAppMap();
        ReadWin32Registry(discovered, names);
        ReadPowerShellLog(discovered);
        ReadMsiTracking(discovered);

        var imeDetected = Directory.Exists(LogRoot) || SafeOpenLocalMachineSubKey(Win32Root) != null;
        var serviceRunning = IsImeServiceRunning();
        var lastActivity = GetLastImeActivityUtc();
        var policySeen = HasPolicyEvaluation();
        var merged = MergeManifest(discovered, manifest, config);
        var phase = !imeDetected ? "Waiting for Intune Management Extension" :
                    !serviceRunning ? "Starting Intune Management Extension" :
                    !policySeen ? "Waiting for Intune policy evaluation" :
                    merged.Count == 0 ? "Discovering assigned workloads" : "Installing applications";

        return new TrackerSnapshot
        {
            Items = merged.OrderBy(x => StatusOrder(x.Status)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            IntuneDetected = imeDetected,
            ImeServiceRunning = serviceRunning,
            PolicyEvaluationSeen = policySeen,
            ManifestLoaded = manifest.Workloads.Count > 0,
            RebootPending = IsRebootPending(),
            LastImeActivityUtc = lastActivity,
            Phase = phase,
            Diagnostic = !imeDetected ? "The tracker is running before IME is ready. It will keep waiting." :
                         !serviceRunning ? "The Intune Management Extension service is not running yet." :
                         !policySeen ? "Waiting for the first Intune policy evaluation. No completion decision is made yet." :
                         merged.Count == 0 ? "IME is present but no tracked workload state has arrived yet." : null
        };
    }

    private static List<WorkloadItem> MergeManifest(Dictionary<string, WorkloadItem> discovered, ManifestRoot manifest, TrackerConfig config)
    {
        if (manifest.Workloads.Count == 0) return discovered.Values.ToList();
        var output = new List<WorkloadItem>();
        foreach (var m in manifest.Workloads.Where(x => x.Required || config.ShowOptionalWorkloads))
        {
            var matches = discovered.Values.Where(x => x.Kind == m.Type &&
                (string.Equals(x.Id, m.Id, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(m.ProductCode) && string.Equals(x.Id, m.ProductCode, StringComparison.OrdinalIgnoreCase)))).ToList();
            var selected = matches.OrderByDescending(x => x.Status == WorkloadStatus.Installed)
                                  .ThenByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            var dependencyBlocked = IsDependencyBlocked(m, output);
            if (selected != null)
            {
                output.Add(selected with
                {
                    Key = m.Type + ":" + m.Id,
                    Id = m.Id,
                    Name = string.IsNullOrWhiteSpace(m.Name) ? selected.Name : m.Name,
                    IsManifestItem = true,
                    IsDuplicateState = matches.Count > 1,
                    DependencyBlocked = dependencyBlocked,
                    IsAssignmentDiscovered = true,
                    Status = dependencyBlocked && selected.Status is WorkloadStatus.Pending or WorkloadStatus.Installing or WorkloadStatus.Downloading ? WorkloadStatus.Pending : selected.Status,
                    Details = dependencyBlocked ? "Waiting for dependency to complete" : selected.Details
                });
            }
            else
            {
                output.Add(new WorkloadItem
                {
                    Key = m.Type + ":" + m.Id,
                    Id = m.Id,
                    Name = m.Name,
                    Kind = m.Type,
                    Status = WorkloadStatus.Pending,
                    Details = dependencyBlocked ? "Waiting for dependency" : "Waiting for Intune evaluation",
                    IsManifestItem = true,
                    DependencyBlocked = dependencyBlocked,
                    IsAssignmentDiscovered = false
                });
            }
        }
        return output;
    }

    private static bool IsDependencyBlocked(ManifestWorkload item, List<WorkloadItem> output)
    {
        if (string.IsNullOrWhiteSpace(item.DependsOn)) return false;
        var dep = output.FirstOrDefault(x => string.Equals(x.Id, item.DependsOn, StringComparison.OrdinalIgnoreCase));
        return dep != null && dep.Status != WorkloadStatus.Installed && dep.Status != WorkloadStatus.NotApplicable;
    }

    private static Dictionary<string, string> GetAppMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(LogRoot, "AppWorkload.log");
        if (!File.Exists(path)) return map;
        foreach (var line in ReadTail(path, 150000))
            foreach (Match m in AppMapRegex.Matches(line))
                map.TryAdd(m.Groups["id"].Value, m.Groups["name"].Value.Trim());
        return map;
    }

    private static string[] SafeGetSubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (System.Security.SecurityException) { return []; }
    }

    private static RegistryKey? SafeOpenLocalMachineSubKey(string keyPath)
    {
        foreach (var view in RegistryViews)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                var key = baseKey.OpenSubKey(keyPath);
                if (key != null) return key;
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (ArgumentException) { }
            catch (IOException) { }
        }

        try { return Registry.LocalMachine.OpenSubKey(keyPath); }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static RegistryKey? SafeOpenSubKey(RegistryKey key, string name)
    {
        try { return key.OpenSubKey(name); }
        catch (UnauthorizedAccessException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }

    private static void ReadWin32Registry(Dictionary<string, WorkloadItem> result, Dictionary<string, string> names)
    {
        using var root = SafeOpenLocalMachineSubKey(Win32Root);
        if (root == null) return;
        foreach (var contextName in SafeGetSubKeyNames(root))
        {
            using var context = SafeOpenSubKey(root, contextName);
            if (context == null) continue;
            foreach (var appKeyName in SafeGetSubKeyNames(context))
            {
                var idMatch = GuidRegex.Match(appKeyName);
                if (!idMatch.Success) continue;
                using var appKey = SafeOpenSubKey(context, appKeyName);
                if (appKey == null) continue;
                try
                {
                    var id = idMatch.Groups["id"].Value;
                    var compliance = ParseJson(appKey.GetValue("ComplianceStateMessage")?.ToString());
                    var enforcement = ParseJson(appKey.GetValue("EnforcementStateMessage")?.ToString());
                    var execution = appKey.GetValue("ExecutionStatus")?.ToString();
                    var download = appKey.GetValue("DownloadStatus")?.ToString();
                    var version = appKey.GetValue("Version")?.ToString() ?? "";
                    var name = names.TryGetValue(id, out var n) ? n : GetString(enforcement?.RootElement, "ApplicationName") ?? GetString(compliance?.RootElement, "ApplicationName") ?? id;
                    var error = GetNullableInt(enforcement?.RootElement, "ErrorCode");
                    var enforcementState = GetNullableInt(enforcement?.RootElement, "EnforcementState");
                    var complianceState = GetNullableInt(compliance?.RootElement, "ComplianceState");
                    var status = MapWin32State(execution, download, enforcementState, complianceState, error);
                    var key = $"win32:{contextName}:{id}:{version}";
                    result[key] = new WorkloadItem { Key = key, Id = id, Name = name, Kind = WorkloadKind.Win32, Status = status, ExitCode = error, Details = BuildDetails(status, error, execution, download, enforcementState), Version = version, IsAssignmentDiscovered = true };
                }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }
    }

    private static WorkloadStatus MapWin32State(string? execution, string? download, int? enforcement, int? compliance, int? error)
    {
        if (error is not null && error.Value != 0) return IsRebootCode(error.Value) ? WorkloadStatus.RebootRequired : WorkloadStatus.Failed;
        if (string.Equals(execution, "Completed", StringComparison.OrdinalIgnoreCase)) return WorkloadStatus.Installed;
        if (string.Equals(download, "RunningInBackground", StringComparison.OrdinalIgnoreCase)) return WorkloadStatus.Downloading;
        if (string.Equals(execution, "Initiated", StringComparison.OrdinalIgnoreCase)) return WorkloadStatus.Installing;
        if (enforcement is >= 5000) return WorkloadStatus.Failed;
        if (enforcement is 0 or 1000 or 1003 or 1016 || compliance == 2) return WorkloadStatus.Installed;
        if (enforcement is > 0) return WorkloadStatus.Installing;
        return WorkloadStatus.Pending;
    }

    private static void ReadPowerShellLog(Dictionary<string, WorkloadItem> result)
    {
        var path = Path.Combine(LogRoot, "AgentExecutor.log");
        if (!File.Exists(path)) return;
        var states = new Dictionary<string, WorkloadItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ReadTail(path, 150000))
        {
            if (!line.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) && !line.Contains("script", StringComparison.OrdinalIgnoreCase)) continue;
            var match = GuidRegex.Match(line);
            if (!match.Success) continue;
            var id = match.Groups["id"].Value;
            var lower = line.ToLowerInvariant();
            var status = lower.Contains("failed") || lower.Contains("error") || lower.Contains("exception") ? WorkloadStatus.Failed :
                         lower.Contains("reboot") ? WorkloadStatus.RebootRequired :
                         lower.Contains("success") || lower.Contains("completed") || lower.Contains("succeeded") ? WorkloadStatus.Installed : WorkloadStatus.Installing;
            var name = ExtractScriptName(line) ?? $"PowerShell script {id}";
            states[id] = new WorkloadItem { Key = $"ps:{id}", Id = id, Name = name, Kind = WorkloadKind.PowerShell, Status = status, Details = status switch { WorkloadStatus.Failed => "Script execution failed", WorkloadStatus.Installed => "Script completed", WorkloadStatus.RebootRequired => "Script completed; restart required", _ => "Script execution in progress" }, IsAssignmentDiscovered = true };
        }
        foreach (var x in states) result[x.Key] = x.Value;
    }

    private static string? ExtractScriptName(string line)
    {
        foreach (var pattern in new[] { @"(?:script|Script)(?:Name)?\s*[:=]\s*[""']?(?<name>[^""']+)", @"ScriptName\s*=\s*(?<name>[^,;]+)" })
        {
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups["name"].Value.Trim();
        }
        return null;
    }

    private static void ReadMsiTracking(Dictionary<string, WorkloadItem> result)
    {
        using var root = SafeOpenLocalMachineSubKey(EspRoot);
        if (root == null) return;
        ScanMsi(root, result);
    }

    private static void ScanMsi(RegistryKey key, Dictionary<string, WorkloadItem> result)
    {
        foreach (var subName in SafeGetSubKeyNames(key))
        {
            using var sub = SafeOpenSubKey(key, subName);
            if (sub == null) continue;
            if (subName.Equals("ExpectedMSIAppPackages", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pkg in SafeGetSubKeyNames(sub))
                {
                    using var p = SafeOpenSubKey(sub, pkg);
                    if (p == null) continue;
                    foreach (var child in SafeGetSubKeyNames(p))
                    {
                        using var c = SafeOpenSubKey(p, child);
                        if (c == null) continue;
                        try
                        {
                            var state = c.GetValue("InstallationState")?.ToString();
                            if (state == null) continue;
                            var name = c.GetValue("DisplayName")?.ToString() ?? child;
                            var status = state switch { "3" => WorkloadStatus.Installed, "4" => WorkloadStatus.Failed, "2" => WorkloadStatus.Installing, _ => WorkloadStatus.Pending };
                            var product = c.GetValue("ProductCode")?.ToString() ?? child;
                            var keyName = $"msi:{product}";
                            result[keyName] = new WorkloadItem { Key = keyName, Id = product, Name = name, Kind = WorkloadKind.Msi, Status = status, Details = status == WorkloadStatus.Failed ? "MSI installation failed" : $"ESP MSI state {state}", IsAssignmentDiscovered = true };
                        }
                        catch (UnauthorizedAccessException) { }
                        catch (System.Security.SecurityException) { }
                    }
                }
            }
            ScanMsi(sub, result);
        }
    }

    private static bool HasPolicyEvaluation()
    {
        var path = Path.Combine(LogRoot, "IntuneManagementExtension.log");
        if (!File.Exists(path)) return false;
        foreach (var line in ReadTail(path, 50000).Reverse())
        {
            if (line.Contains("policy", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("request", StringComparison.OrdinalIgnoreCase) || line.Contains("process", StringComparison.OrdinalIgnoreCase) || line.Contains("received", StringComparison.OrdinalIgnoreCase) || line.Contains("sync", StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static DateTime? GetLastImeActivityUtc()
    {
        DateTime? latest = null;
        foreach (var file in Directory.Exists(LogRoot) ? Directory.EnumerateFiles(LogRoot, "*.log") : [])
        {
            try { var t = File.GetLastWriteTimeUtc(file); if (latest == null || t > latest) latest = t; } catch { }
        }
        return latest;
    }

    private static bool IsImeServiceRunning()
    {
        try { using var sc = new ServiceController("IntuneManagementExtension"); return sc.Status == ServiceControllerStatus.Running; }
        catch { return false; }
    }

    private static bool IsRebootPending()
    {
        using var cbs = SafeOpenLocalMachineSubKey(RebootKeys[0]); if (cbs != null) return true;
        using var wu = SafeOpenLocalMachineSubKey(RebootKeys[1]); if (wu != null) return true;
        using var sm = SafeOpenLocalMachineSubKey(RebootKeys[2]);
        return sm?.GetValue("PendingFileRenameOperations") is string[] a && a.Length > 0;
    }

    private static bool IsRebootCode(int code) => unchecked((uint)code) is 3010 or 1641;
    private static string BuildDetails(WorkloadStatus status, int? error, string? execution, string? download, int? enforcement) =>
        error is not null && error.Value != 0 ? $"Error 0x{unchecked((uint)error.Value):X8}" :
        status == WorkloadStatus.Installed ? "Detection successful" :
        status == WorkloadStatus.Downloading ? "Downloading content" :
        status == WorkloadStatus.Installing ? "Intune is processing this workload" :
        enforcement is not null ? $"Enforcement state {enforcement}" : "Waiting for Intune evaluation";

    private static JsonDocument? ParseJson(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json); } catch { return null; } }
    private static string? GetString(JsonElement? root, string name) => root?.ValueKind == JsonValueKind.Object && root.Value.TryGetProperty(name, out var p) ? p.ToString() : null;
    private static int? GetNullableInt(JsonElement? root, string name) => int.TryParse(GetString(root, name), out var n) ? n : null;
    private static int StatusOrder(WorkloadStatus s) => s switch { WorkloadStatus.Failed => 0, WorkloadStatus.RebootRequired => 1, WorkloadStatus.Downloading => 2, WorkloadStatus.Installing => 3, WorkloadStatus.Pending => 4, WorkloadStatus.Installed => 5, WorkloadStatus.NotApplicable => 6, _ => 7 };
    private static IEnumerable<string> ReadTail(string path, int maxLines)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var q = new Queue<string>(maxLines);
            while (!sr.EndOfStream) { var line = sr.ReadLine(); if (line == null) continue; q.Enqueue(line); if (q.Count > maxLines) q.Dequeue(); }
            return q;
        }
        catch { return []; }
    }
}
