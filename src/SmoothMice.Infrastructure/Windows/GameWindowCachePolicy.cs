namespace SmoothMice.Infrastructure.Windows;

/// <summary>
/// Pure validity policy for cached root-window game signals. A short lifetime plus root/PID and
/// live-window checks avoids trusting a recycled HWND or stale fullscreen state indefinitely.
/// Failed probes are deliberately never reusable, so transient Win32 failures recover on the
/// next physical wheel event.
/// </summary>
public static class GameWindowCachePolicy
{
    public const long TimeToLiveMs = 750;

    public static bool CanReuse(
        IntPtr cachedRootHwnd,
        uint cachedProcessId,
        bool cacheIsComplete,
        long cachedAtMs,
        IntPtr currentRootHwnd,
        uint currentProcessId,
        bool currentRootIsWindow,
        long nowMs)
    {
        if (!cacheIsComplete || !currentRootIsWindow ||
            cachedRootHwnd == IntPtr.Zero || currentRootHwnd == IntPtr.Zero ||
            cachedRootHwnd != currentRootHwnd || cachedProcessId != currentProcessId)
            return false;

        var ageMs = nowMs - cachedAtMs;
        return ageMs >= 0 && ageMs < TimeToLiveMs;
    }
}
