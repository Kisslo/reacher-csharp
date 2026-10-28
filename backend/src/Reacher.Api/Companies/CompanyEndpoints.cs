using Microsoft.AspNetCore.Http.HttpResults;

namespace Reacher.Api.Companies;

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/companies",
                async Task<Results<Ok<CompanySearchResponse>, JsonHttpResult<CompanySearchProblem>>> (
                    [AsParameters] CompanySearchQuery query,
                    ICompanyQueryService companyQueryService,
                    CancellationToken cancellationToken) =>
                {
                    if (!CompanySearchQueryValidator.TryValidate(
                            query,
                            out var parameters,
                            out var errors))
                    {
                        return TypedResults.Json(
                            new CompanySearchProblem(
                                "https://httpstatuses.com/400",
                                "Invalid company search query.",
                                400,
                                "invalid_search_query",
                                errors),
                            statusCode: StatusCodes.Status400BadRequest,
                            contentType: "application/problem+json");
                    }

                    var response = await companyQueryService.SearchAsync(
                        parameters!,
                        cancellationToken);
                    return TypedResults.Ok(response);
                })
            .WithName("SearchCompanies")
            .WithTags("Companies")
            .WithSummary("Search companies")
            .WithDescription(
                "Filters, sorts and pages eligible companies by SNI and workplace municipality.")
            .Produces<CompanySearchResponse>(StatusCodes.Status200OK)
            .Produces<CompanySearchProblem>(
                StatusCodes.Status400BadRequest,
                "application/problem+json");

        return endpoints;
    }
}

public sealed record CompanySearchProblem(
    string Type,
    string Title,
    int Status,
    string Code,
    IReadOnlyList<CompanySearchQueryError> Errors);
