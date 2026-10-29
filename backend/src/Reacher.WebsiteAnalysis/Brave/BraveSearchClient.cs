using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Calls Brave's web search API for one company at a time.
/// The query string is sent unchanged as <c>q</c>.
/// </summary>
public sealed class BraveSearchClient(
    HttpClient httpClient,
    IOptions<BraveOptions> options,
    IBraveRequestRateLimiter rateLimiter,
    IBraveUsageCounter usageCounter) : IBraveSearchClient
{
    private const int ResultCount = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BraveSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is required.", nameof(query));
        }

        var braveOptions = options.Value;
        if (string.IsNullOrWhiteSpace(braveOptions.ApiKey))
        {
            throw new InvalidOperationException("Brave Search API key is not configured.");
        }

        if (!await usageCounter.TryConsumeAsync(
                braveOptions.MonthlyCap,
                cancellationToken))
        {
            throw new BraveQuotaExceededException();
        }

        var requestUri =
            $"res/v1/web/search?q={Uri.EscapeDataString(query)}&count={ResultCount}"
            + "&country=se&search_lang=sv";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("X-Subscription-Token", braveOptions.ApiKey);

        await rateLimiter.WaitAsync(cancellationToken);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BraveSearchException(
                "Brave Search returned an unsuccessful response.",
                response.StatusCode);
        }

        BraveSearchResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<BraveSearchResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new BraveSearchException(
                "Brave Search returned an invalid response.",
                exception);
        }

        if (payload is null)
        {
            throw new BraveSearchException("Brave Search returned an invalid response.");
        }

        // Brave omits web when the search has no results. That is an empty
        // list, not a failed call.
        if (payload.Web?.Results is null)
        {
            return [];
        }

        var results = new List<BraveSearchResult>(Math.Min(payload.Web.Results.Count, ResultCount));
        foreach (var result in payload.Web.Results.Take(ResultCount))
        {
            if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var url))
            {
                throw new BraveSearchException("Brave Search returned an invalid result.");
            }

            results.Add(new BraveSearchResult(url, result.Title, result.Description));
        }

        return results;
    }

    private sealed class BraveSearchResponse
    {
        public BraveWebResults? Web { get; init; }
    }

    private sealed class BraveWebResults
    {
        public List<BraveWebResult>? Results { get; init; }
    }

    private sealed class BraveWebResult
    {
        public string? Url { get; init; }

        public string? Title { get; init; }

        public string? Description { get; init; }
    }
}
