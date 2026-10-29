namespace Reacher.Ingestion.Scb;

/// <summary>
/// At most <c>Scb:RequestsPerSecond</c> requests a second. SCB's limit is undocumented,
/// so this keeps us well below the point where it starts answering 429. A singleton, so
/// every client instance shares the same schedule.
/// </summary>
public sealed class ScbRateLimiter(TimeSpan minInterval, TimeProvider time)
{
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

    /// <summary>Books the next free slot and returns how long to wait for it.</summary>
    internal TimeSpan Reserve()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var requestAt = _nextRequestAt > now ? _nextRequestAt : now;
            _nextRequestAt = requestAt + minInterval;
            return requestAt - now;
        }
    }
}
