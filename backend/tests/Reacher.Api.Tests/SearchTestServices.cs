using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reacher.Api.Searches;
using Reacher.Ingestion.Jobs;

namespace Reacher.Api.Tests;

internal static class SearchTestServices
{
    public static void RemoveIngestWorker(IServiceCollection services)
    {
        foreach (var worker in services
            .Where(descriptor => descriptor.ImplementationType == typeof(IngestWorker))
            .ToList())
        {
            services.Remove(worker);
        }
    }

    public static void UseTestCodes(IServiceCollection services)
    {
        services.RemoveAll<ICodeLookup>();
        services.AddSingleton<ICodeLookup>(TestCodeLookup.Instance);
    }
}
