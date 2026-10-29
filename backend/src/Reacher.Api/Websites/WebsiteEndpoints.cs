using Microsoft.AspNetCore.Http.HttpResults;
using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.Api.Websites;

public static class WebsiteEndpoints
{
    public static IEndpointRouteBuilder MapWebsiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Do not enable HTTP request logging for these routes. The analysis body
        // is a website URL, and a URL from Brave or a user must never be logged (D16).
        var websites = endpoints.MapGroup("/api");
        websites.AddEndpointFilter(NoStore);

        websites.MapPost(
                "/companies/{orgnr}/website-candidates",
                async Task<Results<Ok<WebsiteCandidatesResponse>, JsonHttpResult<WebsiteProblem>>> (
                    string orgnr,
                    IWebsiteCandidateService candidates,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var response = await candidates.FindAsync(orgnr, cancellationToken);
                        if (response is null)
                        {
                            return Problem(
                                StatusCodes.Status404NotFound,
                                "company_not_found",
                                "The company was not found.");
                        }

                        return TypedResults.Ok(response);
                    }
                    catch (BraveQuotaExceededException)
                    {
                        return Problem(
                            StatusCodes.Status429TooManyRequests,
                            "brave_quota_exceeded",
                            "The monthly Brave Search quota has been exceeded.");
                    }
                    catch (BraveSearchException)
                    {
                        return Problem(
                            StatusCodes.Status502BadGateway,
                            "brave_unavailable",
                            "Brave Search is unavailable.");
                    }
                })
            .WithName("FindWebsiteCandidates")
            .WithTags("Websites")
            .WithSummary("Find website candidates for one company")
            .WithDescription(
                "Runs one Brave search and returns scored candidates. "
                + "Nothing from Brave is stored. The response must not be cached.")
            .Produces<WebsiteCandidatesResponse>(StatusCodes.Status200OK)
            .Produces<WebsiteProblem>(
                StatusCodes.Status404NotFound,
                "application/problem+json")
            .Produces<WebsiteProblem>(
                StatusCodes.Status429TooManyRequests,
                "application/problem+json")
            .Produces<WebsiteProblem>(
                StatusCodes.Status502BadGateway,
                "application/problem+json");

        websites.MapPost(
                "/website-analysis",
                async Task<Results<Ok<WebsiteAnalysisResponse>, JsonHttpResult<WebsiteProblem>>> (
                    WebsiteAnalysisRequest? request,
                    WebsiteAnalyzer analyzer,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var result = await analyzer.AnalyzeAsync(
                            request?.Url ?? string.Empty,
                            cancellationToken);
                        return TypedResults.Ok(ToResponse(result));
                    }
                    catch (AnalysisException exception)
                    {
                        return AnalysisProblem(exception);
                    }
                })
            .WithName("AnalyzeWebsite")
            .WithTags("Websites")
            .WithSummary("Analyse one website")
            .WithDescription(
                "Validates the URL, then returns a rework score. "
                + "The result is not stored. The response must not be cached.")
            .Produces<WebsiteAnalysisResponse>(StatusCodes.Status200OK)
            .Produces<WebsiteProblem>(
                StatusCodes.Status400BadRequest,
                "application/problem+json")
            .Produces<WebsiteProblem>(
                StatusCodes.Status502BadGateway,
                "application/problem+json")
            .Produces<WebsiteProblem>(
                StatusCodes.Status504GatewayTimeout,
                "application/problem+json");

        return endpoints;
    }

    private static async ValueTask<object?> NoStore(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Response.OnStarting(() =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });

        return await next(context);
    }

    private static WebsiteAnalysisResponse ToResponse(WebsiteAnalysisResult result)
    {
        LighthouseResponse? lighthouse = result.Lighthouse is null
            ? null
            : new LighthouseResponse(
                result.Lighthouse.Performance,
                result.Lighthouse.Accessibility,
                result.Lighthouse.BestPractices,
                result.Lighthouse.Seo);

        return new WebsiteAnalysisResponse(
            result.FinalUrl.AbsoluteUri,
            result.FetchedAt,
            result.ReworkScore,
            lighthouse,
            new WebsiteChecksResponse(
                result.Checks.Https,
                result.Checks.Viewport,
                result.Checks.Generator,
                result.Checks.CopyrightYear,
                result.Checks.ResponseTimeMs));
    }

    private static JsonHttpResult<WebsiteProblem> AnalysisProblem(AnalysisException exception) =>
        exception.Code switch
        {
            AnalysisException.InvalidUrlCode => Problem(
                StatusCodes.Status400BadRequest,
                exception.Code,
                "The URL is not allowed."),
            AnalysisException.TimeoutCode => Problem(
                StatusCodes.Status504GatewayTimeout,
                exception.Code,
                "The page fetch timed out."),
            _ => Problem(
                StatusCodes.Status502BadGateway,
                AnalysisException.FetchFailedCode,
                "The page could not be fetched."),
        };

    private static JsonHttpResult<WebsiteProblem> Problem(int status, string code, string title) =>
        TypedResults.Json(
            new WebsiteProblem(
                $"https://httpstatuses.com/{status}",
                title,
                status,
                code),
            statusCode: status,
            contentType: "application/problem+json");
}
