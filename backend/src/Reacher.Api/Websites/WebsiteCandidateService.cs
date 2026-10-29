using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reacher.Data;
using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Matching;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.Api.Websites;

public interface IWebsiteCandidateService
{
    /// <summary>
    /// Candidates for one company, or null when the orgnr is not a stored company.
    /// The orgnr is never logged.
    /// </summary>
    Task<WebsiteCandidatesResponse?> FindAsync(
        string orgnr,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads the company, asks Brave once, and scores the result. Nothing from Brave
/// is written. The Brave client owns the usage counter.
/// </summary>
public sealed partial class WebsiteCandidateService(
    ReacherDbContext db,
    IBraveSearchClient brave,
    WebsiteMatcher matcher,
    IOptions<BraveOptions> braveOptions,
    TimeProvider time) : IWebsiteCandidateService
{
    public async Task<WebsiteCandidatesResponse?> FindAsync(
        string orgnr,
        CancellationToken cancellationToken)
    {
        // Same shape as ck_company_orgnr. A personnummer cannot match, and a
        // non-matching value is not sent to the database (D2).
        if (string.IsNullOrWhiteSpace(orgnr) || !CompanyOrgnrPattern().IsMatch(orgnr))
        {
            return null;
        }

        var company = await db.Companies
            .AsNoTracking()
            .Where(item => item.Orgnr == orgnr)
            .Select(item => new
            {
                item.Name,
                City = item.Workplaces
                    .OrderBy(workplace => workplace.Cfar)
                    .Select(workplace => workplace.City)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null)
        {
            return null;
        }

        // Assumption: one city, from the workplace with the lowest cfar, so the
        // query stays stable when a company has several workplaces.
        var query = WebsiteQuery.Build(company.Name, company.City);
        var results = await brave.SearchAsync(query, cancellationToken);
        var matched = matcher.Match(company.Name, results);

        var candidates = new List<WebsiteCandidateResponse>(matched.Count);
        foreach (var candidate in matched)
        {
            candidates.Add(new WebsiteCandidateResponse(
                candidate.Url.AbsoluteUri,
                candidate.Title,
                candidate.Confidence,
                candidate.Preselected));
        }

        var remaining = await RemainingQuotaAsync(cancellationToken);
        return new WebsiteCandidatesResponse(candidates, remaining);
    }

    private async Task<int> RemainingQuotaAsync(CancellationToken cancellationToken)
    {
        // Read after the search. The real client has already incremented the
        // counter. The fake client does not, so the stored count is unchanged.
        var month = time.GetUtcNow().ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var used = await db.BraveUsage
            .AsNoTracking()
            .Where(usage => usage.Month == month)
            .Select(usage => (int?)usage.RequestCount)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var remaining = braveOptions.Value.MonthlyCap - used;
        return remaining < 0 ? 0 : remaining;
    }

    [GeneratedRegex("^[0-9]{2}[2-9][0-9]{7}$", RegexOptions.CultureInvariant)]
    private static partial Regex CompanyOrgnrPattern();
}
