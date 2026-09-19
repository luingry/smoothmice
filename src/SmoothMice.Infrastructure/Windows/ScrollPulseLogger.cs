using System.Collections.Concurrent;
using System.Diagnostics;
using Newtonsoft.Json;
using SmoothMice.Core.Diagnostics;

namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Optional, bounded asynchronous recorder for raw physical wheel pulses. It deliberately does
/// no file I/O on the low-level mouse hook callback.
/// </summary>
public sealed class ScrollPulseLogger : IDisposable
{
    private const int QueueCapacity = 4_096;
    private const int MaximumRecords = 100_000;

    private readonly BlockingCollection<ScrollPulseDiagnosticPulse> _pulses;
    private readonly string _path;
    private readonly Task _writer;
    private int _droppedPulses;
    private int _disposed;

    private ScrollPulseLogger(string path)
    {
        _path = path;
        _pulses = new BlockingCollection<ScrollPulseDiagnosticPulse>(
            new ConcurrentQueue<ScrollPulseDiagnosticPulse>(), QueueCapacity);
        _writer = Task.Run(Write);
    }

    public static ScrollPulseLogger? TryStartDefault()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmoothMice", "Diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"scroll-pulses-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss.fffZ}-{Guid.NewGuid():N}.ndjson");
            return new ScrollPulseLogger(path);
        }
        catch
        {
            // Diagnostics are best-effort and must never prevent normal scroll processing.
            return null;
        }
    }

    public void Record(ScrollPulseDiagnosticPulse pulse)
    {
        try
        {
            if (!_pulses.TryAdd(pulse))
                Interlocked.Increment(ref _droppedPulses);
        }
        catch
        {
            // The hook must remain transparent even if diagnostic collection fails unexpectedly.
        }
    }

    private void Write()
    {
        try
        {
            using var stream = new FileStream(
                _path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                bufferSize: 16 * 1024, FileOptions.SequentialScan);
            using var writer = new StreamWriter(stream) { AutoFlush = false };
            var analyzer = new ScrollPulseDiagnosticAnalyzer(Stopwatch.Frequency);

            writer.WriteLine(JsonConvert.SerializeObject(new
            {
                kind = "session_start",
                schema = 1,
                utc = DateTimeOffset.UtcNow.ToString("O"),
                monotonic_frequency = Stopwatch.Frequency,
                burst_window_ms = ScrollPulseDiagnosticAnalyzer.BurstWindowMilliseconds,
                burst_pulse_threshold = ScrollPulseDiagnosticAnalyzer.BurstPulseThreshold,
                rapid_reversal_window_ms = ScrollPulseDiagnosticAnalyzer.RapidReversalWindowMilliseconds,
                max_records = MaximumRecords,
            }));

            var written = 0;
            foreach (var pulse in _pulses.GetConsumingEnumerable())
            {
                if (written >= MaximumRecords)
                {
                    Interlocked.Increment(ref _droppedPulses);
                    continue;
                }

                writer.WriteLine(ScrollPulseDiagnosticFormatter.FormatPulse(pulse, analyzer.Analyze(pulse)));
                written++;
                if (written % 128 == 0)
                    writer.Flush();
            }

            writer.WriteLine(JsonConvert.SerializeObject(new
            {
                kind = "session_end",
                utc = DateTimeOffset.UtcNow.ToString("O"),
                records_written = written,
                dropped_pulses = Volatile.Read(ref _droppedPulses),
                capped = written >= MaximumRecords,
            }));
            writer.Flush();
        }
        catch
        {
            // The background diagnostic writer is deliberately isolated from the hook and app flow.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _pulses.CompleteAdding();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown: do not turn diagnostic I/O into an application shutdown failure.
        }
    }
}
