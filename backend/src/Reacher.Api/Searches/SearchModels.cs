namespace Reacher.Api.Searches;

/// <summary>Body of <c>POST /api/searches</c>.</summary>
public sealed record SearchRequest(
    string? Sni,
    List<string>? Municipalities,
    bool Refresh);

/// <summary>
/// <c>200</c> when the data is fresh (<c>jobId</c> is null) and <c>202</c> when a job is
/// queued or already running.
/// </summary>
public sealed record SearchResponse(Guid? JobId, string Status);

/// <summary>Body of <c>GET /api/jobs/{jobId}</c>.</summary>
public sealed record JobResponse(
    Guid JobId,
    string Status,
    string? Phase,
    int Done,
    int? Total,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record SearchProblem(
    string Type,
    string Title,
    int Status,
    string Code,
    IReadOnlyList<SearchFieldError>? Errors);

public sealed record SearchFieldError(string Field, string Message);
