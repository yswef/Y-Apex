// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Buffers.Binary;
using System.Text;
using YApex.Core.Protocol;
using YApex.Core.Security;
using Xunit;

namespace YApex.Core.Tests;

public sealed class PathAndProtocolTests
{
    [Theory]
    [InlineData("C:\\Program Files\\Y-Apex\\demo.exe", "C:\\Program Files\\Y-Apex\\demo.exe")]
    [InlineData("d:/Apps/demo.exe", "D:\\Apps\\demo.exe")]
    public void NormalizeAcceptsAbsoluteLocalPaths(string input, string expected)
    {
        Assert.Equal(expected, WindowsProgramPath.Normalize(input));
    }

    [Theory]
    [InlineData("demo.exe")]
    [InlineData("C:\\Apps\\..\\demo.exe")]
    [InlineData("\\\\server\\share\\demo.exe")]
    [InlineData("C:\\Apps\\demo.exe:stream")]
    [InlineData("C:\\Apps\\CON.txt")]
    [InlineData("C:\\Apps\\LPT1\\demo.exe")]
    [InlineData("C:\\Apps\\folder.\\demo.exe")]
    [InlineData("C:\\Apps\\demo.exe\0")]
    public void NormalizeRejectsUnsafePaths(string input)
    {
        Assert.Throws<ArgumentException>(() => WindowsProgramPath.Normalize(input));
    }

    [Fact]
    public async Task ProtocolRoundTripsAValidRequest()
    {
        var request = new PipeRequest(
            PipeProtocol.CurrentVersion,
            Guid.NewGuid(),
            PipeCommands.PreviewRule,
            "C:\\Apps\\demo.exe",
            "block");
        await using var stream = new MemoryStream();
        await PipeProtocol.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        var received = await PipeProtocol.ReadRequestAsync(stream, CancellationToken.None);

        Assert.Equal(request, received);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(PipeProtocol.MaximumMessageBytes + 1)]
    public async Task ProtocolRejectsInvalidLengths(int length)
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.ReadRequestAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ProtocolRoundTripsConnectionPages()
    {
        var request = new PipeRequest(
            PipeProtocol.CurrentVersion,
            Guid.NewGuid(),
            PipeCommands.GetConnections,
            Offset: 32,
            Limit: PipeProtocol.MaximumConnectionPageSize,
            SnapshotId: Guid.NewGuid());
        await using var stream = new MemoryStream();
        await PipeProtocol.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        Assert.Equal(request, await PipeProtocol.ReadRequestAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ProtocolAcceptsTheOperationStatusRequestWithoutExtraFields()
    {
        var request = new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.GetOperations);
        await using var stream = new MemoryStream();
        await PipeProtocol.WriteRequestAsync(stream, request, CancellationToken.None);
        stream.Position = 0;

        Assert.Equal(request, await PipeProtocol.ReadRequestAsync(stream, CancellationToken.None));

        await using var invalidStream = new MemoryStream();
        await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.WriteRequestAsync(
            invalidStream,
            request with { Offset = 0, Limit = 1 },
            CancellationToken.None));
    }

    [Fact]
    public async Task ProtocolRejectsUnsafeConnectionPageBounds()
    {
        var requests = new[]
        {
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.GetConnections, Offset: -1, Limit: 1),
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.GetConnections, Offset: 1, Limit: 1),
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.GetConnections, Offset: 0, Limit: PipeProtocol.MaximumConnectionPageSize + 1),
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.GetConnections, Offset: 0, Limit: 1, SnapshotId: Guid.NewGuid())
        };

        foreach (var request in requests)
        {
            await using var stream = new MemoryStream();
            await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.WriteRequestAsync(stream, request, CancellationToken.None));
        }
    }

    [Fact]
    public async Task EmergencyRemovalRequiresAOneTimePreviewAndExplicitConfirmation()
    {
        var invalidRequests = new[]
        {
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.EmergencyRemove),
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.EmergencyRemove, PreviewId: Guid.NewGuid()),
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.EmergencyRemove, PreviewId: Guid.Empty, Confirmed: true)
        };

        foreach (var request in invalidRequests)
        {
            await using var stream = new MemoryStream();
            await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.WriteRequestAsync(stream, request, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ProtocolRejectsDuplicateAndUnknownFields()
    {
        const string json = "{\"protocolVersion\":1,\"requestId\":\"5e61f4a5-3888-440d-bfaa-42d74c793492\",\"requestId\":\"5e61f4a5-3888-440d-bfaa-42d74c793492\",\"command\":\"getConnections\"}";
        await using var stream = Framed(json);

        await Assert.ThrowsAsync<PipeProtocolException>(() => PipeProtocol.ReadRequestAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ProtocolFuzzRejectsMalformedFramesWithoutEscapingTheProtocolBoundary()
    {
        var random = new Random(804064);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(0, 1024)];
            random.NextBytes(bytes);
            await using var stream = Framed(bytes);
            try
            {
                _ = await PipeProtocol.ReadRequestAsync(stream, CancellationToken.None);
            }
            catch (PipeProtocolException)
            {
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private static MemoryStream Framed(string body) => Framed(Encoding.UTF8.GetBytes(body));

    private static MemoryStream Framed(byte[] body)
    {
        var stream = new MemoryStream();
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        stream.Write(header);
        stream.Write(body);
        stream.Position = 0;
        return stream;
    }
}
