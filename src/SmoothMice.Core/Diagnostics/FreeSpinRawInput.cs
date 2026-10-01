namespace SmoothMice.Core.Diagnostics;

public enum FreeSpinRawEventKind
{
    Move,
    Button,
    Wheel,
}

public enum FreeSpinMouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>Small immutable-at-ingress representation of a physical hook event.</summary>
public sealed class FreeSpinRawInputEvent
{
    public FreeSpinRawEventKind Kind { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public long StopwatchTicks { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int WheelDelta { get; set; }
    public string? WheelAxis { get; set; }
    public FreeSpinMouseButton Button { get; set; }
    public bool IsButtonDown { get; set; }
}
