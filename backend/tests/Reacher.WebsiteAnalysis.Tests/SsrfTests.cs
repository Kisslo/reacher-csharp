using System.Net;
using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class SsrfTests
{
    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://100.64.0.1")]
    [InlineData("http://0.0.0.0")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:pass@203.0.113.10/")]
    [InlineData("http://203.0.113.10:8080/")]
    [InlineData("")]
    [InlineData("not a url")]
    public async Task Rejects_unsafe_urls_before_any_request(string url)
    {
        var dns = FakeDns.Example();
        using var harness = AnalysisHarness.Create(dns: dns);
        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            harness.Analyzer.AnalyzeAsync(url, CancellationToken.None));

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal("The URL is not allowed.", exception.Message);
        Assert.Equal(0, harness.Page.Calls);
        Assert.Equal(0, harness.PageSpeed.Calls);
        Assert.Empty(dns.Queries);
    }

    [Fact]
    public async Task Localhost_is_rejected_with_invalid_url()
    {
        var dns = FakeDns.Example();
        using var harness = AnalysisHarness.Create(dns: dns);
        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            harness.Analyzer.AnalyzeAsync("http://localhost", CancellationToken.None));

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal("The URL is not allowed.", exception.Message);
        Assert.Equal(0, harness.Page.Calls);
        Assert.Equal(0, harness.PageSpeed.Calls);
        Assert.Contains("localhost", dns.Queries, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_host_with_one_private_address_is_rejected()
    {
        var dns = new FakeDns(new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["example.test"] =
            [
                IPAddress.Parse("203.0.113.10"),
                IPAddress.Parse("10.0.0.1"),
            ],
        });
        using var harness = AnalysisHarness.Create(dns: dns);
        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            harness.Analyzer.AnalyzeAsync("https://example.test/", CancellationToken.None));

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal(0, harness.Page.Calls);
        Assert.Equal(0, harness.PageSpeed.Calls);
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_rejected()
    {
        using var harness = AnalysisHarness.Create(
            page: (request, _) =>
            {
                if (request.RequestUri?.Host == "10.0.0.1")
                {
                    return AnalysisHarness.Reject(request, CancellationToken.None);
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        Headers = { Location = new Uri("http://10.0.0.1/") },
                    });
            },
            pageSpeed: (_, _) => Task.FromResult(AnalysisHarness.Json("{}")));

        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            harness.Analyzer.AnalyzeAsync("https://example.test/", CancellationToken.None));

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal("The URL is not allowed.", exception.Message);
        Assert.Equal(1, harness.Page.Calls);
        Assert.DoesNotContain("10.0.0.1", harness.Page.Hosts);
    }

    [Fact]
    public async Task Connect_refuses_a_private_address_from_dns()
    {
        var dns = new FakeDns(new Dictionary<string, IPAddress[]>(StringComparer.Ordinal)
        {
            ["example.test"] = [IPAddress.Parse("10.0.0.1")],
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<AnalysisException>(() =>
            SafeConnect.ConnectAsync(
                dns,
                new DnsEndPoint("example.test", 443),
                timeout.Token).AsTask());

        Assert.Equal(AnalysisException.InvalidUrlCode, exception.Code);
        Assert.Equal("The URL is not allowed.", exception.Message);
    }

    [Fact]
    public async Task A_public_https_url_is_allowed()
    {
        var dns = FakeDns.Example();
        var uri = await new UrlSafety(dns).ValidateAsync(
            "https://example.test/path",
            CancellationToken.None);

        Assert.Equal("https://example.test/path", uri.AbsoluteUri);
        Assert.Contains("example.test", dns.Queries, StringComparer.OrdinalIgnoreCase);
    }
}
