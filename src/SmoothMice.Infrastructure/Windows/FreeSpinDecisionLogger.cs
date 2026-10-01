using System.Collections.Concurrent;
using Newtonsoft.Json;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Bounded asynchronous NDJSON log of live Free-Spin decisions, so live wheel contexts can be
/// compared with calibration samples. No file I/O happens on the hook callback.
/// </summary>
public sealed class FreeSpinDecisionLogger : IDisposable
{
    private const int QueueCapacity = 1_024;
    private const int MaximumRecords = 50_000;
    private readonly BlockingCollection<string> _lines = new(new ConcurrentQueue<string>(), QueueCapacity);
    private readonly Task _writer;
    private int _disposed;

    private FreeSpinDecisionLogger(string path)
    {
        Path = path;
        _writer = Task.Run(Write);
    }

    public string Path { get; }

    public static FreeSpinDecisionLogger? TryStartDefault(string? directoryOverride = null)
    {
        try
        {
            var directory = directoryOverride ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmoothMice", "Diagnostics");
            Directory.CreateDirectory(directory);
            return new FreeSpinDecisionLogger(System.IO.Path.Combine(directory, $"free-spin-live-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}.ndjson"));
        }
        catch { return null; }
    }

    public void Record(int delta, bool horizontal, double movementPx, double? moveAgeMs, FreeSpinDetectionDecision decision)
    {
        try
        {
            // Serialization is small and bounded; the write is not.
            var line = JsonConvert.SerializeObject(new
            {
                utc = DateTimeOffset.UtcNow.ToString("O"),
                delta,
                horizontal,
                movementPx = Math.Round(movementPx, 1),
                moveAgeMs = moveAgeMs is double age ? Math.Round(age, 1) : (double?)null,
                inertia = decision.Eligible,
                suppressed = decision.ShouldSuppress,
                reason = decision.Reason,
            });
            _lines.TryAdd(line);
        }
        catch { /* diagnostics never affect input */ }
    }

    private void Write()
    {
        try
        {
            using var writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            var written = 0;
            foreach (var line in _lines.GetConsumingEnumerable())
            {
                if (written++ >= MaximumRecords) continue;
                writer.WriteLine(line);
                writer.Flush();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lines.CompleteAdding();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
