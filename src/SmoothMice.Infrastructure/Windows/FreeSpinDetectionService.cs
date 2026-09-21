using System.Diagnostics;
using Newtonsoft.Json;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Loads and trains outside the low-level callback. The callback observes an immutable model and
/// bounded causal state only; any error is explicitly pass-through.
/// </summary>
public sealed class FreeSpinDetectionService : IDisposable
{
    private readonly MouseHookService _hook;
    private readonly FreeSpinCalibrationStorage _storage;
    private readonly FreeSpinCausalHistory _history = new();
    private readonly object _historyGate = new();
    private FreeSpinDetectionModel _model = FreeSpinDetectionModel.Train(Array.Empty<FreeSpinCalibrationSample>());
    private FreeSpinRuntimeSettings _settings = new(false, FreeSpinDetectionMode.ReadOnly, 90);
    private long _reloadGeneration;
    private bool _physicalSubscribed;
    private bool _disposed;

    public FreeSpinDetectionService(MouseHookService hook, FreeSpinCalibrationStorage? storage = null)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _storage = storage ?? new FreeSpinCalibrationStorage();
        // Subscribe before ScrollCoordinator is constructed so Suppress can stop an original wheel
        // before it reaches smoothing/injection. It stays fail-open while the module is disabled.
        _hook.MouseWheel += OnWheel;
    }

    public event EventHandler<FreeSpinDetectionDecision>? DecisionObserved;
    public string ModelSummary
    {
        get
        {
            var model = Volatile.Read(ref _model);
            var settings = Volatile.Read(ref _settings);
            if (settings.Mode == FreeSpinDetectionMode.Suppress && model.MaximumSupportedConfidence * 100 < settings.ThresholdPercent)
                return $"Insufficient evidence for the {settings.ThresholdPercent}% threshold; suppression is disabled and events pass.";
            return model.Summary;
        }
    }
    public FreeSpinDetectionModel Model => Volatile.Read(ref _model);

    public void Configure(bool enabled, FreeSpinDetectionMode mode, int thresholdPercent)
    {
        var previous = Volatile.Read(ref _settings);
        Volatile.Write(ref _settings, new FreeSpinRuntimeSettings(enabled, mode, Math.Max(50, Math.Min(99, thresholdPercent))));
        if (previous.Enabled != enabled || previous.Mode != mode)
        {
            // A new policy must never inherit a <2s wheel/move context from the old policy.
            lock (_historyGate) _history.Reset();
        }
        // XY/button objects are not produced by MouseHookService unless a live feature consumer is active.
        // This subscription switch happens off the callback and cannot make a physical event block.
        if (enabled && !_physicalSubscribed) { _hook.PhysicalInputCaptured += OnPhysicalInput; _physicalSubscribed = true; }
        else if (!enabled && _physicalSubscribed) { _hook.PhysicalInputCaptured -= OnPhysicalInput; _physicalSubscribed = false; }
    }

    public async Task ReloadAsync()
    {
        // Advance before the first await. A reset/save can start another reload while disk I/O
        // or model fitting is in flight; only the most recently requested generation may publish.
        var generation = Interlocked.Increment(ref _reloadGeneration);
        try
        {
            var samples = await Task.Run(LoadSamples).ConfigureAwait(true);
            var next = await Task.Run(() => FreeSpinDetectionModel.Train(samples)).ConfigureAwait(true);
            if (Volatile.Read(ref _reloadGeneration) == generation)
                Volatile.Write(ref _model, next);
        }
        catch
        {
            // A corrupt/missing data directory never affects input. Empty model means abstain.
            if (Volatile.Read(ref _reloadGeneration) == generation)
                Volatile.Write(ref _model, FreeSpinDetectionModel.Train(Array.Empty<FreeSpinCalibrationSample>()));
        }
    }

    private List<FreeSpinCalibrationSample> LoadSamples()
    {
        var result = new List<FreeSpinCalibrationSample>();
        if (!Directory.Exists(_storage.RootPath)) return result;
        foreach (var path in Directory.EnumerateFiles(_storage.RootPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var sample = JsonConvert.DeserializeObject<FreeSpinCalibrationSample>(File.ReadAllText(path));
                if (sample is not null && FreeSpinCalibrationValidation.TryValidate(sample, out _)) result.Add(sample);
            }
            catch { /* individual corrupt files are ignored */ }
        }
        return result;
    }

    private void OnPhysicalInput(object? sender, FreeSpinPhysicalInputEventArgs e)
    {
        if (Volatile.Read(ref _settings).Enabled == false || e.Input.Kind == FreeSpinRawEventKind.Wheel) return;
        try { lock (_historyGate) _history.Observe(e.Input); } catch { }
    }

    private void OnWheel(object? sender, MouseWheelHookEventArgs e)
    {
        var settings = Volatile.Read(ref _settings);
        if (!settings.Enabled) return;
        FreeSpinDetectionDecision decision;
        try
        {
            lock (_historyGate)
            {
                var ticks = Stopwatch.GetTimestamp();
                var context = _history.CreateForWheel(ticks, e.Delta, e.IsHorizontal);
                decision = Volatile.Read(ref _model).Evaluate(context, settings.Mode, settings.ThresholdPercent);
                _history.CommitWheel(ticks, e.Delta);
            }
            if (settings.Mode == FreeSpinDetectionMode.Suppress && decision.ShouldSuppress)
                e.Handled = true;
        }
        catch { decision = new FreeSpinDetectionDecision { Verdict = FreeSpinVerdict.Abstain, Reason = "Detector error; event passed." }; }
        try { DecisionObserved?.Invoke(this, decision); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook.MouseWheel -= OnWheel;
        if (_physicalSubscribed) _hook.PhysicalInputCaptured -= OnPhysicalInput;
    }

    private sealed class FreeSpinRuntimeSettings
    {
        public FreeSpinRuntimeSettings(bool enabled, FreeSpinDetectionMode mode, int thresholdPercent) { Enabled = enabled; Mode = mode; ThresholdPercent = thresholdPercent; }
        public bool Enabled { get; }
        public FreeSpinDetectionMode Mode { get; }
        public int ThresholdPercent { get; }
    }
}
