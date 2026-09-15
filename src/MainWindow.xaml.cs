using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace IntuneProgressTracker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WorkloadItem> _items = [];
    private readonly IntuneStateReader _reader = new();
    private readonly ManifestService _manifestService;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private TrackerConfig _config = new();
    private ManifestRoot _manifest = new();
    private bool _allowClose;
    private bool _completionTriggered;
    private DateTime? _policySeenAtUtc;

    public MainWindow()
    {
        InitializeComponent();
        AppListView.ItemsSource = _items;
        _manifestService = new ManifestService(AppContext.BaseDirectory);
        _config = _manifestService.LoadConfig();
        _manifest = _manifestService.LoadManifest();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(3, _config.PollSeconds)) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { PositionWindow(); Refresh(); _timer.Start(); };
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 18);
        Top = Math.Max(area.Top, area.Bottom - Height - 18);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            var snapshot = _reader.Read(_manifest, _config);
            _items.Clear();
            foreach (var item in snapshot.Items) _items.Add(item);

            PhaseText.Text = snapshot.Phase;
            FooterText.Text = snapshot.IntuneDetected
                ? $"Intune detected • IME {(snapshot.ImeServiceRunning ? "running" : "starting")} • Local tracking only"
                : "Waiting for Intune • No Microsoft Graph connection is used";

            if (!snapshot.IntuneDetected || !snapshot.PolicyEvaluationSeen)
            {
                OverallStatusText.Text = snapshot.Phase;
                ProgressSummaryText.Text = snapshot.Diagnostic ?? "Waiting for Intune policy evaluation…";
                SetProgress(0);
                return;
            }

            if (snapshot.Items.Count == 0)
            {
                OverallStatusText.Text = "Discovering applications";
                ProgressSummaryText.Text = "Intune is present. Waiting for application state to arrive…";
                SetProgress(0);
                return;
            }

            var complete = snapshot.Items.Count(x => IsComplete(x));
            var failed = snapshot.Items.Count(x => x.Status == WorkloadStatus.Failed);
            var reboot = snapshot.Items.Count(x => x.Status == WorkloadStatus.RebootRequired);
            var active = snapshot.Items.Count - complete - failed;
            var percent = complete * 100d / snapshot.Items.Count;
            SetProgress(percent);

            RebootText.Text = reboot > 0 || snapshot.RebootPending ? "A restart may be required to finish setup." : "";
            OverallStatusText.Text = failed > 0 ? "Action required" : complete == snapshot.Items.Count ? "Setup complete" : "Installing applications";
            ProgressSummaryText.Text = failed > 0
                ? $"{complete} completed • {active} in progress • {failed} failed"
                : $"{complete} of {snapshot.Items.Count} required workloads completed";

            var minimumReached = DateTime.UtcNow - _startedUtc >= TimeSpan.FromSeconds(Math.Max(0, _config.MinimumObservationSeconds));
            var noStateGraceReached = DateTime.UtcNow - _startedUtc >= TimeSpan.FromSeconds(Math.Max(_config.NoStateGraceSeconds, _config.MinimumObservationSeconds));
            _policySeenAtUtc ??= DateTime.UtcNow;
            var discoveryStable = DateTime.UtcNow - _policySeenAtUtc.Value >= TimeSpan.FromSeconds(Math.Max(0, _config.DiscoveryStabilizationSeconds));
            var allComplete = snapshot.Items.All(IsComplete);
            var policyQuiet = snapshot.LastImeActivityUtc == null || DateTime.UtcNow - snapshot.LastImeActivityUtc.Value >= TimeSpan.FromSeconds(_config.PolicyQuietPeriodSeconds);

            // Never close simply because the first wave of local state happens to be complete.
            // Require IME policy evaluation + observation/grace periods + a quiet policy window.
            if (!_completionTriggered && _config.CloseOnlyWhenAllTrackedWorkloadsComplete && minimumReached && noStateGraceReached && discoveryStable && policyQuiet && allComplete && failed == 0)
            {
                _completionTriggered = true;
                _timer.Stop();
                CleanupLauncherMarker();
                _allowClose = true;
                Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            OverallStatusText.Text = "Monitoring error";
            ProgressSummaryText.Text = ex.Message;
        }
    }

    private bool IsComplete(WorkloadItem item) => item.Status is WorkloadStatus.Installed or WorkloadStatus.NotApplicable || (_config.TreatRebootRequiredAsComplete && item.Status == WorkloadStatus.RebootRequired);

    private void SetProgress(double value)
    {
        value = Math.Clamp(value, 0, 100);
        OverallProgressBar.Value = value;
        ProgressPercentageText.Text = $"{Math.Round(value)}%";
    }

    private void CleanupLauncherMarker()
    {
        if (!_config.RemoveRunEntryAfterCompletion) return;
        // The bootstrap is installed as SYSTEM and creates HKLM Run. The interactive user
        // deliberately does not attempt to delete the machine-level value. Instead, a
        // completion marker makes subsequent launches exit immediately. A future SYSTEM
        // maintenance/bootstrap update can remove the Run value safely.
        try { var markerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EOG", "IntuneProgressTracker"); Directory.CreateDirectory(markerDir); File.WriteAllText(Path.Combine(markerDir, ".setup-complete"), DateTime.UtcNow.ToString("O")); } catch { }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }
}
