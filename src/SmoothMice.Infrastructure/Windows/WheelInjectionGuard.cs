namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Epoch stamps survive Windows' truncation of dwExtraInfo to 32 bits. Validation happens in
/// WH_MOUSE_LL, after SendInput has begun, so reversal can reject an already-calculated tick
/// without making the physical hook wait for the native injection call.
/// </summary>
internal sealed class WheelInjectionGuard
{
    private readonly object _gate = new();
    // A per-instance namespace avoids one running instance rejecting another's injections.
    // Keep the sign bit clear for IntPtr on x86, and avoid Winput LAN's reserved namespace.
    private readonly int _prefix = CreatePrefix();
    private int _serial, _verticalStamp, _horizontalStamp;
    private int _verticalDirection, _horizontalDirection;
    private IntPtr _target;

    private static int CreatePrefix()
    {
        int prefix;
        do { prefix = Guid.NewGuid().GetHashCode() & 0x7fff0000; }
        while (prefix == 0 || prefix == 0x4e500000);
        return prefix;
    }

    private int NextStamp() => _prefix | (++_serial & 0xffff);

    public void ObserveInput(IntPtr target, int delta, bool horizontal)
    {
        lock (_gate)
        {
            if (_target != target)
            {
                InvalidateCore();
                _target = target;
            }
            int direction = Math.Sign(delta);
            if (horizontal)
            {
                if (direction != _horizontalDirection) _horizontalStamp = NextStamp();
                _horizontalDirection = direction;
            }
            else
            {
                if (direction != _verticalDirection) _verticalStamp = NextStamp();
                _verticalDirection = direction;
            }
        }
    }

    public int Capture(bool horizontal)
    {
        lock (_gate) return horizontal ? _horizontalStamp : _verticalStamp;
    }

    public bool IsCurrent(int stamp, bool horizontal)
    {
        lock (_gate) return stamp != 0 && stamp == (horizontal ? _horizontalStamp : _verticalStamp);
    }

    public bool ShouldSuppress(IntPtr extraInfo, bool horizontal)
    {
        var stamp = unchecked((int)(extraInfo.ToInt64() & 0xffffffff));
        if ((stamp & unchecked((int)0xffff0000)) != _prefix) return false;
        return !IsCurrent(stamp, horizontal);
    }

    public void Invalidate()
    {
        lock (_gate) InvalidateCore();
    }

    private void InvalidateCore()
    {
        _verticalStamp = _horizontalStamp = 0;
        _verticalDirection = _horizontalDirection = 0;
        _target = IntPtr.Zero;
    }
}
