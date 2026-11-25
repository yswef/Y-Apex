// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Abstractions;

namespace YApex.Core.Services;

public sealed class OfflineUpdateChecker : IUpdateChecker
{
    public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new UpdateCheckResult(
            IsConfigured: false,
            Message: "No update source is configured. No network request was made."));
    }
}
