// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.IO.Pipes;
using System.Text.Json;
using YApex.Core.Domain;
using YApex.Core.Protocol;
using YApex.Service.Ipc;

var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
if (command is "--help" or "-h" or null)
{
    Console.WriteLine("YApex.Cli status | emergency-remove | register-pipe-user [--interactive]");
    return command is null ? 2 : 0;
}

if (command == "register-pipe-user")
{
    if (args.Length > 2 || (args.Length == 2 && !string.Equals(args[1], "--interactive", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Usage: YApex.Cli register-pipe-user [--interactive]");
        return 2;
    }

    try
    {
        if (args.Length == 2)
        {
            PipeAccessPolicy.RegisterInteractiveUser();
            Console.WriteLine("The interactive desktop user's SID was recorded for the service pipe ACL.");
        }
        else
        {
            PipeAccessPolicy.RegisterCurrentUser();
            Console.WriteLine("The current Windows user's SID was recorded for the service pipe ACL.");
        }

        return 0;
    }
    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

if (command is not ("status" or "emergency-remove"))
{
    Console.Error.WriteLine("Unknown command. Run YApex.Cli --help for supported commands.");
    return 2;
}

try
{
    if (command == "emergency-remove")
    {
        var previewResponse = await SendRequestAsync(PipeCommands.EmergencyPreview, null, null, false);
        var preview = previewResponse.Payload?.Deserialize<EmergencyRemovalPreview>(CliJson.Options)
            ?? throw new JsonException("The service returned an empty emergency-removal preview.");
        Console.WriteLine($"Emergency removal preview: {preview.RuleCount} Y-Apex-owned firewall rule(s) will be removed.");
        foreach (var rule in preview.SampleRules)
        {
            Console.WriteLine($"{rule.ProgramPath} | {rule.Direction} | {rule.Action}");
        }

        if (preview.RuleCount > preview.SampleRules.Count)
        {
            Console.WriteLine($"... and {preview.RuleCount - preview.SampleRules.Count} additional rule(s) are not listed.");
        }

        Console.Write("Type REMOVE to confirm, or press Enter to cancel: ");
        if (!string.Equals(Console.ReadLine(), "REMOVE", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelled. No firewall rules were changed.");
            return 0;
        }

        var removedResponse = await SendRequestAsync(
            PipeCommands.EmergencyRemove,
            preview.PreviewId,
            null,
            true);
        var result = removedResponse.Payload?.Deserialize<EmergencyResponse>(CliJson.Options)
            ?? throw new JsonException("The service returned an empty emergency-removal result.");
        Console.WriteLine($"Removed {result.RemovedRules} Y-Apex rule(s).");
        return 0;
    }

    var statusResponse = await SendRequestAsync(PipeCommands.GetRules, null, null, false);
    var rules = statusResponse.Payload?.Deserialize<ManagedFirewallRule[]>(CliJson.Options)
        ?? Array.Empty<ManagedFirewallRule>();
    Console.WriteLine($"Service is running. Y-Apex rules: {rules.Length}.");
    return 0;
}
catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException or JsonException or PipeProtocolException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"The service request failed: {exception.Message}");
    return 1;
}

static async Task<PipeResponse> SendRequestAsync(
    string command,
    Guid? previewId,
    Guid? operationId,
    bool confirmed)
{
    await using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    await pipe.ConnectAsync(3000, timeout.Token);
    var requestId = Guid.NewGuid();
    await PipeProtocol.WriteRequestAsync(
        pipe,
        new PipeRequest(
            PipeProtocol.CurrentVersion,
            requestId,
            command,
            PreviewId: previewId,
            OperationId: operationId,
            Confirmed: confirmed),
        timeout.Token);
    var response = await PipeProtocol.ReadResponseAsync(pipe, requestId, timeout.Token);
    if (!response.Success)
    {
        throw new InvalidOperationException(response.Error ?? "The service rejected the request.");
    }

    return response;
}

internal static class CliJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}

internal sealed record EmergencyResponse(int RemovedRules);
