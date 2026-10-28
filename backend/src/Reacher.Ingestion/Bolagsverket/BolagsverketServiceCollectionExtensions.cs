using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Bolagsverket;

public static class BolagsverketServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IBolagsverketClient"/> as a typed HttpClient, plus a named client
    /// for the token. Needs <see cref="BolagsverketOptions"/> to be bound (AddReacherOptions
    /// in Reacher.Api).
    /// </summary>
    public static IServiceCollection AddBolagsverketClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<BolagsverketTokenCache>();
        services.TryAddSingleton<BolagsverketRateLimiter>();

        // The default HttpClient loggers are removed from both clients. The token request
        // carries the client secret and its answer the token (hard rules 6 and 7).
        services.AddHttpClient(BolagsverketTokenCache.HttpClientName)
            .RemoveAllLoggers()
            .AddResilienceHandler("bolagsverket-token", AddRetries);

        services.AddHttpClient<IBolagsverketClient, BolagsverketClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<BolagsverketOptions>>().Value;
            var apiUrl = options.ApiUrl
                ?? throw new InvalidOperationException("Missing setting 'Bolagsverket:ApiUrl'.");

            // The trailing slash matters: without it, "v1" is dropped from relative paths.
            client.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            // A larger answer fails before it is read into memory. Not a report anyway.
            client.MaxResponseContentBufferSize = ReportFiles.MaxReportBytes;

            // A safety net over all attempts. Each attempt has its own 30 s timeout.
            client.Timeout = TimeSpan.FromMinutes(5);
        })
        .RemoveAllLoggers()
        .AddResilienceHandler("bolagsverket", AddRetries);

        return services;
    }

    // The same rules as SCB: 429, 5xx and network errors are retried 2, 4 and 8 s apart,
    // Retry-After is followed up to 60 s, and a TLS error is never retried.
    private static void AddRetries(ResiliencePipelineBuilder<HttpResponseMessage> pipeline) =>
        pipeline
            .AddRetry(ScbResilience.RetryOptions())
            .AddTimeout(ScbResilience.AttemptTimeout);
}
