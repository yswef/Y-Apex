// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Domain;
using YApex.Core.Security;
using YApex.Service.Firewall;
using YApex.Service.SystemIntegration;
using Xunit;

namespace YApex.Service.Tests;

public sealed class WindowsIntegrationSmokeTests
{
    [Fact]
    public async Task ConnectionReaderReturnsValidatedRecords()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var source = new WindowsConnectionSource();
        var records = await source.GetConnectionsAsync(CancellationToken.None);
        Assert.All(records, record =>
        {
            Assert.True(record.Process.ProcessId >= 0);
            Assert.InRange(record.LocalEndpoint.Port, 0, 65535);
            Assert.True(Enum.IsDefined(record.Protocol));
            if (record.Process.Path is not null)
            {
                Assert.Equal(record.Process.Path, WindowsProgramPath.Normalize(record.Process.Path));
            }
        });
    }

    [Fact]
    public async Task FirewallBackendCreatesAndReplacesOwnedRuleShapesWhenExplicitlyEnabled()
    {
        if (!OperatingSystem.IsWindows() ||
            !string.Equals(Environment.GetEnvironmentVariable("YAPEX_RUN_FIREWALL_MUTATION_TEST"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var sourceExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        Assert.True(File.Exists(sourceExecutable), "The Windows smoke-test executable was not found.");
        var testExecutable = Path.Combine(Path.GetTempPath(), $"Y-Apex-Firewall-Smoke-{Guid.NewGuid():N}.exe");
        File.Copy(sourceExecutable, testExecutable);

        try
        {
            using var backend = new WindowsFirewallBackend();
            var allowRules = RuleFactory.CreateProgramRules(testExecutable, FirewallAction.Allow);
            var allowRule = Assert.Single(allowRules);
            Assert.Equal(FirewallDirection.Outbound, allowRule.Direction);
            await backend.ReplaceProgramRulesAsync(testExecutable, allowRules, CancellationToken.None);

            var storedAllowRule = Assert.Single(await backend.GetOwnedRulesForProgramAsync(testExecutable, CancellationToken.None));
            Assert.Equal(FirewallDirection.Outbound, storedAllowRule.Direction);
            Assert.Equal(FirewallAction.Allow, storedAllowRule.Action);

            var legacyAllowRules = new[]
            {
                RuleFactory.CreateOwnedRule(testExecutable, FirewallDirection.Inbound, FirewallAction.Allow),
                RuleFactory.CreateOwnedRule(testExecutable, FirewallDirection.Outbound, FirewallAction.Allow)
            };
            await backend.ReplaceProgramRulesAsync(testExecutable, legacyAllowRules, CancellationToken.None);
            var storedLegacyAllowRules = await backend.GetOwnedRulesForProgramAsync(testExecutable, CancellationToken.None);
            Assert.Equal(2, storedLegacyAllowRules.Count);
            Assert.Contains(storedLegacyAllowRules, rule => rule.Direction == FirewallDirection.Inbound && rule.Action == FirewallAction.Allow);
            Assert.Contains(storedLegacyAllowRules, rule => rule.Direction == FirewallDirection.Outbound && rule.Action == FirewallAction.Allow);

            var blockRules = RuleFactory.CreateProgramRules(testExecutable, FirewallAction.Block);
            await backend.ReplaceProgramRulesAsync(testExecutable, blockRules, CancellationToken.None);
            var storedBlockRules = await backend.GetOwnedRulesForProgramAsync(testExecutable, CancellationToken.None);
            Assert.Equal(2, storedBlockRules.Count);
            Assert.Contains(storedBlockRules, rule => rule.Direction == FirewallDirection.Inbound && rule.Action == FirewallAction.Block);
            Assert.Contains(storedBlockRules, rule => rule.Direction == FirewallDirection.Outbound && rule.Action == FirewallAction.Block);
        }
        finally
        {
            try
            {
                using var cleanup = new WindowsFirewallBackend();
                await cleanup.ReplaceProgramRulesAsync(testExecutable, Array.Empty<ManagedFirewallRule>(), CancellationToken.None);
            }
            finally
            {
                File.Delete(testExecutable);
            }
        }
    }

    [Fact]
    public async Task FirewallBackendReadsOnlyYapexOwnedRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var backend = new WindowsFirewallBackend();
        var rules = await backend.GetOwnedRulesAsync(CancellationToken.None);
        Assert.All(rules, rule => Assert.True(RuleFactory.IsOwnedRule(rule)));
    }
}
