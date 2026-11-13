using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.Api.Tests;

public sealed class BraveClientRegistrationTests
{
    [Fact]
    public void Brave_http_client_times_out_after_10_seconds()
    {
        using var factory = ReacherApiFactory
            .Create(ReacherApiFactory.UnreachableDatabase)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(
                SearchTestServices.RemoveIngestWorker));
        using var scope = factory.Services.CreateScope();
        using var client = scope.ServiceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(BraveSearchClient));

        Assert.Equal(TimeSpan.FromSeconds(BraveSearchClient.TimeoutSeconds), client.Timeout);
        Assert.Equal(10, BraveSearchClient.TimeoutSeconds);
    }
}
