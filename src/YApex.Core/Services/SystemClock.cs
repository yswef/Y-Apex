// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using YApex.Core.Abstractions;

namespace YApex.Core.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
