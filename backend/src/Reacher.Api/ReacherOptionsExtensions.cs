using Reacher.Ingestion.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.Api;

/// <summary>Binds and validates every options section when the host starts.</summary>
public static class ReacherOptionsExtensions
{
    public static IServiceCollection AddReacherOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Bind<ScbOptions>(services, configuration, ScbOptions.SectionName);
        Bind<BolagsverketOptions>(services, configuration, BolagsverketOptions.SectionName);
        Bind<IngestOptions>(services, configuration, IngestOptions.SectionName);
        Bind<BraveOptions>(services, configuration, BraveOptions.SectionName);
        Bind<PageSpeedOptions>(services, configuration, PageSpeedOptions.SectionName);
        Bind<ReworkScoreOptions>(services, configuration, ReworkScoreOptions.SectionName);
        return services;
    }

    private static void Bind<TOptions>(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
