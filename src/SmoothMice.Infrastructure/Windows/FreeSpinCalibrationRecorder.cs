using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>Owns exactly one bounded in-memory collection and persists only after Finish.</summary>
public sealed class FreeSpinCalibrationRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly MouseHookService _hook;
    private readonly FreeSpinCalibrationStorage _storage;
    private FreeSpinCalibrationSession? _active;
    private bool _subscribed;

    public FreeSpinCalibrationRecorder(MouseHookService hook, FreeSpinCalibrationStorage? storage = null)
    {
        _hook = hook ?? throw new ArgumentNullException(nameof(hook));
        _storage = storage ?? new FreeSpinCalibrationStorage();
    }

    public bool IsRecording { get { lock (_gate) return _active is not null; } }
    /// <summary>Shared pipeline used by the optional live detector; never exposes hook control.</summary>
    public MouseHookService HookService => _hook;
    public int EventCount { get { lock (_gate) return _active?.EventCount ?? 0; } }
    public int DroppedEventCount { get { lock (_gate) return _active?.DroppedEventCount ?? 0; } }
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                if (_active is null) return TimeSpan.Zero;
                return TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - _active.StartedStopwatchTicks) / (double)Stopwatch.Frequency);
            }
        }
    }

    public void Start(FreeSpinCalibrationPhase phase, int target)
    {
        lock (_gate)
        {
            if (_active is not null)
                throw new InvalidOperationException("A calibration capture is already in progress.");
            _active = new FreeSpinCalibrationSession(phase, target, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
            _hook.PhysicalInputCaptured += OnPhysicalInput;
            _subscribed = true;
        }
    }

    public async Task<FreeSpinCalibrationFinishResult> FinishAsync()
    {
        FreeSpinCalibrationSample? sample;
        lock (_gate)
        {
            if (_active is null)
                return FreeSpinCalibrationFinishResult.NotRecording;

            var completedTicks = Stopwatch.GetTimestamp();
            if (!_active.TryValidateCompletion(completedTicks, out var validationMessage))
                return FreeSpinCalibrationFinishResult.Rejected(validationMessage!);

            sample = _active.Complete(DateTimeOffset.UtcNow, completedTicks);
            StopCaptureLocked();
        }
        if (sample is not null)
            await Task.Run(() => _storage.Save(sample)).ConfigureAwait(true);
        return FreeSpinCalibrationFinishResult.Saved(sample!);
    }

    public void Cancel()
    {
        lock (_gate)
            StopCaptureLocked();
    }

    public int Count(FreeSpinCalibrationPhase phase) => _storage.Count(phase);
    public void Reset(FreeSpinCalibrationPhase phase) => _storage.Reset(phase);

    private void OnPhysicalInput(object? sender, FreeSpinPhysicalInputEventArgs e)
    {
        lock (_gate)
            _active?.Record(e.Input);
    }

    private void StopCaptureLocked()
    {
        _active = null;
        if (_subscribed)
        {
            _hook.PhysicalInputCaptured -= OnPhysicalInput;
            _subscribed = false;
        }
    }

    public void Dispose() => Cancel();
}

public sealed class FreeSpinCalibrationFinishResult
{
    private FreeSpinCalibrationFinishResult(FreeSpinCalibrationSample? sample, string? message)
    {
        Sample = sample;
        Message = message;
    }

    public static FreeSpinCalibrationFinishResult NotRecording { get; } = new(null, "No capture was active.");
    public FreeSpinCalibrationSample? Sample { get; }
    public string? Message { get; }
    public bool WasSaved => Sample is not null;
    public static FreeSpinCalibrationFinishResult Saved(FreeSpinCalibrationSample sample) => new(sample, null);
    public static FreeSpinCalibrationFinishResult Rejected(string message) => new(null, message);
}

public sealed class FreeSpinCalibrationStorage
{
    private readonly object _gate = new();
    private static readonly JsonSerializerSettings JsonOptions = new()
    {
        Formatting = Formatting.Indented,
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = [new StringEnumConverter()],
    };

    public FreeSpinCalibrationStorage(string? rootPath = null) => RootPath = rootPath ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmoothMice", "FreeSpinCalibration", "v1");

    public string RootPath { get; }

    public void Save(FreeSpinCalibrationSample sample)
    {
        if (!FreeSpinCalibrationValidation.TryValidate(sample, out var validationMessage))
            throw new InvalidDataException(validationMessage);
        lock (_gate)
        {
            var directory = CategoryDirectory(ParsePhase(sample.Phase));
            Directory.CreateDirectory(directory);
            var name = sample.CompletedUtc.UtcDateTime.ToString("yyyyMMddTHHmmssfffffff") + "-" + sample.Id + ".json";
            var path = Path.Combine(directory, name);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(sample, JsonOptions));
                File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
    }

    public int Count(FreeSpinCalibrationPhase phase)
    {
        lock (_gate)
        {
            var directory = CategoryDirectory(phase);
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count() : 0;
        }
    }

    public void Reset(FreeSpinCalibrationPhase phase)
    {
        lock (_gate)
        {
            var directory = CategoryDirectory(phase);
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                File.Delete(file);
        }
    }

    private string CategoryDirectory(FreeSpinCalibrationPhase phase) => Path.Combine(RootPath, FreeSpinCalibrationSession.ToStorageLabel(phase));

    private static FreeSpinCalibrationPhase ParsePhase(string phase) => phase switch
    {
        "lift" => FreeSpinCalibrationPhase.Lift,
        "landing" => FreeSpinCalibrationPhase.Landing,
        "reposition" => FreeSpinCalibrationPhase.Reposition,
        "legitimate-scroll" => FreeSpinCalibrationPhase.LegitimateScroll,
        _ => throw new InvalidDataException("Unknown free-spin calibration phase."),
    };
}
