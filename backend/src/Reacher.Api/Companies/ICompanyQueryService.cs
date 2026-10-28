namespace Reacher.Api.Companies;

public interface ICompanyQueryService
{
    Task<CompanySearchResponse> SearchAsync(
        CompanySearchParameters parameters,
        CancellationToken cancellationToken);
}
