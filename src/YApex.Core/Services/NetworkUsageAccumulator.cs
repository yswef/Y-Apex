// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Collections.Concurrent;

namespace YApex.Core.Services;

public sealed record ProcessUsageTotals(int ProcessId, long BytesSent, long BytesReceived);

/// <summary>Thread-safe, bounded daily byte counters shared by the ETW collector and its tests.</summary>
public sealed class NetworkUsageAccumulator
{
    private const int MaximumBuckets = 100_000;
    private const int RetentionDays = 31;
    private const int PruneIntervalEvents = 4096;
    private readonly ConcurrentDictionary<BucketKey, UsageCounter> _buckets = new();
    private readonly object _bucketGate = new();
    private long _eventCount;
    private long _lastPruneDayTicks = long.MinValue;
    private int _isTruncated;

    public NetworkUsageAccumulator(DateTimeOffset? startedAt = null)
    {
        StartedAt = (startedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public DateTimeOffset StartedAt { get; }

    public bool IsTruncated => Volatile.Read(ref _isTruncated) != 0;

    public void Record(int processId, long bytesSent, long bytesReceived, DateTimeOffset timestamp)
    {
        if (processId <= 0 || bytesSent < 0 || bytesReceived < 0 || (bytesSent == 0 && bytesReceived == 0))
        {
            return;
        }

        var utcTimestamp = timestamp.ToUniversalTime();
        var key = new BucketKey(processId, utcTimestamp.UtcDateTime.Date.Ticks);
        lock (_bucketGate)
        {
            if (!_buckets.TryGetValue(key, out var counter))
            {
                var dayTicks = utcTimestamp.UtcDateTime.Date.Ticks;
                if (_buckets.Count >= MaximumBuckets && _lastPruneDayTicks != dayTicks)
                {
                    PruneExpired(utcTimestamp);
                }

                if (_buckets.Count >= MaximumBuckets)
                {
                    Interlocked.Exchange(ref _isTruncated, 1);
                    return;
                }

                counter = _buckets.GetOrAdd(key, static _ => new UsageCounter());
            }

            counter.Add(bytesSent, bytesReceived);
            if (Interlocked.Increment(ref _eventCount) % PruneIntervalEvents == 0)
            {
                PruneExpired(utcTimestamp);
            }
        }
    }

    public IReadOnlyList<ProcessUsageTotals> GetUsage(DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        var startTicks = periodStart.ToUniversalTime().UtcDateTime.Date.Ticks;
        var endTicks = periodEnd.ToUniversalTime().UtcDateTime.Date.Ticks;
        if (startTicks > endTicks)
        {
            return Array.Empty<ProcessUsageTotals>();
        }

        return _buckets
            .Where(pair => pair.Key.DayTicks >= startTicks && pair.Key.DayTicks <= endTicks)
            .GroupBy(pair => pair.Key.ProcessId)
            .Select(group => new ProcessUsageTotals(
                group.Key,
                group.Aggregate(0L, static (sum, pair) => SaturatingAdd(sum, pair.Value.ReadSent())),
                group.Aggregate(0L, static (sum, pair) => SaturatingAdd(sum, pair.Value.ReadReceived()))))
            .OrderByDescending(item => SaturatingAdd(item.BytesSent, item.BytesReceived))
            .ThenBy(item => item.ProcessId)
            .ToArray();
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var cutoff = now.UtcDateTime.Date.AddDays(-RetentionDays).Ticks;
        foreach (var key in _buckets.Keys)
        {
            if (key.DayTicks < cutoff)
            {
                _buckets.TryRemove(key, out _);
            }
        }

        _lastPruneDayTicks = now.UtcDateTime.Date.Ticks;
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;

    private readonly record struct BucketKey(int ProcessId, long DayTicks);

    private sealed class UsageCounter
    {
        public long BytesSent;

        public long BytesReceived;

        public void Add(long bytesSent, long bytesReceived)
        {
            AddSaturating(ref BytesSent, bytesSent);
            AddSaturating(ref BytesReceived, bytesReceived);
        }

        public long ReadSent() => Volatile.Read(ref BytesSent);

        public long ReadReceived() => Volatile.Read(ref BytesReceived);

        private static void AddSaturating(ref long value, long amount)
        {
            while (true)
            {
                var current = Volatile.Read(ref value);
                var next = amount > 0 && current > long.MaxValue - amount
                    ? long.MaxValue
                    : current + amount;
                if (Interlocked.CompareExchange(ref value, next, current) == current)
                {
                    return;
                }
            }
        }
    }
}
