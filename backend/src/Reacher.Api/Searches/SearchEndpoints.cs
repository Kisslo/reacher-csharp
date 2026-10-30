using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Searches;

public static class SearchEndpoints
{
    // App JSON options omit nulls. The contract includes jobId: null and unset job fields.
    private static readonly JsonSerializerOptions IncludeNulls = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/searches",
                async Task<Results<
                    JsonHttpResult<SearchResponse>,
                    JsonHttpResult<SearchProblem>>> (
                    SearchRequest? request,
                    ICodeLookup codes,
                    IIngestCoordinator coordinator,
                    CancellationToken cancellationToken) =>
                {
                    if (!SearchRequestValidator.TryValidate(
                            request,
                            out var search,
                            out var errors))
                    {
                        return Invalid(errors);
                    }

                    try
                    {
                        if (!await codes.IsKnownSniAsync(search.Sni, cancellationToken))
                        {
                            errors.Add(new("sni", "The SNI code is not known."));
                        }

                        var unknown = await codes.UnknownMunicipalitiesAsync(
                            search.Municipalities,
                            cancellationToken);
                        if (unknown.Count > 0)
                        {
                            errors.Add(new(
                                "municipalities",
                                "One or more municipality codes are not known."));
                        }
                    }
                    catch (ScbMaintenanceWindowException)
                    {
                        return Unavailable(
                            StatusCodes.Status503ServiceUnavailable,
                            "SCB is closed for its nightly load. Try after 04:30.");
                    }
                    catch (ScbException)
                    {
                        // The SCB message can name a route. The response stays a fixed sentence.
                        return Unavailable(
                            StatusCodes.Status502BadGateway,
                            "SCB is unavailable.");
                    }

                    if (errors.Count > 0)
                    {
                        return Invalid(errors);
                    }

                    var result = await coordinator.RequestAsync(
                        search.Sni,
                        search.Municipalities,
                        search.Refresh,
                        cancellationToken);
                    var body = new SearchResponse(result.JobId, result.Status);
                    var status = result.JobId is null
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status202Accepted;
                    return TypedResults.Json(body, IncludeNulls, statusCode: status);
                })
            .WithName("StartSearch")
            .WithTags("Searches")
            .WithSummary("Start a search")
            .WithDescription(
                "Returns fresh when every municipality was ingested within the freshness window. "
                + "Otherwise returns the queued or already running ingest job.")
            .Produces<SearchResponse>(StatusCodes.Status200OK)
            .Produces<SearchResponse>(StatusCodes.Status202Accepted)
            .Produces<SearchProblem>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<SearchProblem>(StatusCodes.Status502BadGateway, "application/problem+json")
            .Produces<SearchProblem>(
                StatusCodes.Status503ServiceUnavailable,
                "application/problem+json");

        endpoints.MapGet(
                "/api/jobs/{jobId:guid}",
                async Task<Results<JsonHttpResult<JobResponse>, JsonHttpResult<SearchProblem>>> (
                    Guid jobId,
                    IIngestJobReader jobs,
                    CancellationToken cancellationToken) =>
                {
                    var job = await jobs.FindAsync(jobId, cancellationToken);
                    if (job is null)
                    {
                        return Problem(
                            StatusCodes.Status404NotFound,
                            "job_not_found",
                            "The ingest job was not found.");
                    }

                    return TypedResults.Json(job, IncludeNulls);
                })
            .WithName("GetIngestJob")
            .WithTags("Searches")
            .WithSummary("Read an ingest job")
            .WithDescription("Status of one ingest job. Unknown ids return 404.")
            .Produces<JobResponse>(StatusCodes.Status200OK)
            .Produces<SearchProblem>(StatusCodes.Status404NotFound, "application/problem+json");

        return endpoints;
    }

    private static JsonHttpResult<SearchProblem> Invalid(IReadOnlyList<SearchFieldError> errors) =>
        Problem(StatusCodes.Status400BadRequest, "invalid_search", "Invalid search.", errors);

    private static JsonHttpResult<SearchProblem> Unavailable(int status, string title) =>
        Problem(status, "scb_unavailable", title);

    private static JsonHttpResult<SearchProblem> Problem(
        int status,
        string code,
        string title,
        IReadOnlyList<SearchFieldError>? errors = null) =>
        TypedResults.Json(
            new SearchProblem(
                $"https://httpstatuses.com/{status}",
                title,
                status,
                code,
                errors),
            statusCode: status,
            contentType: "application/problem+json");
}
