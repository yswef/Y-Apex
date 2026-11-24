// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Abstractions;
using YApex.Core.Domain;

namespace YApex.Wfp;

public sealed class WfpUnavailableException(string message) : Exception(message);

public sealed class ExperimentalWfpFirewallBackend : IFirewallBackend
{
    public bool IsReady => false;

    public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesAsync(CancellationToken cancellationToken) =>
        throw Unavailable();

    public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesForProgramAsync(
        string normalizedProgramPath,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task ReplaceProgramRulesAsync(
        string normalizedProgramPath,
        IReadOnlyList<ManagedFirewallRule> replacement,
        CancellationToken cancellationToken) => throw Unavailable();

    public Task RemoveOwnedRulesAsync(CancellationToken cancellationToken) => throw Unavailable();

    private static WfpUnavailableException Unavailable() =>
        new("The experimental WFP provider is intentionally not implemented in this build.");
}

public sealed class WfpFallbackFirewallBackend : IFirewallBackend
{
    private readonly IFirewallBackend _experimental;
    private readonly IFirewallBackend _windowsFirewall;
    private int _enabled;

    public WfpFallbackFirewallBackend(
        bool enabled,
        IFirewallBackend experimental,
        IFirewallBackend windowsFirewall)
    {
        ArgumentNullException.ThrowIfNull(experimental);
        ArgumentNullException.ThrowIfNull(windowsFirewall);
        _enabled = enabled ? 1 : 0;
        _experimental = experimental;
        _windowsFirewall = windowsFirewall;
    }

    public bool ExperimentalBackendFailed { get; private set; }

    public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(backend => backend.GetOwnedRulesAsync(cancellationToken), () => _windowsFirewall.GetOwnedRulesAsync(cancellationToken));

    public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesForProgramAsync(
        string normalizedProgramPath,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            backend => backend.GetOwnedRulesForProgramAsync(normalizedProgramPath, cancellationToken),
            () => _windowsFirewall.GetOwnedRulesForProgramAsync(normalizedProgramPath, cancellationToken));

    public Task ReplaceProgramRulesAsync(
        string normalizedProgramPath,
        IReadOnlyList<ManagedFirewallRule> replacement,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            backend => backend.ReplaceProgramRulesAsync(normalizedProgramPath, replacement, cancellationToken),
            () => _windowsFirewall.ReplaceProgramRulesAsync(normalizedProgramPath, replacement, cancellationToken));

    public Task RemoveOwnedRulesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            backend => backend.RemoveOwnedRulesAsync(cancellationToken),
            () => _windowsFirewall.RemoveOwnedRulesAsync(cancellationToken));

    private async Task<T> ExecuteAsync<T>(Func<IFirewallBackend, Task<T>> experimental, Func<Task<T>> fallback)
    {
        if (Volatile.Read(ref _enabled) == 0)
        {
            return await fallback().ConfigureAwait(false);
        }

        try
        {
            return await experimental(_experimental).ConfigureAwait(false);
        }
        catch (WfpUnavailableException)
        {
            DisableExperimentalBackend();
            return await fallback().ConfigureAwait(false);
        }
    }

    private async Task ExecuteAsync(Func<IFirewallBackend, Task> experimental, Func<Task> fallback)
    {
        if (Volatile.Read(ref _enabled) == 0)
        {
            await fallback().ConfigureAwait(false);
            return;
        }

        try
        {
            await experimental(_experimental).ConfigureAwait(false);
        }
        catch (WfpUnavailableException)
        {
            DisableExperimentalBackend();
            await fallback().ConfigureAwait(false);
        }
    }

    private void DisableExperimentalBackend()
    {
        ExperimentalBackendFailed = true;
        Interlocked.Exchange(ref _enabled, 0);
    }
}
