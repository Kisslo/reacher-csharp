namespace Reacher.WebsiteAnalysis.Brave;

public interface IBraveUsageCounter
{
    Task<bool> TryConsumeAsync(
        int monthlyCap,
        CancellationToken cancellationToken);
}
