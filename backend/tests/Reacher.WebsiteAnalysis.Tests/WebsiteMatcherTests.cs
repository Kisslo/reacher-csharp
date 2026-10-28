using Reacher.WebsiteAnalysis.Brave;
using Reacher.WebsiteAnalysis.Matching;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

public sealed class WebsiteMatcherTests
{
    [Theory]
    [InlineData("Kvarnbyns Rörteknik AB", "Malmö", "\"Kvarnbyns Rörteknik\" Malmö")]
    [InlineData("Example Aktiebolag (publ)", "Stockholm", "\"Example\" Stockholm")]
    [InlineData("Exempel AKTIEBOLAG (PUBL)", null, "\"Exempel\"")]
    [InlineData("Bygg & Måleri AB", null, "\"Bygg & Måleri\"")]
    [InlineData("Bygg   &   Måleri AB", "  Göteborg  ", "\"Bygg & Måleri\" Göteborg")]
    [InlineData("ABBA AB", null, "\"ABBA\"")]
    [InlineData("Aktiebolaget Exempel AB", null, "\"Aktiebolaget Exempel\"")]
    public void Build_strips_the_legal_form_and_quotes_the_name(
        string name,
        string? city,
        string expected)
    {
        Assert.Equal(expected, WebsiteQuery.Build(name, city));
    }

    [Theory]
    [InlineData("AB")]
    [InlineData("(publ)")]
    [InlineData("  Aktiebolag  ")]
    public void Build_rejects_a_name_that_is_only_a_legal_form(string name)
    {
        Assert.Throws<ArgumentException>(() => WebsiteQuery.Build(name, "Malmö"));
    }

    [Theory]
    [InlineData("Kvarnbyns Rörteknik AB", "kvarnbynsror", 0.8, 1)]
    [InlineData("Bygg & Måleri AB", "byggmaleristockholm", 0.5, 0.799)]
    [InlineData("Bygg & Måleri AB", "by", 0, 0)]
    [InlineData("Rör AB", "ror", 1, 1)]
    [InlineData("Rör AB", "rör", 1, 1)]
    [InlineData("Ängsö AB", "angso", 1, 1)]
    public void Score_matches_the_ticket_examples(
        string name,
        string domain,
        double minimum,
        double maximum)
    {
        var score = WebsiteScorer.Score(name, domain, title: null, titleBonus: 0);
        Assert.InRange(score, minimum, maximum);
    }

    [Fact]
    public void Score_adds_the_title_bonus_when_the_full_name_is_in_the_title()
    {
        const string name = "Bygg & Måleri AB";
        const string domain = "byggmaleristockholm";

        var without = WebsiteScorer.Score(name, domain, title: null, titleBonus: 0.15);
        var with = WebsiteScorer.Score(
            name,
            domain,
            "Bygg & Måleri i Stockholm",
            titleBonus: 0.15);
        var unrelatedTitle = WebsiteScorer.Score(
            name,
            domain,
            "En annan firma",
            titleBonus: 0.15);

        Assert.Equal(0.15, with - without, precision: 5);
        Assert.Equal(without, unrelatedTitle);
        Assert.InRange(with, 0.8, 1);
    }

    [Fact]
    public void Score_does_not_let_the_title_rescue_a_domain_that_misses_the_first_word()
    {
        var score = WebsiteScorer.Score(
            "Bygg & Måleri AB",
            "by",
            "Bygg & Måleri",
            titleBonus: 0.15);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_clamps_the_title_bonus_at_one()
    {
        var score = WebsiteScorer.Score("Rör AB", "ror", "Rör", titleBonus: 0.15);

        Assert.Equal(1, score);
    }

    [Theory]
    [InlineData("www.kvarnbynsror.se", "kvarnbynsror")]
    [InlineData("shop.kvarnbynsror.se", "kvarnbynsror")]
    [InlineData("kvarnbynsror.se", "kvarnbynsror")]
    [InlineData("WWW.KvarnbynsRor.se", "kvarnbynsror")]
    public void ExtractLabel_uses_the_registrable_name(string host, string expected)
    {
        Assert.Equal(expected, WebsiteScorer.ExtractLabel(host));
    }

    [Fact]
    public void Match_preselects_a_shortened_company_domain()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [Result("https://kvarnbynsror.se/")]));

        Assert.Equal(WebsiteConfidence.High, candidate.Confidence);
        Assert.True(candidate.Preselected);
        Assert.Equal("https://kvarnbynsror.se/", candidate.Url.AbsoluteUri);
    }

    [Fact]
    public void Match_drops_directory_and_social_hosts_including_subdomains()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://www.allabolag.se/kvarnbyn"),
                Result("https://foretag.hitta.se/kvarnbyn"),
                Result("https://notallabolag.se/kvarnbyn"),
            ]));

        Assert.Equal("notallabolag.se", candidate.Url.Host);
        Assert.False(candidate.Preselected);
    }

    [Fact]
    public void Match_does_not_block_a_host_that_only_contains_a_blocked_name()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [Result("https://allabolag.se.example.test/kvarnbyn")]));

        Assert.Equal("allabolag.se.example.test", candidate.Url.Host);
    }

    [Fact]
    public void Match_scores_an_extra_city_in_the_domain_below_high()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Bygg & Måleri AB",
            [Result("https://byggmaleristockholm.se/")]));

        Assert.NotEqual(WebsiteConfidence.High, candidate.Confidence);
        Assert.Equal(WebsiteConfidence.Medium, candidate.Confidence);
        Assert.False(candidate.Preselected);
    }

    [Fact]
    public void Match_preselects_when_the_title_contains_the_full_name()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Bygg & Måleri AB",
            [Result("https://byggmaleristockholm.se/", "Bygg & Måleri i Stockholm")]));

        Assert.Equal(WebsiteConfidence.High, candidate.Confidence);
        Assert.True(candidate.Preselected);
    }

    [Fact]
    public void Match_returns_empty_when_every_result_is_blocked()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://allabolag.se/x"),
                Result("https://facebook.com/x"),
                Result("https://www.reco.se/x"),
            ]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Match_preselects_only_the_best_high_candidate()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://kvarnbynsror.se/"),
                Result("https://kvarnbynsrorteknik.se/"),
            ]);

        Assert.Equal(WebsiteConfidence.High, candidates[0].Confidence);
        Assert.False(candidates[0].Preselected);
        Assert.Equal(WebsiteConfidence.High, candidates[1].Confidence);
        Assert.True(candidates[1].Preselected);
    }

    [Fact]
    public void Match_keeps_brave_order_when_high_scores_tie()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://kvarnbynsrorteknik.se/a"),
                Result("https://kvarnbynsrorteknik.se/b"),
            ]);

        Assert.True(candidates[0].Preselected);
        Assert.False(candidates[1].Preselected);
    }

    [Fact]
    public void Match_scores_www_and_a_subdomain_as_the_same_registrable_name()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://www.kvarnbynsror.se/"),
                Result("https://shop.kvarnbynsror.se/"),
            ]);

        Assert.Equal(2, candidates.Count);
        Assert.All(
            candidates,
            candidate => Assert.Equal(WebsiteConfidence.High, candidate.Confidence));
        Assert.True(candidates[0].Preselected);
        Assert.False(candidates[1].Preselected);
    }

    [Fact]
    public void Match_scores_at_most_three_results()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                Result("https://allabolag.se/1"),
                Result("https://hitta.se/2"),
                Result("https://eniro.se/3"),
                Result("https://kvarnbynsrorteknik.se/"),
            ]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Match_drops_a_result_without_a_host()
    {
        var candidates = CreateMatcher().Match(
            "Kvarnbyns Rörteknik AB",
            [
                new BraveSearchResult(
                    new Uri("/relative", UriKind.Relative),
                    "Kvarnbyns Rörteknik",
                    null),
            ]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Match_folds_swedish_characters_so_ror_can_score()
    {
        var candidate = Assert.Single(CreateMatcher().Match(
            "Rör AB",
            [Result("https://ror.se/")]));

        Assert.Equal(WebsiteConfidence.High, candidate.Confidence);
        Assert.True(candidate.Preselected);
    }

    private static WebsiteMatcher CreateMatcher()
    {
        return new WebsiteMatcher(Microsoft.Extensions.Options.Options.Create(new MatchingOptions
        {
            BlockedDomains =
            [
                "allabolag.se",
                "hitta.se",
                "eniro.se",
                "merinfo.se",
                "ratsit.se",
                "proff.se",
                "bolagsfakta.se",
                "upplysning.se",
                "facebook.com",
                "instagram.com",
                "linkedin.com",
                "bokadirekt.se",
                "google.com",
                "reco.se",
                "yelp.com",
            ],
        }));
    }

    private static BraveSearchResult Result(string url, string? title = null) =>
        new(new Uri(url), title, null);
}
