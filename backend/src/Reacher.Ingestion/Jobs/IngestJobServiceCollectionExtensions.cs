using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Reacher.Ingestion.Jobs;

public static class IngestJobServiceCollectionExtensions
{
    /// <summary>
    /// Registers the queue, the coordinator, the pipeline and the background worker. Needs
    /// <c>ScbIngestService</c>, <c>FinancialsIngestService</c> and the DbContext to be
    /// registered (Program.cs in Reacher.Api).
    /// </summary>
    public static IServiceCollection AddIngestJobs(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IIngestQueue, ChannelIngestQueue>();
        services.AddScoped<IIngestCoordinator, IngestCoordinator>();
        services.AddScoped<IIngestPipeline, IngestPipeline>();
        services.AddHostedService<IngestWorker>();
        return services;
    }
}
