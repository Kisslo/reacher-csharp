using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Reacher.Data;

namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Atomically consumes one request from the current month's Brave quota.
/// </summary>
public sealed class BraveUsageCounter(
    ReacherDbContext dbContext,
    TimeProvider timeProvider) : IBraveUsageCounter
{
    public async Task<bool> TryConsumeAsync(
        int monthlyCap,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthlyCap);

        var month = timeProvider.GetUtcNow().ToString(
            "yyyy-MM",
            CultureInfo.InvariantCulture);

        var affectedRows = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO brave_usage (month, request_count)
            VALUES ({month}, 1)
            ON CONFLICT (month) DO UPDATE
            SET request_count = brave_usage.request_count + 1
            WHERE brave_usage.request_count < {monthlyCap}
            """,
            cancellationToken);

        return affectedRows == 1;
    }
}
