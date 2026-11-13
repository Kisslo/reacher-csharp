using Microsoft.AspNetCore.Diagnostics;

namespace Reacher.Api.Errors;

/// <summary>
/// Last resort for an exception no endpoint caught. The body is fixed:
/// the exception message can hold a URL, a key or a database value, so it
/// is never written or logged (hard rule 6). Handling it here also stops
/// the exception middleware from logging that message.
/// </summary>
internal sealed partial class InternalErrorExceptionHandler(ILogger<InternalErrorExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogUnhandled(logger, exception.GetType().Name);

        var problem = TypedResults.Json(
            new InternalErrorProblem(
                "https://httpstatuses.com/500",
                "An unexpected error occurred.",
                StatusCodes.Status500InternalServerError,
                "internal_error"),
            statusCode: StatusCodes.Status500InternalServerError,
            contentType: "application/problem+json");
        await problem.ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unhandled exception ({ExceptionType}).")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType);
}

internal sealed record InternalErrorProblem(string Type, string Title, int Status, string Code);
