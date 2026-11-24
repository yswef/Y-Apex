// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Domain;

namespace YApex.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IFirewallBackend
{
    Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesForProgramAsync(
        string normalizedProgramPath,
        CancellationToken cancellationToken);

    Task ReplaceProgramRulesAsync(
        string normalizedProgramPath,
        IReadOnlyList<ManagedFirewallRule> replacement,
        CancellationToken cancellationToken);

    Task RemoveOwnedRulesAsync(CancellationToken cancellationToken);
}

public interface IConnectionSource
{
    Task<IReadOnlyList<ConnectionRecord>> GetConnectionsAsync(CancellationToken cancellationToken);
}

public interface IUsageSource
{
    Task<UsageSnapshot> GetUsageAsync(DateTimeOffset periodStart, CancellationToken cancellationToken);
}

public interface IProgramIdentitySource
{
    Task<ProgramFileIdentity> InspectAsync(string programPath, CancellationToken cancellationToken);
}

public interface IChangeJournal
{
    Task AppendAsync(ChangeJournalEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest unfinished operations newest-first. Completed emergency removal supersedes older pending operations.
    /// </summary>
    Task<IReadOnlyList<ChangeJournalEntry>> GetUnfinishedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ChangeJournalSummary>> GetRecentAsync(CancellationToken cancellationToken);
}

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken);
}

public sealed record UpdateCheckResult(bool IsConfigured, string Message, string? Version = null);
