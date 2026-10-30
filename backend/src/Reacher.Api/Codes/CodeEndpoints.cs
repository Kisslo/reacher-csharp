using Microsoft.AspNetCore.Http.HttpResults;
using Reacher.Ingestion.Scb;

namespace Reacher.Api.Codes;

public static class CodeEndpoints
{
    private const int SniLength = 5;

    public static IEndpointRouteBuilder MapCodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var codes = endpoints.MapGroup("/api/codes").WithTags("Codes");
        codes.AddEndpointFilter(ScbFailuresAsProblems);

        codes.MapGet(
                "/municipalities",
                async Task<Ok<IReadOnlyList<CodeResponse>>> (
                    ICodeTables tables,
                    CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tables.GetMunicipalitiesAsync(cancellationToken)))
            .WithName("ListMunicipalities")
            .WithSummary("List municipalities")
            .WithDescription("Every municipality from SCB, sorted by name.")
            .WithScbProblems();

        codes.MapGet(
                "/employee-classes",
                async Task<Ok<IReadOnlyList<CodeResponse>>> (
                    ICodeTables tables,
                    CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tables.GetEmployeeClassesAsync(cancellationToken)))
            .WithName("ListEmployeeClasses")
            .WithSummary("List employee classes")
            .WithDescription("SCB's employee size classes, smallest first.")
            .WithScbProblems();

        codes.MapGet(
                "/revenue-classes",
                async Task<Ok<IReadOnlyList<CodeResponse>>> (
                    ICodeTables tables,
                    CancellationToken cancellationToken) =>
                    TypedResults.Ok(await tables.GetRevenueClassesAsync(cancellationToken)))
            .WithName("ListRevenueClasses")
            .WithSummary("List revenue classes")
            .WithDescription("SCB's revenue size classes, smallest first.")
            .WithScbProblems();

        codes.MapGet(
                "/sni/{code}",
                async Task<Results<Ok<CodeResponse>, JsonHttpResult<CodeProblem>>> (
                    string code,
                    ICodeTables tables,
                    CancellationToken cancellationToken) =>
                {
                    if (code.Length != SniLength || !code.All(char.IsAsciiDigit))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "invalid_sni",
                            "The SNI code must be 5 digits.");
                    }

                    var sni = await tables.FindSniAsync(code, cancellationToken);
                    if (sni is null)
                    {
                        return Problem(
                            StatusCodes.Status404NotFound,
                            "sni_not_found",
                            "The SNI code was not found.");
                    }

                    return TypedResults.Ok(sni);
                })
            .WithName("GetSni")
            .WithSummary("Read one SNI code")
            .WithDescription("The name of a 5-digit SNI code. Unknown codes return 404.")
            .Produces<CodeProblem>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<CodeProblem>(StatusCodes.Status404NotFound, "application/problem+json")
            .WithScbProblems();

        return endpoints;
    }

    private static RouteHandlerBuilder WithScbProblems(this RouteHandlerBuilder builder) =>
        builder
            .Produces<CodeProblem>(StatusCodes.Status502BadGateway, "application/problem+json")
            .Produces<CodeProblem>(
                StatusCodes.Status503ServiceUnavailable,
                "application/problem+json");

    private static async ValueTask<object?> ScbFailuresAsProblems(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ScbMaintenanceWindowException)
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "scb_unavailable",
                "SCB is closed for its nightly load. Try after 04:30.");
        }
        catch (ScbException)
        {
            // The SCB message can name a route. The response stays a fixed sentence.
            return Problem(
                StatusCodes.Status502BadGateway,
                "scb_unavailable",
                "SCB is unavailable.");
        }
    }

    private static JsonHttpResult<CodeProblem> Problem(int status, string code, string title) =>
        TypedResults.Json(
            new CodeProblem(
                $"https://httpstatuses.com/{status}",
                title,
                status,
                code),
            statusCode: status,
            contentType: "application/problem+json");
}
