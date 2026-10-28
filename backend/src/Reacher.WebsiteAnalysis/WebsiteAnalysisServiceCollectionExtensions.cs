using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Matching;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis;

/// <summary>Registers website analysis. Neither HTTP client logs its request URI.</summary>
public static class WebsiteAnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddWebsiteAnalysis(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDnsLookup, SystemDnsLookup>();
        services.AddSingleton<UrlSafety>();
        services.AddSingleton<ReworkScoreCalculator>();
        services.AddSingleton<WebsiteMatcher>();
        services.AddTransient<WebsiteAnalyzer>();

        // The query string contains the page URL and the API key (D16).
        services.AddHttpClient<PageSpeedClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PageSpeedOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .RemoveAllLoggers();

        services.AddHttpClient<WebsitePageFetcher>((_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(WebsitePageFetcher.TimeoutSeconds);
        })
        .RemoveAllLoggers()
        .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
        {
            var dns = serviceProvider.GetRequiredService<IDnsLookup>();
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectCallback = (context, cancellationToken) =>
                    SafeConnect.ConnectAsync(dns, context.DnsEndPoint, cancellationToken),
            };
        });

        return services;
    }
}
