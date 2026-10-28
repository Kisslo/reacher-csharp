using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Reacher.WebsiteAnalysis.Analysis;
using Reacher.WebsiteAnalysis.Options;

namespace Reacher.WebsiteAnalysis.Tests;

internal static class AnalysisClock
{
    public static readonly DateTimeOffset Instant =
        new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    public static TimeProvider Time { get; } = new FixedTimeProvider(Instant);
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

internal sealed class FakeDns : IDnsLookup
{
    private readonly Dictionary<string, IPAddress[]> _map;

    public FakeDns(Dictionary<string, IPAddress[]> map) => _map = map;

    public List<string> Queries { get; } = [];

    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Queries.Add(host);
        if (_map.TryGetValue(host, out var addresses))
        {
            return Task.FromResult(addresses);
        }

        return Task.FromResult(Array.Empty<IPAddress>());
    }

    public static FakeDns Example() => new(
        new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["localhost"] = [IPAddress.Loopback],
            ["example.test"] = [IPAddress.Parse("203.0.113.10")],
        });
}

internal sealed class CallbackHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

    public CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
    {
        _callback = callback;
    }

    public int Calls { get; private set; }

    public string? LastQuery { get; private set; }

    public List<string> Hosts { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastQuery = request.RequestUri?.Query;
        Hosts.Add(request.RequestUri?.Host ?? "");
        return _callback(request, cancellationToken);
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

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
        Messages.Add(formatter(state, exception));
        if (exception is not null)
        {
            Messages.Add(exception.ToString());
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

internal sealed class AnalysisHarness : IDisposable
{
    public const string ApiKey = "test-key-do-not-log";

    private readonly HttpClient _pageClient;

    private readonly HttpClient _pageSpeedClient;

    public AnalysisHarness(
        FakeDns dns,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> page,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> pageSpeed,
        TimeSpan? pageSpeedTimeout = null,
        ILogger<PageSpeedClient>? logger = null)
    {
        Dns = dns;
        Page = new CallbackHandler(page);
        PageSpeed = new CallbackHandler(pageSpeed);
        _pageClient = new HttpClient(Page)
        {
            Timeout = TimeSpan.FromSeconds(WebsitePageFetcher.TimeoutSeconds),
        };
        _pageSpeedClient = new HttpClient(PageSpeed)
        {
            Timeout = pageSpeedTimeout ?? TimeSpan.FromSeconds(5),
        };
        var client = new PageSpeedClient(
            _pageSpeedClient,
            OptionsFactory.Create(new PageSpeedOptions
            {
                ApiKey = ApiKey,
                Strategy = "mobile",
                TimeoutSeconds = 90,
            }),
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PageSpeedClient>.Instance);
        var safety = new UrlSafety(dns);
        Analyzer = new WebsiteAnalyzer(
            safety,
            new WebsitePageFetcher(_pageClient, safety, AnalysisClock.Time),
            client,
            new ReworkScoreCalculator(OptionsFactory.Create(new ReworkScoreOptions()), AnalysisClock.Time),
            AnalysisClock.Time);
    }

    public FakeDns Dns { get; }

    public CallbackHandler Page { get; }

    public CallbackHandler PageSpeed { get; }

    public WebsiteAnalyzer Analyzer { get; }

    public static AnalysisHarness Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? page = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? pageSpeed = null,
        FakeDns? dns = null,
        TimeSpan? pageSpeedTimeout = null,
        ILogger<PageSpeedClient>? logger = null) =>
        new(
            dns ?? FakeDns.Example(),
            page ?? Reject,
            pageSpeed ?? Reject,
            pageSpeedTimeout,
            logger);

    public void Dispose()
    {
        _pageClient.Dispose();
        _pageSpeedClient.Dispose();
    }

    public static Task<HttpResponseMessage> Reject(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(
            new InvalidOperationException("A blocked URL was requested."));

    public static Task<HttpResponseMessage> Hang(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    public static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        };

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    public static Dictionary<string, List<string>> ParseQuery(string? query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var text = query[0] == '?' ? query[1..] : query;
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(split < 0 ? part : part[..split]);
            var value = split < 0 ? "" : Uri.UnescapeDataString(part[(split + 1)..]);
            if (!result.TryGetValue(key, out var values))
            {
                values = [];
                result[key] = values;
            }

            values.Add(value);
        }

        return result;
    }
}

internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
