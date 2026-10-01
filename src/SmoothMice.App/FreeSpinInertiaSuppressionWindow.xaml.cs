using System.ComponentModel;
using System.Windows;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

/// <summary>Module switch, mode, live decisions and the optional decision log. Input stays fail-open.</summary>
public partial class FreeSpinInertiaSuppressionWindow : Window, INotifyPropertyChanged
{
    private readonly FreeSpinDetectionService _detector;
    private readonly Action<bool> _saveModuleEnabled;
    private readonly Action<FreeSpinDetectionMode> _saveMode;
    private readonly Action<bool> _saveLogDecisions;
    private readonly bool _ownsDetector;
    private bool _initializing = true;
    private bool _moduleEnabled;
    private bool _logDecisions;
    private FreeSpinDetectionMode _detectionMode;
    private string _latestVerdict = "No wheel pulse analyzed yet.";
    private int _analyzedCount, _suppressedCount;

    public FreeSpinInertiaSuppressionWindow(bool moduleEnabled, FreeSpinDetectionMode detectionMode, bool logDecisions,
        FreeSpinDetectionService detector, Action<bool> saveModuleEnabled, Action<FreeSpinDetectionMode> saveMode,
        Action<bool> saveLogDecisions, bool ownsDetector = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _saveModuleEnabled = saveModuleEnabled ?? throw new ArgumentNullException(nameof(saveModuleEnabled));
        _saveMode = saveMode ?? throw new ArgumentNullException(nameof(saveMode));
        _saveLogDecisions = saveLogDecisions ?? throw new ArgumentNullException(nameof(saveLogDecisions));
        _ownsDetector = ownsDetector;
        _moduleEnabled = moduleEnabled;
        _detectionMode = detectionMode;
        _logDecisions = logDecisions;
        _detector.DecisionObserved += Detector_OnDecisionObserved;
        DataContext = this; InitializeComponent(); _initializing = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool ModuleEnabled { get => _moduleEnabled; set { if (_moduleEnabled == value) return; _moduleEnabled = value; Raise(nameof(ModuleEnabled)); } }
    public bool IsReadOnlyMode { get => _detectionMode == FreeSpinDetectionMode.ReadOnly; set { if (value) SetMode(FreeSpinDetectionMode.ReadOnly); } }
    public bool IsSuppressMode { get => _detectionMode == FreeSpinDetectionMode.Suppress; set { if (value) SetMode(FreeSpinDetectionMode.Suppress); } }
    public bool LogDecisions
    {
        get => _logDecisions;
        set
        {
            if (_logDecisions == value) return;
            _logDecisions = value;
            if (!_initializing) _saveLogDecisions(value);
            Raise(nameof(LogDecisions)); Raise(nameof(DecisionLogText));
        }
    }
    public string DetectorSummary => _detector.Summary;
    public string LatestVerdict => _latestVerdict;
    public string DetectorCountersText => $"Analyzed {_analyzedCount:N0} · Dropped {_suppressedCount:N0}";
    public string DecisionLogText => _logDecisions && _detector.DecisionLogPath is { } path
        ? "Recording to " + path
        : @"Writes every decision to %LOCALAPPDATA%\SmoothMice\Diagnostics. Turn it on only to diagnose a problem.";

    private void SetMode(FreeSpinDetectionMode mode)
    {
        if (_detectionMode == mode) return;
        _detectionMode = mode;
        if (!_initializing) _saveMode(mode);
        Raise(nameof(IsReadOnlyMode)); Raise(nameof(IsSuppressMode));
    }

    private void ModuleSwitch_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _saveModuleEnabled(ModuleEnabled);
        Raise(nameof(DecisionLogText));
    }

    private void Detector_OnDecisionObserved(object? sender, FreeSpinDetectionDecision decision)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _analyzedCount++;
                if (decision.Eligible) _suppressedCount++;
                _latestVerdict = decision.Reason;
                Raise(nameof(LatestVerdict)); Raise(nameof(DetectorCountersText));
            }));
        }
        catch { }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _detector.DecisionObserved -= Detector_OnDecisionObserved;
        if (_ownsDetector) _detector.Dispose();
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
