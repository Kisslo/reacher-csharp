using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class BraveSearchResultTests
{
    [Fact]
    public void Stores_the_values_needed_by_matching()
    {
        var result = new BraveSearchResult(
            new Uri("https://example.test/company"),
            "Example",
            "A made-up test result.");

        Assert.Equal("https://example.test/company", result.Url.AbsoluteUri);
        Assert.Equal("Example", result.Title);
        Assert.Equal("A made-up test result.", result.Description);
    }
}
