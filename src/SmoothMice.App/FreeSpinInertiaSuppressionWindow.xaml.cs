using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

/// <summary>Monitor-only calibration UI; all physical input remains fail-open.</summary>
public partial class FreeSpinInertiaSuppressionWindow : Window, INotifyPropertyChanged
{
    private readonly Action<bool> _saveModuleEnabled;
    private readonly Action<FreeSpinCalibrationPhase, int> _saveTarget;
    private readonly FreeSpinCalibrationRecorder _recorder;
    private readonly DispatcherTimer _ticker;
    private bool _initializing = true;
    private bool _moduleEnabled;
    private bool _isSaving;
    private FreeSpinCalibrationPhase _selected = FreeSpinCalibrationPhase.Lift;
    private string _selectedTargetText = "40";
    private string _statusText = "Ready: F8 starts a clean capture without using the mouse.";
    private string _errorText = string.Empty;
    private int _liftTarget, _landingTarget, _repositionTarget, _legitimateTarget;
    private int _liftCount, _landingCount, _repositionCount, _legitimateCount;

    public FreeSpinInertiaSuppressionWindow(bool moduleEnabled, int liftTarget, int landingTarget, int repositionTarget, int legitimateTarget,
        FreeSpinCalibrationRecorder recorder, Action<bool> saveModuleEnabled, Action<FreeSpinCalibrationPhase, int> saveTarget)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _saveModuleEnabled = saveModuleEnabled ?? throw new ArgumentNullException(nameof(saveModuleEnabled));
        _saveTarget = saveTarget ?? throw new ArgumentNullException(nameof(saveTarget));
        _liftTarget = Clamp(liftTarget); _landingTarget = Clamp(landingTarget); _repositionTarget = Clamp(repositionTarget); _legitimateTarget = Clamp(legitimateTarget);
        ModuleEnabled = moduleEnabled; RefreshCounts(); SyncSelectedTarget();
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _ticker.Tick += (_, _) => RaiseRecordingProperties();
        DataContext = this; InitializeComponent(); _initializing = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public bool ModuleEnabled { get => _moduleEnabled; set { if (_moduleEnabled == value) return; _moduleEnabled = value; Raise(nameof(ModuleEnabled)); } }
    public bool IsLiftSelected => _selected == FreeSpinCalibrationPhase.Lift;
    public bool IsLandingSelected => _selected == FreeSpinCalibrationPhase.Landing;
    public bool IsRepositionSelected => _selected == FreeSpinCalibrationPhase.Reposition;
    public bool IsLegitimateSelected => _selected == FreeSpinCalibrationPhase.LegitimateScroll;
    public string LiftCountText => CountText(_liftCount, _liftTarget);
    public string LandingCountText => CountText(_landingCount, _landingTarget);
    public string RepositionCountText => CountText(_repositionCount, _repositionTarget);
    public string LegitimateCountText => CountText(_legitimateCount, _legitimateTarget);
    public string SelectedCountText => CountText(SelectedCount, SelectedTarget);
    public string SelectedTargetText { get => _selectedTargetText; set { _selectedTargetText = value; Raise(nameof(SelectedTargetText)); } }
    public string SelectedStageTitle => _selected switch { FreeSpinCalibrationPhase.Lift => "Lift", FreeSpinCalibrationPhase.Landing => "Landing", FreeSpinCalibrationPhase.Reposition => "Full reposition", _ => "Legitimate scroll" };
    public string SelectedInstructions => _selected switch
    {
        FreeSpinCalibrationPhase.Lift => "Press F8, lift the mouse as you normally would, then press F8 to finish. Each sample contains one gesture only.",
        FreeSpinCalibrationPhase.Landing => "Press F8, place the mouse down as you normally would, then press F8 after the physical movement ends.",
        FreeSpinCalibrationPhase.Reposition => "Press F8, complete the full lift, reposition, and landing cycle, then press F8 to finish.",
        _ => "Press F8, perform a normal intentional scroll, then press F8. This protects the future rule against false positives.",
    };
    public string StatusText => _statusText;
    public string ErrorText => _errorText;
    public bool CanStart => ModuleEnabled && !_isSaving && !_recorder.IsRecording && SelectedCount < SelectedTarget;
    public bool CanFinish => !_isSaving && _recorder.IsRecording;
    public bool CanReset => !_isSaving && !_recorder.IsRecording;
    public string CaptureMetricsText => _recorder.IsRecording
        ? $"Recording · {_recorder.EventCount:N0} events · {_recorder.Duration.TotalSeconds:F1}s" + (_recorder.DroppedEventCount > 0 ? $" · {_recorder.DroppedEventCount:N0} dropped due to the limit" : string.Empty)
        : "Use F8 to start and finish without including the return click. Cancel discards everything.";
    private int SelectedTarget => _selected switch { FreeSpinCalibrationPhase.Lift => _liftTarget, FreeSpinCalibrationPhase.Landing => _landingTarget, FreeSpinCalibrationPhase.Reposition => _repositionTarget, _ => _legitimateTarget };
    private int SelectedCount => _selected switch { FreeSpinCalibrationPhase.Lift => _liftCount, FreeSpinCalibrationPhase.Landing => _landingCount, FreeSpinCalibrationPhase.Reposition => _repositionCount, _ => _legitimateCount };

    private void ModuleSwitch_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (!ModuleEnabled && _recorder.IsRecording) { _recorder.Cancel(); _statusText = "Capture discarded because the module was disabled."; }
        _saveModuleEnabled(ModuleEnabled); RaiseRecordingProperties(); Raise(nameof(StatusText));
    }
    private void Phase_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse(tag, out FreeSpinCalibrationPhase next) || _recorder.IsRecording) return;
        _selected = next; SyncSelectedTarget(); RaiseSelectionProperties();
    }
    private void Start_OnClick(object sender, RoutedEventArgs e) => StartCapture();
    private async void Finish_OnClick(object sender, RoutedEventArgs e) => await FinishCaptureAsync();
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => CancelCapture();

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || _isSaving) return;
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            if (_recorder.IsRecording) _ = FinishCaptureAsync();
            else if (CanStart) StartCapture();
        }
        else if (e.Key == Key.Escape && _recorder.IsRecording)
        {
            e.Handled = true;
            CancelCapture();
        }
    }

    private void StartCapture()
    {
        if (!ModuleEnabled)
        {
            _statusText = "Enable the module before starting a capture.";
            RaiseRecordingProperties();
            Raise(nameof(StatusText));
            return;
        }
        ApplyTarget(); _errorText = string.Empty;
        if (!CanStart)
        {
            _statusText = SelectedCount >= SelectedTarget
                ? "Target reached for this phase. Adjust the target or use Reset phase before recording another sample."
                : "Wait for the current save to finish.";
            RaiseRecordingProperties(); Raise(nameof(StatusText)); return;
        }
        try { _recorder.Start(_selected, SelectedTarget); _statusText = "Recording: perform exactly one gesture and press F8 to finish without adding a click to the sample."; _ticker.Start(); }
        catch (Exception ex) { _errorText = "Could not start the capture: " + ex.Message; }
        RaiseRecordingProperties(); Raise(nameof(StatusText)); Raise(nameof(ErrorText));
    }

    private async Task FinishCaptureAsync()
    {
        if (!CanFinish) return;
        _isSaving = true; RaiseRecordingProperties();
        try
        {
            _statusText = "Saving the sample outside the hook…"; Raise(nameof(StatusText));
            var result = await _recorder.FinishAsync();
            if (!result.WasSaved)
            {
                _statusText = result.Message ?? "The capture is not valid yet.";
                return;
            }
            RefreshCounts();
            _statusText = "Sample saved. " + (SelectedCount >= SelectedTarget ? "Target reached." : "Ready for the next sample with F8.");
        }
        catch (Exception ex) { _errorText = "The capture ended, but the sample could not be persisted: " + ex.Message; }
        finally { _isSaving = false; if (!_recorder.IsRecording) _ticker.Stop(); RaiseRecordingProperties(); RaiseSelectionProperties(); Raise(nameof(StatusText)); Raise(nameof(ErrorText)); }
    }

    private void CancelCapture()
    {
        if (_isSaving) return;
        _recorder.Cancel(); _ticker.Stop(); _statusText = "Capture cancelled; no sample was saved."; RaiseRecordingProperties(); Raise(nameof(StatusText));
    }
    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Delete only the samples for {SelectedStageTitle}? This does not affect the other phases.", "Confirm reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { _recorder.Reset(_selected); RefreshCounts(); _statusText = "Samples for this phase were removed."; _errorText = string.Empty; } catch (Exception ex) { _errorText = "Could not reset this phase: " + ex.Message; }
        RaiseSelectionProperties(); Raise(nameof(StatusText)); Raise(nameof(ErrorText));
    }
    private void Target_OnLostFocus(object sender, RoutedEventArgs e) => ApplyTarget();
    private void Target_OnKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyTarget(); Keyboard.ClearFocus(); } }
    private void ApplyTarget()
    {
        var value = int.TryParse(_selectedTargetText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Clamp(parsed) : SelectedTarget;
        SetTarget(_selected, value); _selectedTargetText = value.ToString(CultureInfo.InvariantCulture); _saveTarget(_selected, value);
        _statusText = SelectedCount >= value ? "Target reached for this phase." : "Target updated."; RaiseSelectionProperties(); Raise(nameof(StatusText));
    }
    private void Window_OnClosed(object? sender, EventArgs e) { if (_recorder.IsRecording) _recorder.Cancel(); _ticker.Stop(); }
    private void RefreshCounts() { _liftCount = _recorder.Count(FreeSpinCalibrationPhase.Lift); _landingCount = _recorder.Count(FreeSpinCalibrationPhase.Landing); _repositionCount = _recorder.Count(FreeSpinCalibrationPhase.Reposition); _legitimateCount = _recorder.Count(FreeSpinCalibrationPhase.LegitimateScroll); }
    private void SetTarget(FreeSpinCalibrationPhase phase, int value) { switch (phase) { case FreeSpinCalibrationPhase.Lift: _liftTarget = value; break; case FreeSpinCalibrationPhase.Landing: _landingTarget = value; break; case FreeSpinCalibrationPhase.Reposition: _repositionTarget = value; break; default: _legitimateTarget = value; break; } }
    private void SyncSelectedTarget() => _selectedTargetText = SelectedTarget.ToString(CultureInfo.InvariantCulture);
    private void RaiseSelectionProperties() { foreach (var name in new[] { nameof(IsLiftSelected), nameof(IsLandingSelected), nameof(IsRepositionSelected), nameof(IsLegitimateSelected), nameof(SelectedStageTitle), nameof(SelectedInstructions), nameof(SelectedTargetText), nameof(SelectedCountText), nameof(LiftCountText), nameof(LandingCountText), nameof(RepositionCountText), nameof(LegitimateCountText), nameof(CanStart), nameof(CanReset) }) Raise(name); }
    private void RaiseRecordingProperties() { Raise(nameof(CanStart)); Raise(nameof(CanFinish)); Raise(nameof(CanReset)); Raise(nameof(CaptureMetricsText)); }
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private static string CountText(int count, int target) => $"{count:N0} / {target:N0} samples" + (count >= target ? " · complete" : string.Empty);
    private static int Clamp(int value) => Math.Max(30, Math.Min(50, value));
}
