using Microsoft.Extensions.DependencyInjection;
using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class PageFetcherHandlerTests
{
    [Fact]
    public void Page_fetcher_connects_directly_and_follows_no_redirects()
    {
        using var provider = new ServiceCollection()
            .AddWebsiteAnalysis()
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        HttpMessageHandler handler = factory.CreateHandler(nameof(WebsitePageFetcher));
        while (handler is DelegatingHandler delegating && delegating.InnerHandler is not null)
        {
            handler = delegating.InnerHandler;
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(sockets.UseProxy);
        Assert.False(sockets.AllowAutoRedirect);
    }
}
