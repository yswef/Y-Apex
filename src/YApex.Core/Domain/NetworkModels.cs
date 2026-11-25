// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
namespace YApex.Core.Domain;

public enum NetworkProtocol
{
    Tcp,
    Udp
}

public enum ConnectionState
{
    Unknown,
    Established,
    Listening,
    SynSent,
    SynReceived,
    FinWait,
    TimeWait,
    CloseWait,
    Closed
}

public enum ConnectionDirection
{
    Unknown,
    Inbound,
    Outbound
}

public enum SignatureState
{
    Unknown,
    Valid,
    Invalid,
    NotSigned
}

public sealed record ProcessIdentity(
    int ProcessId,
    string Name,
    string? Path,
    string? Publisher,
    string? Sha256,
    SignatureState Signature);

public sealed record ProgramFileIdentity(
    string ProgramPath,
    string? Sha256,
    SignatureState Signature,
    string? Publisher = null);

public sealed record NetworkEndpoint(string Address, int Port);

public sealed record ConnectionRecord(
    string Id,
    ProcessIdentity Process,
    NetworkEndpoint LocalEndpoint,
    NetworkEndpoint? RemoteEndpoint,
    NetworkProtocol Protocol,
    ConnectionState State,
    ConnectionDirection Direction,
    DateTimeOffset ObservedAt);

public sealed record ConnectionPage(
    Guid SnapshotId,
    int Offset,
    int TotalCount,
    bool HasMore,
    bool IsTruncated,
    IReadOnlyList<ConnectionRecord> Connections);

public sealed record ProgramUsage(
    string ProgramPath,
    string ProgramName,
    long BytesSent,
    long BytesReceived,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd)
{
    public long TotalBytes => BytesReceived > 0 && BytesSent > long.MaxValue - BytesReceived
        ? long.MaxValue
        : BytesSent + BytesReceived;
}

public sealed record UsageSnapshot(
    long BytesSent,
    long BytesReceived,
    IReadOnlyList<ProgramUsage> Programs,
    DateTimeOffset CapturedAt,
    bool IsAvailable = true,
    bool IsTruncated = false,
    DateTimeOffset? DataAvailableFrom = null);
