using Newtonsoft.Json;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;
using Xunit;

namespace SmoothMice.Core.Tests;

public class FreeSpinCalibrationStorageTests
{
    [Fact]
    public void Save_count_reset_and_json_are_isolated_by_phase()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmoothMice.FreeSpinStorageTests", Guid.NewGuid().ToString("N"));
        var expectedRootPrefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SmoothMice.FreeSpinStorageTests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRootPrefix, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);

        try
        {
            var storage = new FreeSpinCalibrationStorage(root);
            var lift = CreateSample(FreeSpinCalibrationPhase.Lift, "lift-sample");
            var landing = CreateSample(FreeSpinCalibrationPhase.Landing, "landing-sample");
            storage.Save(lift);
            storage.Save(landing);

            Assert.Equal(1, storage.Count(FreeSpinCalibrationPhase.Lift));
            Assert.Equal(1, storage.Count(FreeSpinCalibrationPhase.Landing));

            var liftFile = Directory.EnumerateFiles(Path.Combine(root, "lift"), "*.json", SearchOption.TopDirectoryOnly).Single();
            var restored = JsonConvert.DeserializeObject<FreeSpinCalibrationSample>(File.ReadAllText(liftFile));
            Assert.NotNull(restored);
            Assert.Equal(FreeSpinCalibrationSample.CurrentSchemaVersion, restored!.SchemaVersion);
            Assert.Equal("lift", restored.Phase);
            Assert.Single(restored.Events);
            Assert.Equal("move", restored.Events[0].Kind);

            storage.Reset(FreeSpinCalibrationPhase.Lift);
            Assert.Equal(0, storage.Count(FreeSpinCalibrationPhase.Lift));
            Assert.Equal(1, storage.Count(FreeSpinCalibrationPhase.Landing));
        }
        finally
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(expectedRootPrefix, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejected_sample_never_reaches_storage_or_count()
    {
        var root = Path.Combine(Path.GetTempPath(), "SmoothMice.FreeSpinStorageTests", Guid.NewGuid().ToString("N"));
        var expectedRootPrefix = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SmoothMice.FreeSpinStorageTests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRootPrefix, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        try
        {
            var started = DateTimeOffset.UtcNow;
            var invalid = new FreeSpinCalibrationSession(FreeSpinCalibrationPhase.Lift, 40, started, 100)
                .Complete(started, 100);
            var storage = new FreeSpinCalibrationStorage(root);

            Assert.Throws<InvalidDataException>(() => storage.Save(invalid));
            Assert.Equal(0, storage.Count(FreeSpinCalibrationPhase.Lift));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(expectedRootPrefix, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }

    private static FreeSpinCalibrationSample CreateSample(FreeSpinCalibrationPhase phase, string id)
    {
        var started = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var session = new FreeSpinCalibrationSession(phase, 40, started, 10);
        session.Record(new FreeSpinRawInputEvent { Kind = FreeSpinRawEventKind.Move, TimestampUtc = started, StopwatchTicks = 11, X = 15, Y = 20 });
        var sample = session.Complete(started.AddSeconds(1), 10 + FreeSpinCalibrationSession.MinimumDurationStopwatchTicks);
        sample.Id = id;
        return sample;
    }
}
