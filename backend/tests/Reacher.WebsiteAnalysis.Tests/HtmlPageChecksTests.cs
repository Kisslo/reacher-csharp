using Reacher.WebsiteAnalysis.Analysis;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class HtmlPageChecksTests
{
    private static readonly Uri Page = new("https://example.test/");

    [Fact]
    public void Viewport_meta_is_detected_ignoring_case()
    {
        var findings = Read("viewport.html");

        Assert.True(findings.Viewport);
        Assert.True(findings.Https);
    }

    [Fact]
    public void Missing_viewport_meta_is_false()
    {
        Assert.False(Read("no-viewport.html").Viewport);
    }

    [Fact]
    public void Generator_content_is_read()
    {
        Assert.Equal("ExampleCMS 1", Read("generator.html").Generator);
    }

    [Fact]
    public void Blank_generator_content_is_null()
    {
        Assert.Null(Read("generator-blank.html").Generator);
    }

    [Fact]
    public void Copyright_year_comes_from_the_footer_when_one_exists()
    {
        Assert.Equal(2019, Read("copyright-footer.html").CopyrightYear);
    }

    [Fact]
    public void Copyright_year_is_the_highest_year_in_the_body()
    {
        Assert.Equal(2024, Read("copyright-body.html").CopyrightYear);
    }

    [Theory]
    [InlineData("© 1990", null)]
    [InlineData("© 2099", null)]
    [InlineData("copyrights 2020", null)]
    [InlineData("copyright 2024", 2024)]
    public void Copyright_year_ignores_years_outside_the_window_and_range(string text, int? year)
    {
        var html = "<body><p>" + text + "</p></body>";
        var findings = HtmlPageChecks.Read(html, Page, AnalysisClock.Time);

        Assert.Equal(year, findings.CopyrightYear);
    }

    [Fact]
    public void A_year_far_from_the_copyright_mark_is_ignored()
    {
        var html = "<body><p>copyright" + new string(' ', 50) + "2024</p></body>";
        var findings = HtmlPageChecks.Read(html, Page, AnalysisClock.Time);

        Assert.Null(findings.CopyrightYear);
    }

    [Fact]
    public void Http_final_url_is_not_https()
    {
        var findings = HtmlPageChecks.Read("<body></body>", new Uri("http://example.test/"), AnalysisClock.Time);

        Assert.False(findings.Https);
    }

    private static HtmlFindings Read(string fixture) =>
        HtmlPageChecks.Read(Fixture.Read(fixture), Page, AnalysisClock.Time);
}
