using Microsoft.Extensions.Options;
using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.Api;

public static class BraveServiceCollectionExtensions
{
    public static IServiceCollection AddBraveSearch(
        this IServiceCollection services)
    {
        services.AddHttpClient<BraveSearchClient>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<BraveOptions>>()
                .Value;

            httpClient.BaseAddress = new Uri(options.BaseAddress);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IBraveRequestRateLimiter, BraveRequestRateLimiter>();
        services.AddScoped<IBraveUsageCounter, BraveUsageCounter>();
        services.AddSingleton<FakeBraveSearchClient>();
        services.AddTransient<IBraveSearchClient>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<BraveOptions>>()
                .Value;

            return options.UseFake
                ? serviceProvider.GetRequiredService<FakeBraveSearchClient>()
                : serviceProvider.GetRequiredService<BraveSearchClient>();
        });

        return services;
    }
}
