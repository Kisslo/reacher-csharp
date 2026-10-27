using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Reserves request start times so calls are spaced across the configured rate.
/// </summary>
public sealed class BraveRequestRateLimiter(
    IOptions<BraveOptions> options,
    TimeProvider timeProvider) : IBraveRequestRateLimiter
{
    private readonly object gate = new();
    private DateTimeOffset nextRequestAt;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(1d / options.Value.RequestsPerSecond);
        TimeSpan delay;

        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var requestAt = nextRequestAt > now ? nextRequestAt : now;
            nextRequestAt = requestAt + interval;
            delay = requestAt - now;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
