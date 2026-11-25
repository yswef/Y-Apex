// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;
using YApex.Core.Services;
using Xunit;

namespace YApex.Core.Tests;

public sealed class RuleChangeCoordinatorTests
{
    [Fact]
    public async Task ChangeRequiresConfirmationAndCanBeUndone()
    {
        using var coordinator = CreateCoordinator(out var firewall, out var journal, out _);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyConfirmedAsync(preview.PreviewId, false, CancellationToken.None));
        var result = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        Assert.Equal(2, (await firewall.GetOwnedRulesAsync(CancellationToken.None)).Count);
        Assert.True(await coordinator.UndoAsync(result.OperationId, CancellationToken.None));
        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Contains(journal.Entries, entry => entry.State == JournalState.RolledBack);
    }

    [Fact]
    public async Task AllowCreatesOnlyAnOutboundAllowRule()
    {
        using var coordinator = CreateCoordinator(out var firewall, out _, out _);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);

        var plannedRule = Assert.Single(preview.After);
        Assert.Equal(FirewallDirection.Outbound, plannedRule.Direction);
        Assert.Equal(FirewallAction.Allow, plannedRule.Action);

        _ = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        var appliedRule = Assert.Single(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Equal(FirewallDirection.Outbound, appliedRule.Direction);
        Assert.Equal(FirewallAction.Allow, appliedRule.Action);
    }

    [Fact]
    public async Task UndoRestoresLegacyBidirectionalAllowRules()
    {
        using var coordinator = CreateCoordinator(out var firewall, out _, out _);
        const string programPath = "C:\\Apps\\legacy.exe";
        var legacyRules = new[]
        {
            RuleFactory.CreateOwnedRule(programPath, FirewallDirection.Inbound, FirewallAction.Allow),
            RuleFactory.CreateOwnedRule(programPath, FirewallDirection.Outbound, FirewallAction.Allow)
        };
        firewall.SetOwnedRules(legacyRules);

        var preview = await coordinator.CreatePreviewAsync(programPath, FirewallAction.Block, CancellationToken.None);
        var applied = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        Assert.All(await firewall.GetOwnedRulesAsync(CancellationToken.None), rule => Assert.Equal(FirewallAction.Block, rule.Action));
        Assert.True(await coordinator.UndoAsync(applied.OperationId, CancellationToken.None));

        Assert.Equal(legacyRules, await firewall.GetOwnedRulesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ActiveOperationSnapshotsTrackPendingConfirmedAndUndoneStates()
    {
        using var coordinator = CreateCoordinator(out _, out _, out _);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        var applied = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);

        var pending = await coordinator.GetActiveOperationsAsync(CancellationToken.None);
        var pendingOperation = Assert.Single(pending);
        Assert.Equal(applied.OperationId, pendingOperation.OperationId);
        Assert.False(pendingOperation.ConnectivityConfirmed);

        Assert.True(await coordinator.ConfirmConnectivityAsync(applied.OperationId, CancellationToken.None));
        var confirmed = Assert.Single(await coordinator.GetActiveOperationsAsync(CancellationToken.None));
        Assert.True(confirmed.ConnectivityConfirmed);

        Assert.True(await coordinator.UndoAsync(applied.OperationId, CancellationToken.None));
        Assert.Empty(await coordinator.GetActiveOperationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnconfirmedChangeIsRestoredAfterTheRollbackWindow()
    {
        using var coordinator = CreateCoordinator(out var firewall, out var journal, out var clock);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        var result = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);

        await clock.AdvanceAsync(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(async () => (await firewall.GetOwnedRulesAsync(CancellationToken.None)).Count == 0);

        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Contains(journal.Entries, entry => entry.OperationId == result.OperationId && entry.State == JournalState.RolledBack);
    }

    [Fact]
    public async Task ChangeForTheSameProgramMustResolveThePreviousUndoWindowFirst()
    {
        using var coordinator = CreateCoordinator(out _, out _, out _);
        var firstPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        var first = await coordinator.ApplyConfirmedAsync(firstPreview.PreviewId, true, CancellationToken.None);
        var conflictingPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyConfirmedAsync(conflictingPreview.PreviewId, true, CancellationToken.None));
        Assert.True(await coordinator.UndoAsync(first.OperationId, CancellationToken.None));

        var nextPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(nextPreview.PreviewId, true, CancellationToken.None);
    }

    [Fact]
    public async Task ConfirmedChangesForTheSameProgramCanBeChangedAndUndoneInReverseOrder()
    {
        using var coordinator = CreateCoordinator(out var firewall, out _, out _);
        var firstPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        var first = await coordinator.ApplyConfirmedAsync(firstPreview.PreviewId, true, CancellationToken.None);
        Assert.True(await coordinator.ConfirmConnectivityAsync(first.OperationId, CancellationToken.None));

        var secondPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);
        var second = await coordinator.ApplyConfirmedAsync(secondPreview.PreviewId, true, CancellationToken.None);

        Assert.False(await coordinator.UndoAsync(first.OperationId, CancellationToken.None));
        Assert.True(await coordinator.UndoAsync(second.OperationId, CancellationToken.None));
        Assert.True(await coordinator.UndoAsync(first.OperationId, CancellationToken.None));
        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartupRecoveryRestoresAnUnconfirmedChange()
    {
        var coordinator = CreateCoordinator(out var firewall, out var journal, out var clock);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        coordinator.Dispose();

        using var recovered = new RuleChangeCoordinator(firewall, journal, clock);
        await recovered.RecoverInterruptedOperationsAsync(CancellationToken.None);

        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Contains(journal.Entries, entry => entry.State == JournalState.RolledBack && entry.Reason == "startup-recovery");
    }

    [Fact]
    public async Task StartupRecoveryRestoresLatestUnconfirmedChangeToThePreviouslyConfirmedState()
    {
        var coordinator = CreateCoordinator(out var firewall, out var journal, out var clock);
        var firstPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        var first = await coordinator.ApplyConfirmedAsync(firstPreview.PreviewId, true, CancellationToken.None);
        Assert.True(await coordinator.ConfirmConnectivityAsync(first.OperationId, CancellationToken.None));

        var secondPreview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(secondPreview.PreviewId, true, CancellationToken.None);
        coordinator.Dispose();

        using var recovered = new RuleChangeCoordinator(firewall, journal, clock);
        await recovered.RecoverInterruptedOperationsAsync(CancellationToken.None);

        var restoredRules = await firewall.GetOwnedRulesAsync(CancellationToken.None);
        Assert.Equal(2, restoredRules.Count);
        Assert.All(restoredRules, rule => Assert.Equal(FirewallAction.Block, rule.Action));
        Assert.Single(journal.Entries, entry => entry.Reason == "startup-recovery");
        Assert.Empty(await journal.GetUnfinishedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SuccessfulEmergencyRemovalSupersedesOlderPendingOperationsOnRecovery()
    {
        var coordinator = CreateCoordinator(out var firewall, out var journal, out var clock);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Block, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        var emergency = await coordinator.CreateEmergencyRemovalPreviewAsync(CancellationToken.None);
        _ = await coordinator.ConfirmEmergencyRemovalAsync(emergency.PreviewId, true, CancellationToken.None);
        coordinator.Dispose();

        using var recovered = new RuleChangeCoordinator(firewall, journal, clock);
        await recovered.RecoverInterruptedOperationsAsync(CancellationToken.None);

        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.DoesNotContain(journal.Entries, entry => entry.Reason == "startup-recovery");
    }

    [Fact]
    public async Task EmergencyRemovalRestoresRulesWhenCompletionJournalFails()
    {
        using var coordinator = CreateCoordinator(out var firewall, out var journal, out _);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);
        var emergency = await coordinator.CreateEmergencyRemovalPreviewAsync(CancellationToken.None);
        journal.FailOnState = JournalState.EmergencyRemoval;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ConfirmEmergencyRemovalAsync(emergency.PreviewId, true, CancellationToken.None));

        Assert.Single(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Contains(journal.Entries, entry => entry.State == JournalState.Failed);
    }

    [Fact]
    public async Task EmergencyRemovalDeletesOnlyTaggedYapexRules()
    {
        using var coordinator = CreateCoordinator(out var firewall, out _, out _);
        var preview = await coordinator.CreatePreviewAsync("C:\\Apps\\demo.exe", FirewallAction.Allow, CancellationToken.None);
        _ = await coordinator.ApplyConfirmedAsync(preview.PreviewId, true, CancellationToken.None);

        var emergencyPreview = await coordinator.CreateEmergencyRemovalPreviewAsync(CancellationToken.None);
        Assert.Equal(1, emergencyPreview.RuleCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ConfirmEmergencyRemovalAsync(emergencyPreview.PreviewId, false, CancellationToken.None));
        var removed = await coordinator.ConfirmEmergencyRemovalAsync(emergencyPreview.PreviewId, true, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(await firewall.GetOwnedRulesAsync(CancellationToken.None));
        Assert.Equal(1, firewall.UnownedRuleCount);
    }

    private static RuleChangeCoordinator CreateCoordinator(
        out FakeFirewallBackend firewall,
        out FakeJournal journal,
        out FakeClock clock)
    {
        firewall = new FakeFirewallBackend();
        journal = new FakeJournal();
        clock = new FakeClock();
        return new RuleChangeCoordinator(firewall, journal, clock, TimeSpan.FromSeconds(30));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!await predicate().ConfigureAwait(false))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, timeout.Token).ConfigureAwait(false);
        }
    }

    private sealed class FakeFirewallBackend : IFirewallBackend
    {
        private readonly List<ManagedFirewallRule> _ownedRules = [];

        public int UnownedRuleCount { get; private set; } = 1;

        public void SetOwnedRules(IEnumerable<ManagedFirewallRule> rules)
        {
            _ownedRules.Clear();
            _ownedRules.AddRange(rules);
        }

        public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ManagedFirewallRule>>(_ownedRules.ToArray());
        }

        public Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesForProgramAsync(
            string normalizedProgramPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ManagedFirewallRule> result = _ownedRules
                .Where(rule => string.Equals(rule.ProgramPath, normalizedProgramPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task ReplaceProgramRulesAsync(
            string normalizedProgramPath,
            IReadOnlyList<ManagedFirewallRule> replacement,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ownedRules.RemoveAll(rule => string.Equals(rule.ProgramPath, normalizedProgramPath, StringComparison.OrdinalIgnoreCase));
            _ownedRules.AddRange(replacement);
            return Task.CompletedTask;
        }

        public Task RemoveOwnedRulesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ownedRules.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeJournal : IChangeJournal
    {
        public List<ChangeJournalEntry> Entries { get; } = [];

        public JournalState? FailOnState { get; set; }

        public Task AppendAsync(ChangeJournalEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.State == FailOnState)
            {
                throw new IOException($"Simulated journal failure for {entry.State}.");
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChangeJournalEntry>> GetUnfinishedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indexed = Entries.Select((entry, index) => new { Entry = entry, Index = index }).ToArray();
            var emergencyBarrier = indexed
                .Where(item => item.Entry.State == JournalState.EmergencyRemoval && item.Entry.Reason == "emergency-remove-confirmed")
                .Select(item => item.Index)
                .DefaultIfEmpty(-1)
                .Max();
            IReadOnlyList<ChangeJournalEntry> unfinished = indexed
                .GroupBy(item => item.Entry.OperationId)
                .Select(group => group.OrderByDescending(item => item.Index).First())
                .Where(item => item.Index > emergencyBarrier &&
                    item.Entry.State is JournalState.Planned or JournalState.Applied or JournalState.RollingBack or JournalState.RecoveryRequired)
                .OrderByDescending(item => item.Index)
                .Select(item => item.Entry)
                .ToArray();
            return Task.FromResult(unfinished);
        }

        public Task<IReadOnlyList<ChangeJournalSummary>> GetRecentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ChangeJournalSummary> summaries = Entries
                .TakeLast(32)
                .Reverse()
                .Select(entry => new ChangeJournalSummary(
                    entry.OperationId,
                    entry.Timestamp,
                    entry.State,
                    entry.Reason,
                    entry.ProgramPath))
                .ToArray();
            return Task.FromResult(summaries);
        }
    }

    private sealed class FakeClock : IClock
    {
        private readonly object _gate = new();
        private readonly List<ScheduledDelay> _delays = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ScheduledDelay scheduled;
            lock (_gate)
            {
                scheduled = new ScheduledDelay(UtcNow.Add(delay), completion);
                _delays.Add(scheduled);
            }

            if (cancellationToken.CanBeCanceled)
            {
                scheduled.Registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            return completion.Task;
        }

        public Task AdvanceAsync(TimeSpan amount)
        {
            List<ScheduledDelay> due;
            lock (_gate)
            {
                UtcNow = UtcNow.Add(amount);
                due = _delays.Where(delay => delay.DueAt <= UtcNow).ToList();
                _delays.RemoveAll(delay => delay.DueAt <= UtcNow);
            }

            foreach (var delay in due)
            {
                delay.Registration.Dispose();
                delay.Completion.TrySetResult();
            }

            return Task.CompletedTask;
        }

        private sealed class ScheduledDelay(DateTimeOffset dueAt, TaskCompletionSource completion)
        {
            public DateTimeOffset DueAt { get; } = dueAt;

            public TaskCompletionSource Completion { get; } = completion;

            public CancellationTokenRegistration Registration { get; set; }
        }
    }
}
