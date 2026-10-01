using SmoothMice.App;
using Xunit;

namespace SmoothMice.Core.Tests;

public class SingleInstanceGuardTests
{
    // A unique scope keeps tests away from a SmoothMice instance running on this machine.
    private static string Scope() => "Test" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Second_acquire_fails_while_the_first_owner_is_alive_and_succeeds_after_release()
    {
        var scope = Scope();
        Exception? failure = null;
        using var released = new ManualResetEventSlim();
        using var acquired = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            try
            {
                using var first = SingleInstanceGuard.TryAcquire(TimeSpan.Zero, () => { }, scope);
                Assert.NotNull(first);
                acquired.Set();
                released.Wait();
            }
            catch (Exception ex) { failure = ex; acquired.Set(); }
        });
        owner.Start();
        acquired.Wait();

        Assert.Null(SingleInstanceGuard.TryAcquire(TimeSpan.FromMilliseconds(50), () => { }, scope));
        released.Set();
        owner.Join();
        Assert.Null(failure);

        using var next = SingleInstanceGuard.TryAcquire(TimeSpan.FromSeconds(2), () => { }, scope);
        Assert.NotNull(next);
    }

    [Fact]
    public void Automatic_launch_waits_for_a_predecessor_that_is_shutting_down()
    {
        var scope = Scope();
        using var acquired = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var first = SingleInstanceGuard.TryAcquire(TimeSpan.Zero, () => { }, scope);
            acquired.Set();
            Thread.Sleep(300);
        });
        owner.Start();
        acquired.Wait();

        using var successor = SingleInstanceGuard.TryAcquire(TimeSpan.FromSeconds(5), () => { }, scope);
        Assert.NotNull(successor);
        owner.Join();
    }

    [Fact]
    public void Signal_reaches_the_running_owner()
    {
        var scope = Scope();
        using var activated = new ManualResetEventSlim();
        using var owner = SingleInstanceGuard.TryAcquire(TimeSpan.Zero, activated.Set, scope);
        Assert.NotNull(owner);
        SingleInstanceGuard.SignalActivate(scope);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
    }
}
