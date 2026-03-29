namespace Winix.Peep;

/// <summary>
/// Wraps <see cref="PeriodicTimer"/> with reset support. When a file-change trigger causes
/// a run, calling <see cref="Reset"/> restarts the interval so the next tick is a full
/// interval away, preventing double-fires in combined mode.
/// </summary>
public sealed class IntervalScheduler : IDisposable
{
    private readonly TimeSpan _interval;
    private PeriodicTimer _timer;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new interval scheduler with the specified interval.
    /// </summary>
    /// <param name="interval">Time between ticks.</param>
    public IntervalScheduler(TimeSpan interval)
    {
        _interval = interval;
        _timer = new PeriodicTimer(interval);
    }

    /// <summary>
    /// The configured interval between ticks.
    /// </summary>
    public TimeSpan Interval => _interval;

    /// <summary>
    /// Asynchronously waits for the next tick. Returns false if the scheduler has been disposed.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>True if a tick occurred; false if the scheduler was disposed.</returns>
    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
        PeriodicTimer timer;
        lock (_lock)
        {
            if (_disposed)
            {
                return false;
            }
            timer = _timer;
        }

        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resets the interval by disposing the current timer and creating a new one.
    /// The next tick will be a full interval from now. This is used after a file-change
    /// trigger to prevent a double-fire where the interval would have expired shortly after.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _timer.Dispose();
            _timer = new PeriodicTimer(_interval);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer.Dispose();
        }
    }
}
