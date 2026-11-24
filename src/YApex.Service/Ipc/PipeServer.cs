// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Protocol;
using YApex.Core.Services;

namespace YApex.Service.Ipc;

public sealed class PipeServer : BackgroundService
{
    public const string PipeName = "Y-Apex.Control.v1";
    private const int MaximumRequestsPerMinute = 120;
    private const int MaximumConnectionSnapshots = 2;
    private const int MaximumConnectionRows = 250_000;
    private static readonly TimeSpan ConnectionSnapshotLifetime = TimeSpan.FromMinutes(5);
    private readonly ILogger<PipeServer> _logger;
    private readonly PipeAccessPolicy _accessPolicy;
    private readonly IConnectionSource _connections;
    private readonly IUsageSource _usage;
    private readonly IProgramIdentitySource _identities;
    private readonly IFirewallBackend _firewall;
    private readonly IChangeJournal _journal;
    private readonly RuleChangeCoordinator _changes;
    private readonly object _rateGate = new();
    private readonly object _snapshotGate = new();
    private readonly Dictionary<Guid, ConnectionSnapshot> _connectionSnapshots = new();
    private DateTimeOffset _rateWindowStart = DateTimeOffset.UtcNow;
    private int _requestCount;

    public PipeServer(
        ILogger<PipeServer> logger,
        PipeAccessPolicy accessPolicy,
        IConnectionSource connections,
        IUsageSource usage,
        IProgramIdentitySource identities,
        IFirewallBackend firewall,
        IChangeJournal journal,
        RuleChangeCoordinator changes)
    {
        _logger = logger;
        _accessPolicy = accessPolicy;
        _connections = connections;
        _usage = usage;
        _identities = identities;
        _firewall = firewall;
        _journal = journal;
        _changes = changes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _changes.RecoverInterruptedOperationsAsync(stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 4,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                PipeProtocol.MaximumMessageBytes + sizeof(int),
                PipeProtocol.MaximumMessageBytes + sizeof(int),
                _accessPolicy.CreatePipeSecurity());
            PipeRequest? request = null;

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                var clientSid = GetClientSid(pipe);
                if (!_accessPolicy.IsAllowed(clientSid) || !TryConsumeRateLimit())
                {
                    _logger.LogWarning("Rejected a pipe client because its SID or request rate was not allowed.");
                    continue;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                request = await PipeProtocol.ReadRequestAsync(pipe, timeout.Token).ConfigureAwait(false);
                var result = await HandleRequestAsync(request, timeout.Token).ConfigureAwait(false);
                await PipeProtocol.WriteResponseAsync(pipe, request.RequestId, true, null, result, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A Y-Apex pipe request was rejected.");
                if (request is not null && pipe.IsConnected)
                {
                    try
                    {
                        await PipeProtocol.WriteResponseAsync<object?>(
                            pipe,
                            request.RequestId,
                            false,
                            "The service could not process this request. Check the request and try again.",
                            null,
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception writeException) when (writeException is IOException or OperationCanceledException)
                    {
                        _logger.LogDebug(writeException, "Could not send a protocol error response.");
                    }
                }
            }
        }
    }

    private async Task<object?> HandleRequestAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case PipeCommands.GetConnections:
                return await GetConnectionsPageAsync(request, cancellationToken).ConfigureAwait(false);
            case PipeCommands.GetRules:
                return await _firewall.GetOwnedRulesAsync(cancellationToken).ConfigureAwait(false);
            case PipeCommands.GetUsage:
                return await _usage.GetUsageAsync(
                    new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero),
                    cancellationToken).ConfigureAwait(false);
            case PipeCommands.GetJournal:
                return await _journal.GetRecentAsync(cancellationToken).ConfigureAwait(false);
            case PipeCommands.GetOperations:
                return await _changes.GetActiveOperationsAsync(cancellationToken).ConfigureAwait(false);
            case PipeCommands.PreviewRule:
            {
                var normalizedPath = YApex.Core.Security.WindowsProgramPath.Normalize(request.ProgramPath!);
                var identity = await _identities.InspectAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
                return await _changes.CreatePreviewAsync(
                    normalizedPath,
                    request.Action == "block" ? FirewallAction.Block : FirewallAction.Allow,
                    cancellationToken,
                    identity).ConfigureAwait(false);
            }
            case PipeCommands.ApplyRule:
                return await _changes.ApplyConfirmedAsync(request.PreviewId!.Value, request.Confirmed, cancellationToken).ConfigureAwait(false);
            case PipeCommands.ConfirmConnectivity:
                return new { Confirmed = await _changes.ConfirmConnectivityAsync(request.OperationId!.Value, cancellationToken).ConfigureAwait(false) };
            case PipeCommands.Undo:
                return new { Undone = await _changes.UndoAsync(request.OperationId!.Value, cancellationToken).ConfigureAwait(false) };
            case PipeCommands.EmergencyPreview:
                return await _changes.CreateEmergencyRemovalPreviewAsync(cancellationToken).ConfigureAwait(false);
            case PipeCommands.EmergencyRemove:
                return new { RemovedRules = await _changes.ConfirmEmergencyRemovalAsync(request.PreviewId!.Value, request.Confirmed, cancellationToken).ConfigureAwait(false) };
            default:
                throw new PipeProtocolException("The pipe command is not supported by this service version.");
        }
    }

    private async Task<ConnectionPage> GetConnectionsPageAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        var offset = request.Offset ?? 0;
        var limit = request.Limit ?? PipeProtocol.MaximumConnectionPageSize;
        ConnectionSnapshot snapshot;
        if (offset == 0)
        {
            var records = await _connections.GetConnectionsAsync(cancellationToken).ConfigureAwait(false);
            var truncated = records.Count > MaximumConnectionRows;
            var boundedRecords = records.Take(MaximumConnectionRows).ToArray();
            var snapshotId = Guid.NewGuid();
            snapshot = new ConnectionSnapshot(snapshotId, DateTimeOffset.UtcNow.Add(ConnectionSnapshotLifetime), boundedRecords, truncated);
            lock (_snapshotGate)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var expired in _connectionSnapshots
                             .Where(pair => pair.Value.ExpiresAt <= now)
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    _connectionSnapshots.Remove(expired);
                }

                if (_connectionSnapshots.Count >= MaximumConnectionSnapshots)
                {
                    var oldest = _connectionSnapshots.Values.OrderBy(item => item.ExpiresAt).First();
                    _connectionSnapshots.Remove(oldest.SnapshotId);
                }

                _connectionSnapshots[snapshotId] = snapshot;
            }
        }
        else
        {
            if (request.SnapshotId is not { } snapshotId)
            {
                throw new PipeProtocolException("A noninitial connection page requires a snapshot identifier.");
            }

            lock (_snapshotGate)
            {
                if (!_connectionSnapshots.TryGetValue(snapshotId, out var cached) || cached.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    _connectionSnapshots.Remove(snapshotId);
                    throw new InvalidOperationException("The connection snapshot expired. Refresh the connection table and try again.");
                }

                snapshot = cached;
            }
        }

        if (offset > snapshot.Records.Count || (offset > 0 && offset == snapshot.Records.Count))
        {
            throw new InvalidOperationException("The connection-page offset is outside the current snapshot.");
        }

        var page = snapshot.Records.Skip(offset).Take(limit).ToArray();
        return new ConnectionPage(
            snapshot.SnapshotId,
            offset,
            snapshot.Records.Count,
            offset + page.Length < snapshot.Records.Count,
            snapshot.IsTruncated,
            page);
    }

    private bool TryConsumeRateLimit()
    {
        lock (_rateGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _rateWindowStart >= TimeSpan.FromMinutes(1))
            {
                _rateWindowStart = now;
                _requestCount = 0;
            }

            if (_requestCount >= MaximumRequestsPerMinute)
            {
                return false;
            }

            _requestCount++;
            return true;
        }
    }

    private sealed record ConnectionSnapshot(
        Guid SnapshotId,
        DateTimeOffset ExpiresAt,
        IReadOnlyList<ConnectionRecord> Records,
        bool IsTruncated);

    private static SecurityIdentifier? GetClientSid(NamedPipeServerStream pipe)
    {
        SecurityIdentifier? sid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            sid = identity.User;
        });
        return sid;
    }
}
