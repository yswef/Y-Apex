// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
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
