// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Collections.Concurrent;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;

namespace YApex.Core.Services;

public sealed class RuleChangeCoordinator : IDisposable
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(2);
    private const int MaximumPreviews = 128;
    private const int MaximumOperations = 256;
    private const int MaximumPendingOperations = 16;
    private readonly IFirewallBackend _firewall;
    private readonly IChangeJournal _journal;
    private readonly IClock _clock;
    private readonly IProgramIdentitySource? _identitySource;
    private readonly TimeSpan _rollbackWindow;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _operationSequence;
    private readonly ConcurrentDictionary<Guid, RuleChangePreview> _previews = new();
    private readonly ConcurrentDictionary<Guid, EmergencyPreviewState> _emergencyPreviews = new();
    private readonly ConcurrentDictionary<Guid, OperationState> _operations = new();
    private readonly ConcurrentDictionary<Guid, OperationState> _pending = new();

    public RuleChangeCoordinator(
        IFirewallBackend firewall,
        IChangeJournal journal,
        IClock clock,
        TimeSpan? rollbackWindow = null,
        IProgramIdentitySource? identitySource = null)
    {
        ArgumentNullException.ThrowIfNull(firewall);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(clock);
        _firewall = firewall;
        _journal = journal;
        _clock = clock;
        _identitySource = identitySource;
        _rollbackWindow = rollbackWindow ?? TimeSpan.FromSeconds(30);
        if (_rollbackWindow <= TimeSpan.Zero || _rollbackWindow > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(rollbackWindow));
        }
    }

    public async Task RecoverInterruptedOperationsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var unfinished = await _journal.GetUnfinishedAsync(cancellationToken).ConfigureAwait(false);
            foreach (var entry in unfinished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.OperationId == Guid.Empty ||
                    entry.State is not (JournalState.Planned or JournalState.Applied or JournalState.RollingBack or JournalState.RecoveryRequired) ||
                    entry.Before.Any(rule => rule is null || !RuleFactory.IsOwnedRule(rule)) ||
                    entry.After.Any(rule => rule is null || !RuleFactory.IsOwnedRule(rule)))
                {
                    throw new InvalidDataException("An unfinished journal entry failed recovery validation.");
                }

                var isRollback = entry.State == JournalState.RollingBack || entry.Reason == "automatic-rollback";
                var recoverySourceRules = isRollback ? entry.Before : entry.After;
                var restorationRules = isRollback ? entry.After : entry.Before;
                if (entry.ProgramPath is { } programPath)
                {
                    var normalizedPath = WindowsProgramPath.Normalize(programPath);
                    if (!string.Equals(normalizedPath, programPath, StringComparison.OrdinalIgnoreCase) ||
                        entry.Before.Concat(entry.After).Any(rule =>
                            !string.Equals(rule.ProgramPath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException("An unfinished journal entry contains rules for an unexpected program path.");
                    }

                    await _firewall.ReplaceProgramRulesAsync(normalizedPath, restorationRules, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (entry.Reason != "emergency-remove-confirmed")
                    {
                        throw new InvalidDataException("An unfinished journal entry has no program path and is not an emergency operation.");
                    }

                    foreach (var group in restorationRules.GroupBy(rule => rule.ProgramPath, StringComparer.OrdinalIgnoreCase))
                    {
                        var normalizedPath = WindowsProgramPath.Normalize(group.Key);
                        if (!string.Equals(normalizedPath, group.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("An emergency journal entry contains a noncanonical program path.");
                        }

                        await _firewall.ReplaceProgramRulesAsync(normalizedPath, group.ToArray(), cancellationToken).ConfigureAwait(false);
                    }
                }

                await _journal.AppendAsync(
                    CreateJournalEntry(
                        entry.OperationId,
                        JournalState.RolledBack,
                        "startup-recovery",
                        entry.ProgramPath,
                        recoverySourceRules,
                        restorationRules,
                        "Restored the pre-operation Y-Apex rule snapshot after an interrupted change."),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RuleChangePreview> CreatePreviewAsync(
        string programPath,
        FirewallAction action,
        CancellationToken cancellationToken,
        ProgramFileIdentity? identity = null)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        var normalizedPath = WindowsProgramPath.Normalize(programPath);
        foreach (var expired in _previews.Where(pair => pair.Value.ExpiresAt <= _clock.UtcNow).Select(pair => pair.Key))
        {
            _previews.TryRemove(expired, out _);
        }

        if (_previews.Count >= MaximumPreviews)
        {
            foreach (var oldest in _previews.Values.OrderBy(preview => preview.ExpiresAt).Take(_previews.Count - MaximumPreviews + 1))
            {
                _previews.TryRemove(oldest.PreviewId, out _);
            }
        }

        var before = await _firewall.GetOwnedRulesForProgramAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (before.Any(rule => !RuleFactory.IsOwnedRule(rule)))
        {
            throw new InvalidOperationException("The firewall backend returned a rule without Y-Apex ownership metadata.");
        }

        var preview = new RuleChangePreview(
            Guid.NewGuid(),
            normalizedPath,
            action,
            before.ToArray(),
            RuleFactory.CreateProgramRules(normalizedPath, action, identity),
            _clock.UtcNow.Add(PreviewLifetime),
            identity);
        _previews[preview.PreviewId] = preview;
        return preview;
    }

    public async Task<RuleChangeResult> ApplyConfirmedAsync(
        Guid previewId,
        bool explicitlyConfirmed,
        CancellationToken cancellationToken)
    {
        if (!explicitlyConfirmed)
        {
            throw new InvalidOperationException("A rule change requires explicit user confirmation.");
        }

        if (!_previews.TryRemove(previewId, out var preview))
        {
            throw new InvalidOperationException("The preview does not exist or has already been used.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (preview.ExpiresAt <= _clock.UtcNow)
            {
                throw new InvalidOperationException("The preview expired. Create a new preview before changing the firewall.");
            }

            if (_operations.Values.Any(operation =>
                    operation.State == JournalState.Applied &&
                    string.Equals(operation.Preview.ProgramPath, preview.ProgramPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("A firewall change for this program is still available to confirm or undo. Resolve it before changing the same program again.");
            }

            if (_pending.Count >= MaximumPendingOperations)
            {
                throw new InvalidOperationException("Too many firewall changes are awaiting connectivity confirmation. Confirm or undo a pending change first.");
            }

            var current = await _firewall.GetOwnedRulesForProgramAsync(preview.ProgramPath, cancellationToken).ConfigureAwait(false);
            if (!RulesEqual(current, preview.Before))
            {
                throw new InvalidOperationException("Firewall rules changed after preview. Create a new preview before continuing.");
            }

            if (_identitySource is not null && preview.After[0].FileSha256 is { } expectedHash)
            {
                var currentIdentity = await _identitySource.InspectAsync(preview.ProgramPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(currentIdentity.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase) ||
                    currentIdentity.Signature != preview.After[0].Signature)
                {
                    throw new InvalidOperationException("The program file changed after preview. Create a new preview before continuing.");
                }
            }

            var sequence = checked(_operationSequence + 1);
            var operationId = Guid.NewGuid();
            await _journal.AppendAsync(
                CreateJournalEntry(operationId, JournalState.Planned, "user-confirmed", preview.ProgramPath, preview.Before, preview.After),
                cancellationToken).ConfigureAwait(false);

            try
            {
                await _firewall.ReplaceProgramRulesAsync(preview.ProgramPath, preview.After, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception applyError)
            {
                try
                {
                    await _firewall.ReplaceProgramRulesAsync(preview.ProgramPath, preview.Before, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    await AppendFailureAsync(operationId, preview, $"Apply failed: {applyError.Message}; restore failed: {rollbackError.Message}", JournalState.RecoveryRequired).ConfigureAwait(false);
                    throw new AggregateException("Firewall update and immediate restore both failed.", applyError, rollbackError);
                }

                await AppendFailureAsync(operationId, preview, $"Apply failed and the prior rules were restored: {applyError.Message}").ConfigureAwait(false);
                throw new InvalidOperationException("The firewall change failed. The previous Y-Apex rules were restored.", applyError);
            }

            try
            {
                await _journal.AppendAsync(
                    CreateJournalEntry(operationId, JournalState.Applied, "awaiting-connectivity-confirmation", preview.ProgramPath, preview.Before, preview.After),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception journalError)
            {
                try
                {
                    await _firewall.ReplaceProgramRulesAsync(preview.ProgramPath, preview.Before, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    await AppendFailureAsync(operationId, preview, $"Applied state could not be journaled: {journalError.Message}; restore failed: {rollbackError.Message}", JournalState.RecoveryRequired).ConfigureAwait(false);
                    throw new AggregateException("The applied state could not be journaled and the previous rules could not be restored.", journalError, rollbackError);
                }

                await AppendFailureAsync(operationId, preview, $"Applied state could not be journaled; previous rules restored: {journalError.Message}").ConfigureAwait(false);
                throw new InvalidOperationException("The change was not kept because its applied state could not be journaled. Previous rules were restored.", journalError);
            }

            var deadline = _clock.UtcNow.Add(_rollbackWindow);
            _operationSequence = sequence;
            var operation = new OperationState(operationId, preview, deadline, sequence);
            _operations[operationId] = operation;
            _pending[operationId] = operation;
            TrimCompletedOperations();
            operation.TimerTask = MonitorRollbackAsync(operation);
            return new RuleChangeResult(operationId, deadline, preview.After);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActiveRuleOperation>> GetActiveOperationsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _operations.Values
                .Where(operation => operation.State is JournalState.Applied or JournalState.Confirmed)
                .OrderBy(operation => operation.Sequence)
                .Select(operation => new ActiveRuleOperation(
                    operation.OperationId,
                    operation.Deadline,
                    operation.State == JournalState.Confirmed))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ConfirmConnectivityAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_pending.TryGetValue(operationId, out var operation) || operation.State != JournalState.Applied)
            {
                return false;
            }

            await _journal.AppendAsync(
                CreateJournalEntry(operationId, JournalState.Confirmed, "user-confirmed-connectivity", operation.Preview.ProgramPath, operation.Preview.Before, operation.Preview.After),
                cancellationToken).ConfigureAwait(false);
            operation.State = JournalState.Confirmed;
            _pending.TryRemove(operationId, out _);
            operation.TimerCancellation.Cancel();
            TrimCompletedOperations();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UndoAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_operations.TryGetValue(operationId, out var operation) ||
                operation.State is not (JournalState.Applied or JournalState.Confirmed))
            {
                return false;
            }

            if (_operations.Values.Any(candidate =>
                    candidate.Sequence > operation.Sequence &&
                    candidate.State is (JournalState.Applied or JournalState.Confirmed) &&
                    string.Equals(candidate.Preview.ProgramPath, operation.Preview.ProgramPath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            await _journal.AppendAsync(
                CreateJournalEntry(operationId, JournalState.RollingBack, "user-requested-undo", operation.Preview.ProgramPath, operation.Preview.After, operation.Preview.Before),
                cancellationToken).ConfigureAwait(false);
            await _firewall.ReplaceProgramRulesAsync(operation.Preview.ProgramPath, operation.Preview.Before, cancellationToken).ConfigureAwait(false);
            await _journal.AppendAsync(
                CreateJournalEntry(operationId, JournalState.RolledBack, "user-requested-undo", operation.Preview.ProgramPath, operation.Preview.After, operation.Preview.Before),
                cancellationToken).ConfigureAwait(false);
            operation.State = JournalState.RolledBack;
            operation.TimerCancellation.Cancel();
            _pending.TryRemove(operationId, out _);
            TrimCompletedOperations();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EmergencyRemovalPreview> CreateEmergencyRemovalPreviewAsync(CancellationToken cancellationToken)
    {
        var rules = await _firewall.GetOwnedRulesAsync(cancellationToken).ConfigureAwait(false);
        if (rules.Any(rule => rule is null || !RuleFactory.IsOwnedRule(rule)))
        {
            throw new InvalidOperationException("The firewall backend returned a rule without Y-Apex ownership metadata.");
        }

        var orderedRules = rules
            .OrderBy(rule => rule.ProgramPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Direction)
            .ThenBy(rule => rule.RuleId)
            .ToArray();
        var preview = new EmergencyRemovalPreview(
            Guid.NewGuid(),
            _clock.UtcNow.Add(PreviewLifetime),
            orderedRules.Length,
            orderedRules.Take(12)
                .Select(rule => new EmergencyRemovalRuleSummary(
                    rule.ProgramPath.Length <= 512 ? rule.ProgramPath : rule.ProgramPath[..512],
                    rule.Direction,
                    rule.Action))
                .ToArray());
        foreach (var expired in _emergencyPreviews
                     .Where(pair => pair.Value.Preview.ExpiresAt <= _clock.UtcNow)
                     .Select(pair => pair.Key))
        {
            _emergencyPreviews.TryRemove(expired, out _);
        }

        if (_emergencyPreviews.Count >= MaximumPreviews)
        {
            foreach (var oldest in _emergencyPreviews.Values
                         .OrderBy(item => item.Preview.ExpiresAt)
                         .Take(_emergencyPreviews.Count - MaximumPreviews + 1))
            {
                _emergencyPreviews.TryRemove(oldest.Preview.PreviewId, out _);
            }
        }

        _emergencyPreviews[preview.PreviewId] = new EmergencyPreviewState(preview, orderedRules);
        return preview;
    }

    public async Task<int> ConfirmEmergencyRemovalAsync(
        Guid previewId,
        bool explicitlyConfirmed,
        CancellationToken cancellationToken)
    {
        if (!explicitlyConfirmed)
        {
            throw new InvalidOperationException("Emergency rule removal requires explicit user confirmation.");
        }

        if (!_emergencyPreviews.TryRemove(previewId, out var previewState))
        {
            throw new InvalidOperationException("The emergency preview does not exist or has already been used.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preview = previewState.Preview;
            var previousRules = previewState.Rules;
            if (preview.ExpiresAt <= _clock.UtcNow)
            {
                throw new InvalidOperationException("The emergency preview expired. Create a new preview before continuing.");
            }

            var current = await _firewall.GetOwnedRulesAsync(cancellationToken).ConfigureAwait(false);
            if (current.Any(rule => rule is null || !RuleFactory.IsOwnedRule(rule)) || !RulesEqual(current, previousRules))
            {
                throw new InvalidOperationException("Y-Apex rules changed after preview. Create a new emergency preview before continuing.");
            }

            var operationId = Guid.NewGuid();
            await _journal.AppendAsync(
                CreateJournalEntry(operationId, JournalState.Planned, "emergency-remove-confirmed", null, previousRules, Array.Empty<ManagedFirewallRule>()),
                cancellationToken).ConfigureAwait(false);

            try
            {
                await _firewall.RemoveOwnedRulesAsync(cancellationToken).ConfigureAwait(false);
                await _journal.AppendAsync(
                    CreateJournalEntry(operationId, JournalState.EmergencyRemoval, "emergency-remove-confirmed", null, previousRules, Array.Empty<ManagedFirewallRule>()),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception removalError)
            {
                var restoreErrors = new List<Exception>();
                foreach (var group in previousRules.GroupBy(rule => rule.ProgramPath, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _firewall.ReplaceProgramRulesAsync(group.Key, group.ToArray(), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception restoreError)
                    {
                        restoreErrors.Add(restoreError);
                    }
                }

                var failureState = restoreErrors.Count == 0 ? JournalState.Failed : JournalState.RecoveryRequired;
                try
                {
                    await _journal.AppendAsync(
                        CreateJournalEntry(
                            operationId,
                            failureState,
                            "emergency-remove-confirmed",
                            null,
                            previousRules,
                            Array.Empty<ManagedFirewallRule>(),
                            $"Removal or completion journal failed: {removalError.Message}"),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception journalError)
                {
                    restoreErrors.Add(journalError);
                }

                if (restoreErrors.Count > 0)
                {
                    restoreErrors.Insert(0, removalError);
                    throw new AggregateException("Emergency removal failed and one or more recovery steps also failed.", restoreErrors);
                }

                throw new InvalidOperationException("Emergency removal was not completed. The previous Y-Apex rules were restored.", removalError);
            }

            foreach (var operation in _operations.Values)
            {
                operation.State = JournalState.EmergencyRemoval;
                operation.TimerCancellation.Cancel();
            }

            _pending.Clear();
            TrimCompletedOperations();
            return previousRules.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        var operations = _operations.Values.ToArray();
        foreach (var operation in operations)
        {
            operation.TimerCancellation.Cancel();
        }

        var timerTasks = operations.Select(operation => operation.TimerTask).Where(task => task is not null).Cast<Task>().ToArray();
        Task.WhenAll(timerTasks).GetAwaiter().GetResult();
        foreach (var operation in operations)
        {
            operation.TimerCancellation.Dispose();
        }

        _gate.Dispose();
    }

    private async Task MonitorRollbackAsync(OperationState operation)
    {
        try
        {
            await _clock.DelayAsync(_rollbackWindow, operation.TimerCancellation.Token).ConfigureAwait(false);
            await RollbackAfterTimeoutAsync(operation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.TimerCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            try
            {
                await _journal.AppendAsync(
                    CreateJournalEntry(operation.OperationId, JournalState.RecoveryRequired, "automatic-rollback", operation.Preview.ProgramPath, operation.Preview.After, operation.Preview.Before, exception.Message),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task RollbackAfterTimeoutAsync(OperationState operation)
    {
        await _gate.WaitAsync(operation.TimerCancellation.Token).ConfigureAwait(false);
        try
        {
            if (!_pending.ContainsKey(operation.OperationId) || operation.State != JournalState.Applied)
            {
                return;
            }

            await _journal.AppendAsync(
                CreateJournalEntry(operation.OperationId, JournalState.RollingBack, "connectivity-not-confirmed", operation.Preview.ProgramPath, operation.Preview.After, operation.Preview.Before),
                CancellationToken.None).ConfigureAwait(false);
            await _firewall.ReplaceProgramRulesAsync(operation.Preview.ProgramPath, operation.Preview.Before, CancellationToken.None).ConfigureAwait(false);
            await _journal.AppendAsync(
                CreateJournalEntry(operation.OperationId, JournalState.RolledBack, "connectivity-not-confirmed", operation.Preview.ProgramPath, operation.Preview.After, operation.Preview.Before),
                CancellationToken.None).ConfigureAwait(false);
            operation.State = JournalState.RolledBack;
            _pending.TryRemove(operation.OperationId, out _);
            TrimCompletedOperations();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TrimCompletedOperations()
    {
        var excess = _operations.Count - MaximumOperations;
        if (excess <= 0)
        {
            return;
        }

        foreach (var operation in _operations.Values
                     .Where(candidate => !_pending.ContainsKey(candidate.OperationId) && candidate.State != JournalState.Applied)
                     .OrderBy(candidate => candidate.Deadline)
                     .Take(excess))
        {
            _operations.TryRemove(operation.OperationId, out _);
        }
    }

    private async Task AppendFailureAsync(
        Guid operationId,
        RuleChangePreview preview,
        string error,
        JournalState state = JournalState.Failed)
    {
        await _journal.AppendAsync(
            CreateJournalEntry(operationId, state, "firewall-apply-failed", preview.ProgramPath, preview.Before, preview.After, error),
            CancellationToken.None).ConfigureAwait(false);
    }

    private ChangeJournalEntry CreateJournalEntry(
        Guid operationId,
        JournalState state,
        string reason,
        string? programPath,
        IReadOnlyList<ManagedFirewallRule> before,
        IReadOnlyList<ManagedFirewallRule> after,
        string? error = null) =>
        new(operationId, _clock.UtcNow, state, reason, programPath, before, after, error);

    private static bool RulesEqual(
        IReadOnlyList<ManagedFirewallRule> left,
        IReadOnlyList<ManagedFirewallRule> right) =>
        left.OrderBy(rule => rule.RuleId).SequenceEqual(right.OrderBy(rule => rule.RuleId));

    private sealed record EmergencyPreviewState(
        EmergencyRemovalPreview Preview,
        IReadOnlyList<ManagedFirewallRule> Rules);

    private sealed class OperationState(Guid operationId, RuleChangePreview preview, DateTimeOffset deadline, long sequence)
    {
        public Guid OperationId { get; } = operationId;

        public RuleChangePreview Preview { get; } = preview;

        public DateTimeOffset Deadline { get; } = deadline;

        public long Sequence { get; } = sequence;

        public CancellationTokenSource TimerCancellation { get; } = new();

        public Task? TimerTask { get; set; }

        public JournalState State { get; set; } = JournalState.Applied;
    }
}
