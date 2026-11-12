namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>
/// A clock tests move by hand. Its timers are the system's, in real time, unless it was made
/// <see cref="WithTimers"/>: then they fire only when <see cref="Advance"/> moves the clock past them.
/// </summary>
public sealed class ManualClock(DateTimeOffset start, bool manualTimers = false) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    private static DateTimeOffset Start2026 => new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    public static ManualClock StartingAt2026() => new(Start2026);

    /// <summary>A clock at the same start whose timers wait for <see cref="Advance"/>.</summary>
    public static ManualClock WithTimers() => new(Start2026, manualTimers: true);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(t => t.DueAt <= _now)];
        }

        foreach (var timer in due)
        {
            timer.Fire(_now);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (!manualTimers)
        {
            return base.CreateTimer(callback, state, dueTime, period);
        }

        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                _period = period;
                clock._timers.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = clock._now + dueTime;
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        public void Fire(DateTimeOffset now)
        {
            lock (clock._gate)
            {
                if (!clock._timers.Remove(this))
                {
                    return; // Changed or disposed since Advance found it due.
                }

                if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
                {
                    DueAt = now + _period;
                    clock._timers.Add(this);
                }
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
