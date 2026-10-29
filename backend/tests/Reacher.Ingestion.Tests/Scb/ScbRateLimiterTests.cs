using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Tests.Scb;

public sealed class ScbRateLimiterTests
{
    [Fact]
    public void Two_a_second_books_requests_half_a_second_apart()
    {
        var limiter = new ScbRateLimiter(
            TimeSpan.FromSeconds(0.5),
            new FixedTimeProvider(ScbTestData.Now));

        Assert.Equal(TimeSpan.Zero, limiter.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(0.5), limiter.Reserve());
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.Reserve());
    }

    [Fact]
    public void A_request_after_a_pause_does_not_wait()
    {
        var time = new MovableTimeProvider(ScbTestData.Now);
        var limiter = new ScbRateLimiter(TimeSpan.FromSeconds(0.5), time);

        limiter.Reserve();
        time.Now += TimeSpan.FromSeconds(2);

        Assert.Equal(TimeSpan.Zero, limiter.Reserve());
    }

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
