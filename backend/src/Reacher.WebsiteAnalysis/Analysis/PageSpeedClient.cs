using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// Calls the PageSpeed Insights API. A failure or timeout returns null so the
/// analysis can continue with our own checks. The request is never logged: the
/// query string holds both the page URL and the API key (D16).
/// </summary>
public sealed partial class PageSpeedClient
{
    private const string Endpoint =
        "https://www.googleapis.com/pagespeedonline/v5/runPagespeed";

    private readonly HttpClient _http;

    private readonly PageSpeedOptions _options;

    private readonly ILogger<PageSpeedClient> _logger;

    public PageSpeedClient(
        HttpClient http,
        IOptions<PageSpeedOptions> options,
        ILogger<PageSpeedClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LighthouseScores?> GetScoresAsync(Uri pageUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Missing setting 'PageSpeed:ApiKey'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri(pageUrl));
        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogStatusFailed(_logger, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadScores(document.RootElement);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(_logger);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            LogFailed(_logger);
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "PageSpeed request failed with status {StatusCode}.")]
    private static partial void LogStatusFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PageSpeed request timed out.")]
    private static partial void LogTimedOut(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PageSpeed request failed.")]
    private static partial void LogFailed(ILogger logger);

    private Uri BuildRequestUri(Uri pageUrl)
    {
        var query = new StringBuilder();
        Append(query, "url", pageUrl.AbsoluteUri);
        Append(query, "strategy", _options.Strategy ?? "mobile");
        Append(query, "category", "performance");
        Append(query, "category", "accessibility");
        Append(query, "category", "best-practices");
        Append(query, "category", "seo");
        Append(query, "key", _options.ApiKey!);
        return new Uri(Endpoint + "?" + query);
    }

    private static void Append(StringBuilder query, string key, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }

        query.Append(Uri.EscapeDataString(key));
        query.Append('=');
        query.Append(Uri.EscapeDataString(value));
    }

    private static LighthouseScores? ReadScores(JsonElement root)
    {
        if (!root.TryGetProperty("lighthouseResult", out var lighthouse)
            || !lighthouse.TryGetProperty("categories", out var categories))
        {
            return null;
        }

        var performance = Scale(categories, "performance");
        var accessibility = Scale(categories, "accessibility");
        var bestPractices = Scale(categories, "best-practices");
        var seo = Scale(categories, "seo");
        if (performance is null || accessibility is null || bestPractices is null || seo is null)
        {
            return null;
        }

        return new LighthouseScores(
            performance.Value,
            accessibility.Value,
            bestPractices.Value,
            seo.Value);
    }

    private static int? Scale(JsonElement categories, string name)
    {
        if (!categories.TryGetProperty(name, out var category)
            || !category.TryGetProperty("score", out var score)
            || score.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        decimal value;
        try
        {
            value = score.GetDecimal();
        }
        catch (FormatException)
        {
            return null;
        }

        if (value < 0 || value > 1)
        {
            return null;
        }

        return (int)decimal.Round(value * 100, 0, MidpointRounding.AwayFromZero);
    }
}
