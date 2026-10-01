using System;
using System.Threading;

namespace SmoothMice.App;

/// <summary>
/// One SmoothMice per user session. A second process would install a second mouse hook and, on
/// exit, overwrite settings.json with its own stale snapshot; it must exit before loading or
/// saving anything.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly RegisteredWaitHandle _registration;
    private int _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activate, Action onActivate)
    {
        _mutex = mutex;
        _activate = activate;
        _registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static string MutexName(string scope) => $@"Local\SmoothMice.{scope}.SingleInstance";
    public static string ActivateEventName(string scope) => $@"Local\SmoothMice.{scope}.Activate";

    /// <summary>
    /// Returns null when another instance keeps ownership for <paramref name="wait"/>. Automatic
    /// launches pass a wait so a relaunch can outlive a predecessor that is still shutting down.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(TimeSpan wait, Action onActivate, string scope = "App")
    {
        var mutex = new Mutex(false, MutexName(scope));
        bool owned;
        try { owned = mutex.WaitOne(wait); }
        catch (AbandonedMutexException) { owned = true; } // previous owner crashed: we now own it
        if (!owned)
        {
            mutex.Dispose();
            return null;
        }
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName(scope));
        return new SingleInstanceGuard(mutex, activate, onActivate);
    }

    /// <summary>Asks the running instance to show its window.</summary>
    public static void SignalActivate(string scope = "App")
    {
        try
        {
            using var activate = EventWaitHandle.OpenExisting(ActivateEventName(scope));
            activate.Set();
        }
        catch
        {
            // The owner may be starting or exiting; the second launch still must not continue.
        }
    }

    /// <summary>Releases ownership early, e.g. before deliberately launching a successor.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registration.Unregister(null);
        _activate.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* released on a different thread */ }
        _mutex.Dispose();
    }
}
