// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Abstractions;
using YApex.Core.Domain;

namespace YApex.Service.SystemIntegration;

public sealed class UnavailableUsageSource : IUsageSource
{
    public Task<UsageSnapshot> GetUsageAsync(DateTimeOffset periodStart, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new UsageSnapshot(0, 0, Array.Empty<ProgramUsage>(), now, IsAvailable: false));
    }
}
