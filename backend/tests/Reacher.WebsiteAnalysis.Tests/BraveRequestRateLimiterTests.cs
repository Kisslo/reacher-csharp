using System.Diagnostics;
using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class BraveRequestRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_spaces_reservations_at_the_configured_rate()
    {
        var limiter = new BraveRequestRateLimiter(
            Microsoft.Extensions.Options.Options.Create(new BraveOptions
            {
                RequestsPerSecond = 20,
            }),
            TimeProvider.System);
        var stopwatch = Stopwatch.StartNew();

        await limiter.WaitAsync(CancellationToken.None);
        await limiter.WaitAsync(CancellationToken.None);

        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(35),
            $"Expected reservations to be spaced, elapsed {stopwatch.Elapsed}.");
    }
}
