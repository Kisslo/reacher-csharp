using Polly.Timeout;

namespace Reacher.Api.Errors;

/// <summary>
/// Network errors and timeouts from Brave or SCB. They are not bugs in this
/// API, so the endpoints answer 502 instead of an empty 500.
/// </summary>
internal static class UpstreamFailures
{
    /// <summary>
    /// True for a failed or timed-out upstream call. False when the caller
    /// cancelled the request: that is an abort, not an outage.
    /// </summary>
    public static bool IsTransport(Exception exception, CancellationToken cancellationToken)
    {
        // HttpClient.Timeout and a Polly attempt timeout both surface as a
        // cancellation that the caller did not ask for. A disconnected client
        // sets the request token, and that one is left for the exception middleware.
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException
            or TimeoutException
            or TimeoutRejectedException
            or OperationCanceledException;
    }
}
