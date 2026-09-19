using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SmoothMice.App.ViewModels;
using SmoothMice.Core.Diagnostics;
using SmoothMice.Infrastructure.Windows;

namespace SmoothMice.App;

/// <summary>
/// A non-modal diagnostic view. The low-level hook only adds to its bounded queue; WPF drains
/// that queue in small batches on the dispatcher, keeping input processing independent of UI work.
/// </summary>
public partial class ScrollPulseMonitorWindow : Window
{
    private const int QueueCapacity = 1_024;
    private const int MaximumDrainPerTick = 128;

    private readonly MouseHookService _hook;
    private readonly BlockingCollection<ScrollPulseDiagnosticPulse> _pending = new(
        new ConcurrentQueue<ScrollPulseDiagnosticPulse>(), QueueCapacity);
    private readonly DispatcherTimer _drainTimer;
    private readonly ScrollPulseMonitorViewModel _viewModel = new();
    private int _ingressDropped;
    private bool _subscribed;

    public ScrollPulseMonitorWindow(MouseHookService hook)
    {
        _hook = hook;
        DataContext = _viewModel;
        InitializeComponent();
        _drainTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _drainTimer.Tick += DrainPending;
    }

    private void Monitor_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _hook.ScrollPulseCaptured += OnPulseCaptured;
            _subscribed = true;
        }
        _drainTimer.Start();
        Activate();
    }

    private void OnPulseCaptured(object? sender, ScrollPulseCapturedEventArgs e)
    {
        try
        {
            if (!_pending.TryAdd(e.Pulse))
                System.Threading.Interlocked.Increment(ref _ingressDropped);
        }
        catch
        {
            // A closed monitor is never allowed to disturb the physical input hook.
        }
    }

    private void DrainPending(object? sender, EventArgs e)
    {
        var drained = 0;
        while (drained < MaximumDrainPerTick && _pending.TryTake(out var pulse))
        {
            _viewModel.Add(pulse);
            drained++;
        }
        _viewModel.SetIngressDropped(System.Threading.Volatile.Read(ref _ingressDropped));

        if (drained > 0)
            PulseList.ScrollIntoView(PulseList.Items[PulseList.Items.Count - 1]);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        while (_pending.TryTake(out _)) { }
        System.Threading.Interlocked.Exchange(ref _ingressDropped, 0);
        _viewModel.Clear();
    }

    private void Monitor_OnClosed(object? sender, EventArgs e)
    {
        _drainTimer.Stop();
        if (_subscribed)
        {
            _hook.ScrollPulseCaptured -= OnPulseCaptured;
            _subscribed = false;
        }
        _pending.CompleteAdding();
    }
}
