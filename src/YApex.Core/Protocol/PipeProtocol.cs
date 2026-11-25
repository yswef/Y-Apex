// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using YApex.Core.Security;

namespace YApex.Core.Protocol;

public static class PipeCommands
{
    public const string GetConnections = "getConnections";
    public const string GetRules = "getRules";
    public const string GetUsage = "getUsage";
    public const string GetJournal = "getJournal";
    public const string GetOperations = "getOperations";
    public const string PreviewRule = "previewRule";
    public const string ApplyRule = "applyRule";
    public const string ConfirmConnectivity = "confirmConnectivity";
    public const string Undo = "undo";
    public const string EmergencyPreview = "emergencyPreview";
    public const string EmergencyRemove = "emergencyRemove";
}

public sealed record PipeRequest(
    int ProtocolVersion,
    Guid RequestId,
    string Command,
    string? ProgramPath = null,
    string? Action = null,
    Guid? PreviewId = null,
    Guid? OperationId = null,
    bool Confirmed = false,
    int? Offset = null,
    int? Limit = null,
    Guid? SnapshotId = null);

public sealed record PipeResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    string? Error,
    JsonElement? Payload = null);

public sealed class PipeProtocolException : Exception
{
    public PipeProtocolException(string message)
        : base(message)
    {
    }

    public PipeProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class PipeProtocol
{
    public const string PipeName = "Y-Apex.Control.v1";
    public const int CurrentVersion = 1;
    public const int MaximumMessageBytes = 64 * 1024;
    public const int MaximumConnectionPageSize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task<PipeRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > MaximumMessageBytes)
        {
            throw new PipeProtocolException("The pipe message length is outside the allowed range.");
        }

        var body = new byte[length];
        await ReadExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            var request = document.RootElement.Deserialize<PipeRequest>(JsonOptions)
                ?? throw new PipeProtocolException("The pipe message is empty.");
            ValidateRequest(request);
            return request;
        }
        catch (JsonException exception)
        {
            throw new PipeProtocolException("The pipe message is not valid protocol JSON.", exception);
        }
    }

    public static async Task WriteRequestAsync(Stream stream, PipeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        await WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteResponseAsync<T>(
        Stream stream,
        Guid requestId,
        bool success,
        string? error,
        T? payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A response must refer to a request.", nameof(requestId));
        }

        JsonElement? element = payload is null
            ? null
            : JsonSerializer.SerializeToElement(payload, JsonOptions);
        var response = new PipeResponse(CurrentVersion, requestId, success, error, element);
        await WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    public static async Task<PipeResponse> ReadResponseAsync(Stream stream, Guid expectedRequestId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 2 or > MaximumMessageBytes)
        {
            throw new PipeProtocolException("The pipe response length is outside the allowed range.");
        }

        var body = new byte[length];
        await ReadExactlyAsync(stream, body, cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(document.RootElement);
            var response = document.RootElement.Deserialize<PipeResponse>(JsonOptions)
                ?? throw new PipeProtocolException("The pipe response is empty.");
            if (response.ProtocolVersion != CurrentVersion || response.RequestId != expectedRequestId)
            {
                throw new PipeProtocolException("The pipe response version or request identifier is invalid.");
            }

            return response;
        }
        catch (JsonException exception)
        {
            throw new PipeProtocolException("The pipe response is not valid protocol JSON.", exception);
        }
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] body, CancellationToken cancellationToken)
    {
        if (body.Length is < 2 or > MaximumMessageBytes)
        {
            throw new PipeProtocolException("The serialized pipe message exceeds the allowed size.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new PipeProtocolException("The pipe message ended before the full frame was received.");
            }

            read += count;
        }
    }

    private static void ValidateRequest(PipeRequest request)
    {
        if (request.ProtocolVersion != CurrentVersion || request.RequestId == Guid.Empty)
        {
            throw new PipeProtocolException("The pipe protocol version or request identifier is invalid.");
        }

        switch (request.Command)
        {
            case PipeCommands.GetConnections:
            {
                if (request.ProgramPath is not null || request.Action is not null || request.PreviewId is not null ||
                    request.OperationId is not null || request.Confirmed || request.Offset.HasValue != request.Limit.HasValue)
                {
                    throw new PipeProtocolException("The connection-page request is incomplete or has unexpected fields.");
                }

                var offset = request.Offset ?? 0;
                var limit = request.Limit ?? MaximumConnectionPageSize;
                if (offset is < 0 or > 250_000 || limit is < 1 or > MaximumConnectionPageSize ||
                    (request.SnapshotId is null && offset != 0) ||
                    (request.SnapshotId is { } snapshotId && (snapshotId == Guid.Empty || offset == 0)))
                {
                    throw new PipeProtocolException("The connection-page bounds or snapshot identifier are invalid.");
                }

                break;
            }
            case PipeCommands.GetRules:
            case PipeCommands.GetUsage:
            case PipeCommands.GetJournal:
            case PipeCommands.GetOperations:
            case PipeCommands.EmergencyPreview:
                if (request.ProgramPath is not null || request.Action is not null || request.PreviewId is not null ||
                    request.OperationId is not null || request.Confirmed || request.Offset is not null ||
                    request.Limit is not null || request.SnapshotId is not null)
                {
                    throw new PipeProtocolException("This pipe command does not accept additional fields.");
                }

                break;
            case PipeCommands.PreviewRule:
                if (request.ProgramPath is null || request.Action is null || request.PreviewId is not null || request.OperationId is not null ||
                    request.Confirmed || request.Offset is not null || request.Limit is not null || request.SnapshotId is not null)
                {
                    throw new PipeProtocolException("The rule preview request is incomplete or has unexpected fields.");
                }

                _ = WindowsProgramPath.Normalize(request.ProgramPath);
                if (request.Action is not ("allow" or "block"))
                {
                    throw new PipeProtocolException("The requested firewall action is not supported.");
                }

                break;
            case PipeCommands.ApplyRule:
            case PipeCommands.EmergencyRemove:
                if (request.PreviewId is not { } previewId || previewId == Guid.Empty ||
                    request.ProgramPath is not null || request.Action is not null || request.OperationId is not null ||
                    !request.Confirmed || request.Offset is not null || request.Limit is not null || request.SnapshotId is not null)
                {
                    throw new PipeProtocolException("Applying a firewall change requires a valid preview identifier and explicit confirmation.");
                }

                break;
            case PipeCommands.ConfirmConnectivity:
            case PipeCommands.Undo:
                if (request.OperationId is not { } operationId || operationId == Guid.Empty ||
                    request.ProgramPath is not null || request.Action is not null || request.PreviewId is not null ||
                    request.Confirmed || request.Offset is not null || request.Limit is not null || request.SnapshotId is not null)
                {
                    throw new PipeProtocolException("This pipe command requires only a valid operation identifier.");
                }

                break;
            default:
                throw new PipeProtocolException("The pipe command is not recognized.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new PipeProtocolException("A pipe message must be a JSON object.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new PipeProtocolException("The pipe message contains a duplicate field.");
            }
        }
    }
}
