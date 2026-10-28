namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// At most one API request a second (team rule; Bolagsverket allows 60 a minute).
/// A singleton, so every client instance shares the same schedule.
/// </summary>
internal sealed class BolagsverketRateLimiter(TimeProvider time)
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();

    private DateTimeOffset _nextRequestAt;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var delay = Reserve();
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, time, cancellationToken);
        }
    }

    /// <summary>Books the next free second and returns how long to wait for it.</summary>
    internal TimeSpan Reserve()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var requestAt = _nextRequestAt > now ? _nextRequestAt : now;
            _nextRequestAt = requestAt + MinInterval;
            return requestAt - now;
        }
    }
}
