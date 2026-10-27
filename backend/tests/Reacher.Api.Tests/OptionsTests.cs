using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Reacher.Api;
using Reacher.Ingestion.Options;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.Api.Tests;

public sealed class OptionsTests
{
    [Fact]
    public void Options_bind_from_in_memory_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scb:ApiKey"] = "test-only",
                ["Scb:PageSize"] = "100",
                ["Bolagsverket:ClientId"] = "test-only",
                ["Bolagsverket:ClientSecret"] = "test-only",
                ["Bolagsverket:TagMap:0:Tag"] = "se-gen-base:Nettoomsattning",
                ["Bolagsverket:TagMap:0:Fact"] = "revenue",
                ["Ingest:AllowedLegalForms:0"] = "49",
                ["Brave:ApiKey"] = "test-only",
                ["Brave:UseFake"] = "true",
                ["PageSpeed:ApiKey"] = "test-only",
                ["ReworkScore:PerformanceWeight"] = "0.55",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddReacherOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var scb = provider.GetRequiredService<IOptions<ScbOptions>>().Value;
        var bolagsverket = provider.GetRequiredService<IOptions<BolagsverketOptions>>().Value;
        var ingest = provider.GetRequiredService<IOptions<IngestOptions>>().Value;
        var brave = provider.GetRequiredService<IOptions<BraveOptions>>().Value;
        var rework = provider.GetRequiredService<IOptions<ReworkScoreOptions>>().Value;

        Assert.Equal(100, scb.PageSize);
        var tag = Assert.Single(bolagsverket.TagMap);
        var tagName = tag.Tag;
        Assert.NotNull(tagName);
        Assert.Contains(':', tagName);
        Assert.Equal("se-gen-base:Nettoomsattning", tagName);
        Assert.Equal("revenue", tag.Fact);
        Assert.Equal("49", Assert.Single(ingest.AllowedLegalForms));
        Assert.True(brave.UseFake);
        Assert.Equal(0.55, rework.PerformanceWeight);
    }

    [Theory]
    [InlineData("Scb:ApiKey")]
    [InlineData("Bolagsverket:ClientId")]
    [InlineData("Bolagsverket:ClientSecret")]
    [InlineData("Brave:ApiKey")]
    [InlineData("PageSpeed:ApiKey")]
    public void Startup_fails_when_a_required_key_is_missing(string setting)
    {
        using var factory = ReacherApiFactory.Create(
            ReacherApiFactory.UnreachableDatabase,
            setting);

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        var text = exception.ToString();
        Assert.Contains($"Missing setting '{setting}'.", text);
        Assert.DoesNotContain(ReacherApiFactory.Sentinel, text);
    }

    [Fact]
    public void Startup_fails_when_connection_string_is_blank()
    {
        using var factory = ReacherApiFactory.Create("");

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        Assert.Contains(
            "Missing setting 'ConnectionStrings:Default'.",
            exception.ToString());
    }
}
