using Panopticon.Server.Metrics;

namespace Panopticon.Server.Tests.Metrics;

// change-metrics D4: medians in code, Monday-start UTC weeks.
public class MetricsReaderTests
{
    [Theory]
    [InlineData(new long[] { }, null)]
    [InlineData(new long[] { 7 }, 7L)]
    [InlineData(new long[] { 10, 2, 5 }, 5L)]
    [InlineData(new long[] { 10, 2, 5, 4 }, 4L)]
    [InlineData(new long[] { 1, 2 }, 1L)]
    public void Median_is_the_middle_value_or_the_mean_of_the_two_rounded_down(long[] values, long? expected)
    {
        Assert.Equal(expected, MetricsReader.Median(values));
    }

    [Theory]
    [InlineData("2026-11-09T00:00:00Z", "2026-11-09T00:00:00Z")]
    [InlineData("2026-11-15T23:59:59Z", "2026-11-09T00:00:00Z")]
    [InlineData("2026-11-08T23:59:59Z", "2026-11-02T00:00:00Z")]
    [InlineData("2026-11-09T01:00:00+02:00", "2026-11-02T00:00:00Z")]
    public void A_week_starts_on_the_monday_at_midnight_utc(string at, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), MetricsReader.WeekStart(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
