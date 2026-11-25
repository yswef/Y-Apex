// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Services;
using Xunit;

namespace YApex.Core.Tests;

public sealed class NetworkUsageAccumulatorTests
{
    [Fact]
    public void AccumulatesSentAndReceivedBytesPerProcessAndDay()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var accumulator = new NetworkUsageAccumulator(start);
        accumulator.Record(42, 100, 0, start);
        accumulator.Record(42, 0, 75, start.AddMinutes(1));
        accumulator.Record(7, 20, 30, start.AddDays(1));

        var dayOne = new DateTimeOffset(start.UtcDateTime.Date, TimeSpan.Zero);
        var dayTwo = dayOne.AddDays(1);
        var today = accumulator.GetUsage(dayOne, dayOne);
        var nextDay = accumulator.GetUsage(dayTwo, dayTwo);

        var process = Assert.Single(today);
        Assert.Equal(42, process.ProcessId);
        Assert.Equal(100, process.BytesSent);
        Assert.Equal(75, process.BytesReceived);
        var nextProcess = Assert.Single(nextDay);
        Assert.Equal(7, nextProcess.ProcessId);
        Assert.Equal(20, nextProcess.BytesSent);
        Assert.Equal(30, nextProcess.BytesReceived);
    }

    [Fact]
    public void IgnoresInvalidProcessIdsAndNegativeByteCounts()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var accumulator = new NetworkUsageAccumulator(start);
        accumulator.Record(0, 100, 0, start);
        accumulator.Record(-1, 0, 100, start);
        accumulator.Record(2, -1, 0, start);

        Assert.Empty(accumulator.GetUsage(start, start));
    }

    [Fact]
    public void ConcurrentRecordsCannotExceedTheBucketLimitAndReportTruncation()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var accumulator = new NetworkUsageAccumulator(start);

        Parallel.For(1, 100_002, processId => accumulator.Record(processId, 1, 0, start));

        Assert.Equal(100_000, accumulator.GetUsage(start, start).Count);
        Assert.True(accumulator.IsTruncated);
    }

    [Fact]
    public void SaturatesCountersInsteadOfWrappingAround()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var accumulator = new NetworkUsageAccumulator(start);
        accumulator.Record(10, long.MaxValue, 0, start);
        accumulator.Record(10, 1, 0, start);

        var process = Assert.Single(accumulator.GetUsage(start, start));

        Assert.Equal(long.MaxValue, process.BytesSent);
    }
}
