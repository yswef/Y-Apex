// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;

namespace YApex.Service.SystemIntegration;

public sealed partial class WindowsConnectionSource : IConnectionSource
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AddressFamilyInet = 2;
    private const int AddressFamilyInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const int MaximumRows = 250_000;
    private readonly ConcurrentDictionary<int, CachedIdentity> _processes = new();

    public async Task<IReadOnlyList<ConnectionRecord>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var connections = new List<ConnectionRecord>();
            var maximumReturnedRows = MaximumRows + 1;
            connections.AddRange(ReadTcpTable(AddressFamilyInet, capturedAt, maximumReturnedRows, cancellationToken));
            if (connections.Count < maximumReturnedRows)
            {
                connections.AddRange(ReadTcpTable(AddressFamilyInet6, capturedAt, maximumReturnedRows - connections.Count, cancellationToken));
            }

            if (connections.Count < maximumReturnedRows)
            {
                connections.AddRange(ReadUdpTable(AddressFamilyInet, capturedAt, maximumReturnedRows - connections.Count, cancellationToken));
            }

            if (connections.Count < maximumReturnedRows)
            {
                connections.AddRange(ReadUdpTable(AddressFamilyInet6, capturedAt, maximumReturnedRows - connections.Count, cancellationToken));
            }

            return (IReadOnlyList<ConnectionRecord>)connections
                .OrderBy(connection => connection.Process.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(connection => connection.LocalEndpoint.Port)
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ConnectionRecord> ReadTcpTable(
        int addressFamily,
        DateTimeOffset observedAt,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        if (maximumRows <= 0)
        {
            return Array.Empty<ConnectionRecord>();
        }

        var buffer = ReadTable(GetExtendedTcpTable, addressFamily, TcpTableOwnerPidAll, cancellationToken);
        if (buffer == IntPtr.Zero)
        {
            return Array.Empty<ConnectionRecord>();
        }

        try
        {
            var count = Math.Min(ReadCount(buffer), maximumRows);
            var rowSize = addressFamily == AddressFamilyInet ? 24 : 56;
            var result = new List<ConnectionRecord>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = IntPtr.Add(buffer, sizeof(uint) + (index * rowSize));
                var record = addressFamily == AddressFamilyInet
                    ? ReadTcpV4(row, observedAt)
                    : ReadTcpV6(row, observedAt);
                result.Add(record);
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private IReadOnlyList<ConnectionRecord> ReadUdpTable(
        int addressFamily,
        DateTimeOffset observedAt,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        if (maximumRows <= 0)
        {
            return Array.Empty<ConnectionRecord>();
        }

        var buffer = ReadTable(GetExtendedUdpTable, addressFamily, UdpTableOwnerPid, cancellationToken);
        if (buffer == IntPtr.Zero)
        {
            return Array.Empty<ConnectionRecord>();
        }

        try
        {
            var count = Math.Min(ReadCount(buffer), maximumRows);
            var rowSize = addressFamily == AddressFamilyInet ? 12 : 28;
            var result = new List<ConnectionRecord>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = IntPtr.Add(buffer, sizeof(uint) + (index * rowSize));
                result.Add(addressFamily == AddressFamilyInet
                    ? ReadUdpV4(row, observedAt)
                    : ReadUdpV6(row, observedAt));
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private ConnectionRecord ReadTcpV4(IntPtr row, DateTimeOffset observedAt)
    {
        var state = ReadUInt32(row, 0);
        var local = new IPAddress(BitConverter.GetBytes(ReadUInt32(row, 4)));
        var localPort = ReadPort(row, 8);
        var remote = new IPAddress(BitConverter.GetBytes(ReadUInt32(row, 12)));
        var remotePort = ReadPort(row, 16);
        var process = GetProcessIdentity(ReadUInt32(row, 20));
        return CreateConnection(process, local, localPort, remote, remotePort, NetworkProtocol.Tcp, ToConnectionState(state), observedAt);
    }

    private ConnectionRecord ReadTcpV6(IntPtr row, DateTimeOffset observedAt)
    {
        var localAddress = ReadBytes(row, 0, 16);
        var localScope = ReadUInt32(row, 16);
        var localPort = ReadPort(row, 20);
        var remoteAddress = ReadBytes(row, 24, 16);
        var remoteScope = ReadUInt32(row, 40);
        var remotePort = ReadPort(row, 44);
        var state = ReadUInt32(row, 48);
        var process = GetProcessIdentity(ReadUInt32(row, 52));
        var local = new IPAddress(localAddress, localScope);
        var remote = new IPAddress(remoteAddress, remoteScope);
        return CreateConnection(process, local, localPort, remote, remotePort, NetworkProtocol.Tcp, ToConnectionState(state), observedAt);
    }

    private ConnectionRecord ReadUdpV4(IntPtr row, DateTimeOffset observedAt)
    {
        var local = new IPAddress(BitConverter.GetBytes(ReadUInt32(row, 0)));
        var localPort = ReadPort(row, 4);
        var process = GetProcessIdentity(ReadUInt32(row, 8));
        return CreateConnection(process, local, localPort, null, 0, NetworkProtocol.Udp, ConnectionState.Listening, observedAt);
    }

    private ConnectionRecord ReadUdpV6(IntPtr row, DateTimeOffset observedAt)
    {
        var localAddress = ReadBytes(row, 0, 16);
        var scope = ReadUInt32(row, 16);
        var localPort = ReadPort(row, 20);
        var process = GetProcessIdentity(ReadUInt32(row, 24));
        return CreateConnection(process, new IPAddress(localAddress, scope), localPort, null, 0, NetworkProtocol.Udp, ConnectionState.Listening, observedAt);
    }

    private static ConnectionRecord CreateConnection(
        ProcessIdentity process,
        IPAddress localAddress,
        int localPort,
        IPAddress? remoteAddress,
        int remotePort,
        NetworkProtocol protocol,
        ConnectionState state,
        DateTimeOffset observedAt)
    {
        var local = new NetworkEndpoint(localAddress.ToString(), localPort);
        var remote = remoteAddress is null ? null : new NetworkEndpoint(remoteAddress.ToString(), remotePort);
        var id = string.Join('|', process.ProcessId, protocol, local.Address, local.Port, remote?.Address ?? string.Empty, remote?.Port ?? 0);
        return new ConnectionRecord(id, process, local, remote, protocol, state, ConnectionDirection.Unknown, observedAt);
    }

    private ProcessIdentity GetProcessIdentity(uint processId)
    {
        var pid = checked((int)processId);
        if (_processes.TryGetValue(pid, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Identity;
        }

        ProcessIdentity identity;
        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName.Length <= 128 ? process.ProcessName : process.ProcessName[..128];
            string? path = null;
            string? publisher = null;
            string? sha256 = null;
            var signature = SignatureState.Unknown;
            try
            {
                var reportedPath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(reportedPath))
                {
                    try
                    {
                        var normalizedPath = WindowsProgramPath.Normalize(reportedPath);
                        if (normalizedPath.Length <= 512)
                        {
                            path = normalizedPath;
                        }
                    }
                    catch (ArgumentException)
                    {
                        path = null;
                    }

                    if (path is not null)
                    {
                        var signatureResult = WindowsSignatureVerifier.Inspect(path);
                        publisher = signatureResult.Publisher is { Length: > 256 } longPublisher
                            ? longPublisher[..256]
                            : signatureResult.Publisher;
                        sha256 = signatureResult.Sha256;
                        signature = signatureResult.State;
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                signature = SignatureState.Unknown;
            }

            identity = new ProcessIdentity(pid, name, path, publisher, sha256, signature);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            identity = new ProcessIdentity(pid, "Unknown process", null, null, null, SignatureState.Unknown);
        }

        _processes[pid] = new CachedIdentity(identity, DateTimeOffset.UtcNow.AddSeconds(30));
        return identity;
    }

    private static IntPtr ReadTable(TableFunction getTable, int addressFamily, int tableClass, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bufferSize = 0;
        var result = getTable(IntPtr.Zero, ref bufferSize, true, addressFamily, tableClass, 0);
        if (result == 0 && bufferSize == 0)
        {
            return IntPtr.Zero;
        }

        if (result != ErrorInsufficientBuffer || bufferSize < sizeof(uint) || bufferSize > MaximumRows * 64)
        {
            throw new Win32Exception((int)result, "Windows could not provide a network connection table.");
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        result = getTable(buffer, ref bufferSize, true, addressFamily, tableClass, 0);
        if (result != 0)
        {
            Marshal.FreeHGlobal(buffer);
            throw new Win32Exception((int)result, "Windows could not read a network connection table.");
        }

        return buffer;
    }

    private static int ReadCount(IntPtr table)
    {
        var count = unchecked((uint)Marshal.ReadInt32(table));
        if (count > MaximumRows)
        {
            throw new InvalidDataException("Windows returned an unreasonable number of network table rows.");
        }

        return checked((int)count);
    }

    private static uint ReadUInt32(IntPtr buffer, int offset) => unchecked((uint)Marshal.ReadInt32(buffer, offset));

    private static int ReadPort(IntPtr buffer, int offset)
    {
        var bytes = BitConverter.GetBytes(ReadUInt32(buffer, offset));
        return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2));
    }

    private static byte[] ReadBytes(IntPtr buffer, int offset, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(IntPtr.Add(buffer, offset), bytes, 0, length);
        return bytes;
    }

    private static ConnectionState ToConnectionState(uint state) => state switch
    {
        1 => ConnectionState.Closed,
        2 => ConnectionState.Listening,
        3 => ConnectionState.SynSent,
        4 => ConnectionState.SynReceived,
        5 => ConnectionState.Established,
        6 or 7 or 9 or 10 => ConnectionState.FinWait,
        8 => ConnectionState.CloseWait,
        11 => ConnectionState.TimeWait,
        _ => ConnectionState.Unknown
    };

    private delegate uint TableFunction(
        IntPtr table,
        ref int bufferSize,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int addressFamily,
        int tableClass,
        uint reserved);

    private sealed record CachedIdentity(ProcessIdentity Identity, DateTimeOffset ExpiresAt);

    [LibraryImport("iphlpapi.dll", EntryPoint = "GetExtendedTcpTable", SetLastError = true)]
    private static partial uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int bufferSize,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion,
        int tableClass,
        uint reserved);

    [LibraryImport("iphlpapi.dll", EntryPoint = "GetExtendedUdpTable", SetLastError = true)]
    private static partial uint GetExtendedUdpTable(
        IntPtr udpTable,
        ref int bufferSize,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion,
        int tableClass,
        uint reserved);
}
