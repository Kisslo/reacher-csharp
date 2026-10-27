using System.Security.Authentication;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace Reacher.Ingestion.Scb;

/// <summary>When and how long the SCB client waits before it tries again.</summary>
internal static class ScbResilience
{
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The first attempt plus 3 retries: about 2, 4 and 8 seconds apart.</summary>
    public static RetryStrategyOptions<HttpResponseMessage> RetryOptions() => new()
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(2),
        MaxDelay = MaxDelay,
        UseJitter = true,
        ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome)),
        DelayGenerator = args => ValueTask.FromResult(RetryAfter(args.Outcome.Result)),
    };

    public static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        // A TLS error does not get better by waiting. Fix the machine, never turn off
        // certificate validation.
        if (outcome.Exception is HttpRequestException
            {
                HttpRequestError: HttpRequestError.SecureConnectionError,
            }
            or HttpRequestException { InnerException: AuthenticationException })
        {
            return false;
        }

        // 408, 429, 5xx, network errors and attempt timeouts.
        return HttpClientResiliencePredicates.IsTransient(outcome);
    }

    /// <summary>
    /// SCB's <c>Retry-After</c> in seconds, at most 60. Null means "use the exponential
    /// backoff", also when the header is a date instead of seconds.
    /// </summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage? response) =>
        response?.Headers.RetryAfter?.Delta is { } delta
            ? (delta < MaxDelay ? delta : MaxDelay)
            : null;
}
