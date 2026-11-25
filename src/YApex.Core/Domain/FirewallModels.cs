// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
namespace YApex.Core.Domain;

public enum FirewallAction
{
    Allow,
    Block
}

public enum FirewallDirection
{
    Inbound,
    Outbound
}

public enum JournalState
{
    Planned,
    Applied,
    Confirmed,
    RollingBack,
    RolledBack,
    Failed,
    RecoveryRequired,
    EmergencyRemoval
}

public sealed record ManagedFirewallRule(
    Guid RuleId,
    string Name,
    string Description,
    string ProgramPath,
    FirewallDirection Direction,
    FirewallAction Action,
    bool Enabled = true,
    string? FileSha256 = null,
    SignatureState Signature = SignatureState.Unknown);

public sealed record RuleChangePreview(
    Guid PreviewId,
    string ProgramPath,
    FirewallAction DesiredAction,
    IReadOnlyList<ManagedFirewallRule> Before,
    IReadOnlyList<ManagedFirewallRule> After,
    DateTimeOffset ExpiresAt,
    ProgramFileIdentity? Identity = null);

public sealed record RuleChangeResult(
    Guid OperationId,
    DateTimeOffset RollbackDeadline,
    IReadOnlyList<ManagedFirewallRule> AppliedRules);

public sealed record ActiveRuleOperation(
    Guid OperationId,
    DateTimeOffset RollbackDeadline,
    bool ConnectivityConfirmed);

public sealed record EmergencyRemovalRuleSummary(
    string ProgramPath,
    FirewallDirection Direction,
    FirewallAction Action);

public sealed record EmergencyRemovalPreview(
    Guid PreviewId,
    DateTimeOffset ExpiresAt,
    int RuleCount,
    IReadOnlyList<EmergencyRemovalRuleSummary> SampleRules);

public sealed record ChangeJournalEntry(
    Guid OperationId,
    DateTimeOffset Timestamp,
    JournalState State,
    string Reason,
    string? ProgramPath,
    IReadOnlyList<ManagedFirewallRule> Before,
    IReadOnlyList<ManagedFirewallRule> After,
    string? Error = null);

public sealed record ChangeJournalSummary(
    Guid OperationId,
    DateTimeOffset Timestamp,
    JournalState State,
    string Reason,
    string? ProgramPath);
