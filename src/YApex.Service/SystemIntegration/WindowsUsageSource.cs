// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;
using YApex.Core.Services;

namespace YApex.Service.SystemIntegration;

public sealed class WindowsUsageSource : BackgroundService, IUsageSource
{
    private const int MaximumProgramsPerSnapshot = 32;
    private readonly ILogger<WindowsUsageSource> _logger;
    private readonly NetworkUsageAccumulator _usage = new();
    private readonly ConcurrentDictionary<int, CachedProcessInfo> _processInfo = new();
    private int _captureState;

    public WindowsUsageSource(ILogger<WindowsUsageSource> logger)
    {
        _logger = logger;
    }

    public Task<UsageSnapshot> GetUsageAsync(DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => CreateSnapshot(periodStart, cancellationToken), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Interlocked.Exchange(ref _captureState, -1);
            return;
        }

        try
        {
            using var session = new TraceEventSession(
                "Y-Apex.NetworkUsage",
                TraceEventSessionOptions.NoRestartOnCreate)
            {
                BufferSizeMB = 64,
                BufferQuantumKB = 64
            };
            _ = session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            var source = session.Source;
            source.Kernel.TcpIpSend += data => RecordSent(data.ProcessID, data.size);
            source.Kernel.TcpIpRecv += data => RecordReceived(data.ProcessID, data.size);
            source.Kernel.TcpIpSendIPV6 += data => RecordSent(data.ProcessID, data.size);
            source.Kernel.TcpIpRecvIPV6 += data => RecordReceived(data.ProcessID, data.size);
            source.Kernel.UdpIpSend += data => RecordSent(data.ProcessID, data.size);
            source.Kernel.UdpIpRecv += data => RecordReceived(data.ProcessID, data.size);
            source.Kernel.UdpIpSendIPV6 += data => RecordSent(data.ProcessID, data.size);
            source.Kernel.UdpIpRecvIPV6 += data => RecordReceived(data.ProcessID, data.size);
            Interlocked.Exchange(ref _captureState, 1);

            using var cancellationRegistration = stoppingToken.Register(static state =>
            {
                try
                {
                    _ = ((TraceEventSession)state!).Stop(noThrow: true);
                }
                catch (Exception)
                {
                }
            }, session);
            await Task.Run(source.Process, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _captureState, 0);
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _captureState, -1);
            _logger.LogError(exception, "Local ETW network-usage collection could not be started or stopped cleanly.");
        }
    }

    private void RecordSent(int processId, int size)
    {
        if (size > 0)
        {
            _usage.Record(processId, size, 0, DateTimeOffset.UtcNow);
        }
    }

    private void RecordReceived(int processId, int size)
    {
        if (size > 0)
        {
            _usage.Record(processId, 0, size, DateTimeOffset.UtcNow);
        }
    }

    private UsageSnapshot CreateSnapshot(DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capturedAt = DateTimeOffset.UtcNow;
        var start = periodStart.ToUniversalTime();
        if (start > capturedAt)
        {
            start = capturedAt;
        }

        var totals = _usage.GetUsage(start, capturedAt);
        var programs = new List<ProgramUsage>(Math.Min(totals.Count, MaximumProgramsPerSnapshot));
        var totalSent = totals.Aggregate(0L, static (sum, item) => SaturatingAdd(sum, item.BytesSent));
        var totalReceived = totals.Aggregate(0L, static (sum, item) => SaturatingAdd(sum, item.BytesReceived));
        foreach (var total in totals.Take(MaximumProgramsPerSnapshot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = GetProcessInfo(total.ProcessId);
            programs.Add(new ProgramUsage(
                info.Path,
                info.Name,
                total.BytesSent,
                total.BytesReceived,
                start < _usage.StartedAt ? _usage.StartedAt : start,
                capturedAt));
        }

        return new UsageSnapshot(
            totalSent,
            totalReceived,
            programs,
            capturedAt,
            IsAvailable: Volatile.Read(ref _captureState) == 1,
            IsTruncated: _usage.IsTruncated || totals.Count > MaximumProgramsPerSnapshot,
            DataAvailableFrom: _usage.StartedAt);
    }

    private CachedProcessInfo GetProcessInfo(int processId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_processInfo.TryGetValue(processId, out var cached) && cached.ExpiresAt > now)
        {
            return cached;
        }

        CachedProcessInfo info;
        try
        {
            using var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            if (processName.Length > 128)
            {
                processName = processName[..128];
            }

            string path;
            try
            {
                var reportedPath = process.MainModule?.FileName;
                var normalizedPath = string.IsNullOrWhiteSpace(reportedPath)
                    ? null
                    : WindowsProgramPath.Normalize(reportedPath);
                path = normalizedPath is null
                    ? $"pid:{processId}"
                    : normalizedPath.Length > 512 ? normalizedPath[..512] : normalizedPath;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException or ArgumentException)
            {
                path = $"pid:{processId}";
            }

            info = new CachedProcessInfo(processName, path, now.AddSeconds(10));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            info = new CachedProcessInfo($"Unknown process ({processId})", $"pid:{processId}", now.AddSeconds(2));
        }

        _processInfo[processId] = info;
        if (_processInfo.Count > 4096)
        {
            foreach (var expired in _processInfo
                         .Where(pair => pair.Value.ExpiresAt <= now)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _processInfo.TryRemove(expired, out _);
            }
        }

        return info;
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;

    private sealed record CachedProcessInfo(string Name, string Path, DateTimeOffset ExpiresAt);
}
