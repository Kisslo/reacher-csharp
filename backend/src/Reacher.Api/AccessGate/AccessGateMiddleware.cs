using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Reacher.Api.AccessGate;

/// <summary>
/// Requires the shared password (HTTP Basic) on every path except <c>/health</c> (D17).
/// The username is ignored. Nothing here logs the header or the password (hard rule 6).
/// </summary>
public sealed class AccessGateMiddleware(RequestDelegate next, IOptions<AccessGateOptions> options)
{
    private readonly byte[] expectedHash =
        SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Password!));

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || HasPassword(context.Request))
        {
            await next(context);
            return;
        }

        // This header is what makes the browser show its own password prompt.
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Reacher\", charset=\"UTF-8\"";

        var status = StatusCodes.Status401Unauthorized;
        var problem = TypedResults.Json(
            new AccessGateProblem(
                $"https://httpstatuses.com/{status}",
                "A password is required.",
                status,
                "unauthenticated"),
            statusCode: status,
            contentType: "application/problem+json");
        await problem.ExecuteAsync(context);
    }

    private bool HasPassword(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return false;
        }

        var decoded = new byte[header.Parameter.Length];
        if (!Convert.TryFromBase64String(header.Parameter, decoded, out var length))
        {
            return false;
        }

        // Basic credentials are "username:password". Only the first colon separates
        // them, so the password itself may contain colons.
        var credentials = decoded.AsSpan(0, length);
        var colon = credentials.IndexOf((byte)':');
        if (colon < 0)
        {
            return false;
        }

        // Comparing hashes in fixed time gives away neither how many characters
        // matched nor how long the password is.
        var actualHash = SHA256.HashData(credentials[(colon + 1)..]);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

public sealed record AccessGateProblem(string Type, string Title, int Status, string Code);
