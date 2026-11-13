using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Reacher.Data;
using Reacher.Data.Entities;

namespace Reacher.Api.Companies;

public static partial class OutreachEndpoints
{
    public static IEndpointRouteBuilder MapOutreachEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(
                "/api/companies/{orgnr}/outreach",
                async Task<Results<NoContent, JsonHttpResult<OutreachProblem>>> (
                    string orgnr,
                    OutreachRequest request,
                    ReacherDbContext db,
                    TimeProvider time,
                    CancellationToken cancellationToken) =>
                {
                    var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
                    if (note is not null && IsRejectedNote(note))
                    {
                        return Problem(
                            StatusCodes.Status400BadRequest,
                            "note_rejected",
                            "The outreach note is not allowed.");
                    }

                    var companyExists = await db.Companies
                        .AnyAsync(company => company.Orgnr == orgnr, cancellationToken);
                    if (!companyExists)
                    {
                        return Problem(
                            StatusCodes.Status404NotFound,
                            "company_not_found",
                            "The company was not found.");
                    }

                    var outreach = await db.CompanyOutreach
                        .SingleOrDefaultAsync(item => item.Orgnr == orgnr, cancellationToken);

                    if (!request.Called && request.Willing is null && note is null)
                    {
                        if (outreach is not null)
                        {
                            db.CompanyOutreach.Remove(outreach);
                            await db.SaveChangesAsync(cancellationToken);
                        }

                        return TypedResults.NoContent();
                    }

                    var now = time.GetUtcNow();
                    if (outreach is null)
                    {
                        outreach = new CompanyOutreach
                        {
                            Orgnr = orgnr,
                            UpdatedAt = now,
                        };
                        db.CompanyOutreach.Add(outreach);
                    }

                    outreach.CalledAt = request.Called
                        ? outreach.CalledAt ?? now
                        : null;
                    outreach.Willing = request.Willing;
                    outreach.Note = note;
                    outreach.UpdatedAt = now;
                    await db.SaveChangesAsync(cancellationToken);

                    return TypedResults.NoContent();
                })
            .WithName("UpdateCompanyOutreach")
            .WithTags("Companies")
            .WithSummary("Update company outreach state")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<OutreachProblem>(
                StatusCodes.Status400BadRequest,
                "application/problem+json")
            .Produces<OutreachProblem>(
                StatusCodes.Status404NotFound,
                "application/problem+json");

        return endpoints;
    }

    private static bool IsRejectedNote(string note) =>
        note.Length > 500
        || UrlPattern().IsMatch(note)
        || PersonnummerPattern().IsMatch(note);

    private static JsonHttpResult<OutreachProblem> Problem(
        int status,
        string code,
        string title) =>
        TypedResults.Json(
            new OutreachProblem(
                "https://httpstatuses.com/" + status,
                title,
                status,
                code),
            statusCode: status,
            contentType: "application/problem+json");

    // http://, https:// or a www. address. A bare domain such as foretag.se is not
    // caught, because a looser pattern would also match abbreviations like "t.ex.".
    [GeneratedRegex(@"https?://|\bwww\.", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Personnummer-like: 6 or 8 digits, an optional - or +, then 4 digits
    // (YYMMDD-NNNN, YYYYMMDDNNNN and so on). There is no date check and no digit
    // boundary, so orgnr, phone numbers and longer digit runs are rejected too.
    [GeneratedRegex(@"\d{6}(?:\d{2})?[-+]?\d{4}")]
    private static partial Regex PersonnummerPattern();
}

public sealed record OutreachRequest(bool Called, bool? Willing, string? Note);

public sealed record OutreachProblem(string Type, string Title, int Status, string Code);
