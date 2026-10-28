using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Holds one OAuth2 token (client credentials) for the whole app and renews it a minute
/// before it expires. A singleton, because the typed client is created per use.
/// </summary>
/// <remarks>The token, client id and client secret are never logged or put in a message.</remarks>
internal sealed class BolagsverketTokenCache(
    IHttpClientFactory httpClients,
    IOptions<BolagsverketOptions> options,
    TimeProvider time) : IDisposable
{
    public const string HttpClientName = "Bolagsverket.Token";

    /// <summary>Renew this long before expiry, so a token never expires on its way.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    // One caller at a time, so two calls that both see an old token fetch only one new one.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;

    private DateTimeOffset _renewAt;

    /// <summary>A valid token, fetched only when needed.</summary>
    /// <param name="rejected">
    /// A token the API just answered 401 to, or null. If it is still the cached one, a new
    /// token is fetched; if another call already replaced it, the new one is returned.
    /// </param>
    public async Task<string> GetToken(string? rejected, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is null || _token == rejected || time.GetUtcNow() >= _renewAt)
            {
                (_token, _renewAt) = await FetchToken(cancellationToken);
            }

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<(string Token, DateTimeOffset RenewAt)> FetchToken(
        CancellationToken cancellationToken)
    {
        const string route = "POST /oauth2/token";
        var settings = options.Value;
        var tokenUrl = settings.TokenUrl
            ?? throw new InvalidOperationException("Missing setting 'Bolagsverket:TokenUrl'.");
        var scope = settings.Scope
            ?? throw new InvalidOperationException("Missing setting 'Bolagsverket:Scope'.");

        // The only place the client id and secret are used.
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(tokenUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(
        [
            new("grant_type", "client_credentials"),
            new("scope", scope),
        ]);

        // The token's lifetime starts when we ask, not when the answer arrives.
        var requestedAt = time.GetUtcNow();
        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.SendAsync(request, cancellationToken);

        var status = (int)response.StatusCode;
        if (response.StatusCode
            is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new BolagsverketAuthException(
                $"{route}: HTTP {status}. Check the settings 'Bolagsverket:ClientId' and "
                + "'Bolagsverket:ClientSecret'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BolagsverketException($"{route}: HTTP {status}.");
        }

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (string.IsNullOrEmpty(body?.AccessToken))
        {
            throw new BolagsverketException($"{route}: the response has no access_token.");
        }

        // expires_in <= 60 makes the token count as expired at once, so every call fetches
        // a new one. Slow, but never a call with an expired token.
        var expiresIn = TimeSpan.FromSeconds(body.ExpiresIn ?? 0);
        return (body.AccessToken, requestedAt + expiresIn - RenewBefore);
    }
}
