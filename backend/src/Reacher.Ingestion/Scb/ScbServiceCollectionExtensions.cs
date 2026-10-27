using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Scb;

public static class ScbServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IScbClient"/> as a typed HttpClient. Needs
    /// <see cref="ScbOptions"/> to be bound (AddReacherOptions in Reacher.Api).
    /// </summary>
    public static IHttpClientBuilder AddScbClient(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        var builder = services.AddHttpClient<IScbClient, ScbClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ScbOptions>>().Value;
            var baseUrl = options.BaseUrl
                ?? throw new InvalidOperationException("Missing setting 'Scb:BaseUrl'.");
            var apiKey = options.ApiKey
                ?? throw new InvalidOperationException("Missing setting 'Scb:ApiKey'.");

            // The trailing slash matters: without it, "v1" is dropped from relative paths.
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            // A safety net over all attempts. Each attempt has its own 30 s timeout below.
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // The default HttpClient loggers write every request URL, and SCB URLs hold a
        // peOrgNr (hard rules 1 and 6). Remove them.
        builder.RemoveAllLoggers();

        // Retry is the outer strategy, so every attempt gets a fresh 30 s timeout.
        builder.AddResilienceHandler("scb", pipeline => pipeline
            .AddRetry(ScbResilience.RetryOptions())
            .AddTimeout(ScbResilience.AttemptTimeout));

        return builder;
    }
}
