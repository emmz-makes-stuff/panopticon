namespace Panopticon.Server.Tests.Infrastructure;

/// <summary>A clock tests move by hand.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public static ManualClock StartingAt2026() => new(new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
