using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Reacher.Api.Websites;
using Reacher.Data;
using Reacher.Data.Entities;
using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Brave;

namespace Reacher.Api.Tests;

public sealed class WebsiteEndpointTests(CompanySearchFixture postgres)
    : IClassFixture<CompanySearchFixture>
{
    private const string HealthyPage = """
        <!DOCTYPE html>
        <html><head>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        </head><body><footer>© 2026 Example</footer></body></html>
        """;

    private const string LighthouseJson = """
        {
          "lighthouseResult": {
            "categories": {
              "performance": { "score": 0.4 },
              "accessibility": { "score": 0.5 },
              "best-practices": { "score": 0.6 },
              "seo": { "score": 0.7 }
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Candidates_returns_the_fake_result_without_storing_it()
    {
        await using var host = CreateHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/api/companies/5599001001/website-candidates",
            content: null);

        response.EnsureSuccessStatusCode();
        AssertNoStore(response);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(1500, body["remainingMonthlyQuota"]!.GetValue<int>());
        var candidate = Assert.Single(body["candidates"]!.AsArray());
        Assert.Equal("https://example.test/company", candidate!["url"]!.GetValue<string>());
        Assert.Equal("\"Alpha\" Teststad", candidate["title"]!.GetValue<string>());
        Assert.Equal("low", candidate["confidence"]!.GetValue<string>());
        Assert.False(candidate["preselected"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("5599001999")]
    [InlineData("190001011111")]
    public async Task Candidates_returns_404_when_the_company_cannot_be_shown(string orgnr)
    {
        await using var host = CreateHost();
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{orgnr}/website-candidates",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("company_not_found", body["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Candidates_reports_the_stored_monthly_quota()
    {
        var month = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await using var host = CreateHost();
        await using (var scope = host.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();
            db.BraveUsage.Add(new BraveUsage { Month = month, RequestCount = 10 });
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = host.CreateClient();
            var response = await client.PostAsync(
                "/api/companies/5599001001/website-candidates",
                content: null);

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions);
            Assert.NotNull(body);
            Assert.Equal(1490, body["remainingMonthlyQuota"]!.GetValue<int>());
        }
        finally
        {
            await using var scope = host.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ReacherDbContext>();
            var row = await db.BraveUsage.FindAsync(month);
            if (row is not null)
            {
                db.BraveUsage.Remove(row);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Candidates_returns_429_when_the_quota_is_exceeded()
    {
        await using var host = CreateHost(brave: new ThrowingBraveClient(new BraveQuotaExceededException()));
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/api/companies/5599001001/website-candidates",
            content: null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("brave_quota_exceeded", body["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Candidates_does_not_echo_a_brave_failure()
    {
        const string secret = "https://sentinel.example.test/do-not-log";
        await using var host = CreateHost(brave: new ThrowingBraveClient(new BraveSearchException(secret)));
        using var client = host.CreateClient();

        var response = await client.PostAsync(
            "/api/companies/5599001001/website-candidates",
            content: null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("brave_unavailable", body["code"]!.GetValue<string>());
        var text = body.ToJsonString();
        Assert.DoesNotContain("sentinel.example.test", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analysis_returns_the_rework_score_from_the_fakes()
    {
        await using var host = CreateHost(
            page: (_, _) => Task.FromResult(Html(HealthyPage)),
            pageSpeed: (_, _) => Task.FromResult(Json(LighthouseJson)));
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("https://example.test/"));

        response.EnsureSuccessStatusCode();
        AssertNoStore(response);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("scoreFromChecksOnly", text, StringComparison.OrdinalIgnoreCase);
        var body = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("https://example.test/", body["url"]!.GetValue<string>());
        Assert.Equal(48, body["reworkScore"]!.GetValue<int>());
        Assert.Equal(40, body["lighthouse"]!["performance"]!.GetValue<int>());
        Assert.Equal(50, body["lighthouse"]!["accessibility"]!.GetValue<int>());
        Assert.Equal(60, body["lighthouse"]!["bestPractices"]!.GetValue<int>());
        Assert.Equal(70, body["lighthouse"]!["seo"]!.GetValue<int>());
        Assert.True(body["checks"]!["https"]!.GetValue<bool>());
        Assert.True(body["checks"]!["viewport"]!.GetValue<bool>());
        Assert.Equal(2026, body["checks"]!["copyrightYear"]!.GetValue<int>());
        Assert.Equal(1, host.Page.Calls);
        Assert.Equal(1, host.PageSpeed.Calls);
    }

    [Fact]
    public async Task Analysis_rejects_a_loopback_url_without_fetching()
    {
        await using var host = CreateHost();
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("http://127.0.0.1/"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("invalid_url", body["code"]!.GetValue<string>());
        Assert.DoesNotContain(
            "127.0.0.1",
            body.ToJsonString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, host.Page.Calls);
        Assert.Equal(0, host.PageSpeed.Calls);
    }

    [Fact]
    public async Task Analysis_returns_504_when_the_fetch_is_cancelled()
    {
        await using var host = CreateHost(
            page: (_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException()));
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("https://example.test/"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("analysis_timeout", body["code"]!.GetValue<string>());
        Assert.DoesNotContain("example.test", body.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analysis_returns_502_when_the_fetch_fails()
    {
        await using var host = CreateHost(
            page: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("https://example.test/"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await ReadProblem(response);
        Assert.Equal("analysis_fetch_failed", body["code"]!.GetValue<string>());
        Assert.DoesNotContain("example.test", body.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analysis_returns_checks_when_pagespeed_fails()
    {
        await using var host = CreateHost(
            page: (_, _) => Task.FromResult(Html(HealthyPage)),
            pageSpeed: (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        using var client = host.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("https://example.test/"));

        response.EnsureSuccessStatusCode();
        AssertNoStore(response);
        var body = (await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions))!;
        Assert.True(body.TryGetPropertyValue("lighthouse", out var lighthouse));
        Assert.Null(lighthouse);
        Assert.True(body["checks"]!["https"]!.GetValue<bool>());
        Assert.Equal(0, body["reworkScore"]!.GetValue<int>());
    }

    [Fact]
    public async Task Website_responses_are_not_logged()
    {
        var logs = new RecordingLoggerProvider();
        await using var host = CreateHost(
            page: (_, _) => Task.FromResult(Html(HealthyPage)),
            pageSpeed: (_, _) => Task.FromResult(Json(LighthouseJson)),
            logs: logs);
        using var client = host.CreateClient();

        var candidates = await client.PostAsync(
            "/api/companies/5599001001/website-candidates",
            content: null);
        var analysis = await client.PostAsJsonAsync(
            "/api/website-analysis",
            new WebsiteAnalysisRequest("https://example.test/path"));

        candidates.EnsureSuccessStatusCode();
        analysis.EnsureSuccessStatusCode();
        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(
            logs.Messages,
            message => message.Contains("example.test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Openapi_contains_the_website_endpoints()
    {
        await using var host = CreateHost();
        using var client = host.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions);
        Assert.NotNull(document);
        Assert.NotNull(document["paths"]?["/api/companies/{orgnr}/website-candidates"]?["post"]);
        Assert.NotNull(document["paths"]?["/api/website-analysis"]?["post"]);
        var schemas = document["components"]!["schemas"]!.AsObject();
        Assert.NotNull(schemas["WebsiteCandidatesResponse"]);
        Assert.NotNull(schemas["WebsiteAnalysisResponse"]);

        var confidence = ConfidenceSchema(schemas);
        var names = confidence["enum"]!
            .AsArray()
            .Select(item => item!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["high", "low", "medium"], names);
    }

    private static JsonObject ConfidenceSchema(JsonObject schemas)
    {
        var candidate = schemas["WebsiteCandidateResponse"]?.AsObject()
            ?? throw new InvalidOperationException("WebsiteCandidateResponse is missing from OpenAPI.");
        var confidence = candidate["properties"]?["confidence"]?.AsObject()
            ?? throw new InvalidOperationException("confidence is missing from OpenAPI.");
        if (confidence["$ref"]?.GetValue<string>() is string reference)
        {
            var name = reference.Split('/')[^1];
            return schemas[name]?.AsObject()
                ?? throw new InvalidOperationException($"Schema '{name}' is missing from OpenAPI.");
        }

        return confidence;
    }

    private static async Task<JsonObject> ReadProblem(HttpResponseMessage response)
    {
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        AssertNoStore(response);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(JsonOptions);
        Assert.NotNull(body);
        return body;
    }

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html"),
        };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

    private static Task<HttpResponseMessage> Reject(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(
            new InvalidOperationException("An external request was made."));

    private ApiHost CreateHost(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? page = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? pageSpeed = null,
        IBraveSearchClient? brave = null,
        RecordingLoggerProvider? logs = null)
    {
        var pageHandler = new ScriptedHandler(page ?? Reject);
        var pageSpeedHandler = new ScriptedHandler(pageSpeed ?? Reject);
        var factory = ReacherApiFactory.Create(postgres.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Brave:UseFake", "true");
            builder.UseSetting("Brave:MonthlyCap", "1500");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDnsLookup>(new ExampleDns());
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
                    new HandlerReplacement(pageHandler, pageSpeedHandler));
                if (brave is not null)
                {
                    services.AddSingleton<IBraveSearchClient>(brave);
                }
            });

            if (logs is not null)
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(logs);
                });
            }
        });

        return new ApiHost(factory, pageHandler, pageSpeedHandler);
    }

    private sealed class ApiHost(
        WebApplicationFactory<Program> factory,
        ScriptedHandler page,
        ScriptedHandler pageSpeed) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public ScriptedHandler Page { get; } = page;

        public ScriptedHandler PageSpeed { get; } = pageSpeed;

        public HttpClient CreateClient() => Factory.CreateClient();

        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private sealed class ExampleDns : IDnsLookup
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            if (host.Equals("example.test", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IPAddress[]>([IPAddress.Parse("203.0.113.10")]);
            }

            return Task.FromResult<IPAddress[]>([]);
        }
    }

    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    /// <summary>
    /// The production clients already set a primary handler, and a second
    /// <c>ConfigurePrimaryHttpMessageHandler</c> throws. Replace the handler
    /// when the pipeline is built so tests never open a socket.
    /// </summary>
    private sealed class HandlerReplacement(HttpMessageHandler page, HttpMessageHandler pageSpeed)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                var replaced = builder.Name switch
                {
                    nameof(WebsitePageFetcher) => page,
                    nameof(PageSpeedClient) => pageSpeed,
                    _ => null,
                };
                if (replaced is null)
                {
                    return;
                }

                var previous = builder.PrimaryHandler;
                builder.PrimaryHandler = replaced;
                if (previous is not null && !ReferenceEquals(previous, replaced))
                {
                    previous.Dispose();
                }
            };
    }

    private sealed class ThrowingBraveClient(Exception exception) : IBraveSearchClient
    {
        public Task<IReadOnlyList<BraveSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<BraveSearchResult>>(exception);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly RecordingLogger logger = new();

        public IReadOnlyList<string> Messages => logger.Messages;

        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly Lock gate = new();
            private readonly List<string> messages = [];

            public IReadOnlyList<string> Messages
            {
                get
                {
                    lock (gate)
                    {
                        return messages.ToArray();
                    }
                }
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => EmptyScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                lock (gate)
                {
                    messages.Add(message);
                    if (exception is not null)
                    {
                        messages.Add(exception.ToString());
                    }
                }
            }

            private sealed class EmptyScope : IDisposable
            {
                public static readonly EmptyScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
