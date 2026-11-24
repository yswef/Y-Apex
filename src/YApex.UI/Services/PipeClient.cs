// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using YApex.Core.Protocol;

namespace YApex.UI.Services;

public sealed class PipeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> SendAsync<T>(PipeRequest request, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1500, cancellationToken).ConfigureAwait(false);
        await PipeProtocol.WriteRequestAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        var response = await PipeProtocol.ReadResponseAsync(pipe, request.RequestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "The service rejected the request.");
        }

        if (response.Payload is not { } payload)
        {
            return default!;
        }

        return payload.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException("The service returned an empty response payload.");
    }

    public Task<T> SendAsync<T>(string command, CancellationToken cancellationToken) =>
        SendAsync<T>(new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), command), cancellationToken);
}
