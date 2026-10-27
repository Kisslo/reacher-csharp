using Microsoft.EntityFrameworkCore;
using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.Data.Tests;

public sealed class BraveUsageCounterTests(MigratedPostgresFixture postgres)
    : IClassFixture<MigratedPostgresFixture>
{
    [Fact]
    public async Task Concurrent_consumers_cannot_exceed_the_monthly_cap()
    {
        await using var firstContext = postgres.CreateContext();
        await using var secondContext = postgres.CreateContext();
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var firstCounter = new BraveUsageCounter(firstContext, timeProvider);
        var secondCounter = new BraveUsageCounter(secondContext, timeProvider);

        var results = await Task.WhenAll(
            firstCounter.TryConsumeAsync(1, CancellationToken.None),
            secondCounter.TryConsumeAsync(1, CancellationToken.None));

        Assert.Equal(1, results.Count(result => result));
        Assert.Equal(1, results.Count(result => !result));
        await using var verificationContext = postgres.CreateContext();
        var usage = await verificationContext.BraveUsage.SingleAsync();
        Assert.Equal("2026-10", usage.Month);
        Assert.Equal(1, usage.RequestCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
