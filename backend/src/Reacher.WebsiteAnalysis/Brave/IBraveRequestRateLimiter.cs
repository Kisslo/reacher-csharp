namespace Reacher.WebsiteAnalysis.Brave;

public interface IBraveRequestRateLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);
}
