using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Options;

namespace Reacher.Ingestion.Tests.Bolagsverket;

/// <summary>
/// Builds the client exactly as the API does
/// (<see cref="BolagsverketServiceCollectionExtensions"/>, so with the real retry pipeline),
/// but on top of a <see cref="FakeBolagsverket"/>.
/// </summary>
internal sealed class BolagsverketTestHost : IDisposable
{
    /// <summary>Not real credentials. They look like keys, so a leak shows in the assert.</summary>
    public const string ClientId = "test-only-client-id";

    public const string ClientSecret = "test-only-client-secret";

    /// <summary>Fictional, the same company as in arsredovisning_fiktiv.xhtml.</summary>
    public const string OrgNr = "5599990016";

    public const string TokenPath = "/oauth2/token";

    public const string ApiPath = "/vardefulla-datamangder/v1";

    private readonly ServiceProvider _provider;

    public BolagsverketTestHost(FakeBolagsverket api, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddOptions<BolagsverketOptions>().Configure(options =>
        {
            options.TokenUrl = "https://portal.bolagsverket.test" + TokenPath;
            options.ApiUrl = "https://gw.bolagsverket.test" + ApiPath;
            options.ClientId = ClientId;
            options.ClientSecret = ClientSecret;
        });
        services.AddSingleton<TimeProvider>(api.Clock);
        services.AddBolagsverketClient();
        // Both the token client and the API client go to the fake.
        services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(() => api));
        _provider = services.BuildServiceProvider();
    }

    public IBolagsverketClient Client => _provider.GetRequiredService<IBolagsverketClient>();

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Stands in for Bolagsverket (hard rule 8). Answers requests in order and records what
/// was sent. A request with no answer left fails the test.
/// </summary>
internal sealed class FakeBolagsverket(params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    : HttpMessageHandler
{
    public static readonly DateTimeOffset Start = new(2026, 11, 3, 11, 0, 0, TimeSpan.Zero);

    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new(answers);

    /// <summary>
    /// Each request takes one second of fake time, so the one-request-a-second limit never
    /// makes a test wait for real.
    /// </summary>
    public ManualTimeProvider Clock { get; } = new(Start);

    public List<SentRequest> Requests { get; } = [];

    public List<SentRequest> TokenRequests =>
        Requests.Where(request => request.Path == BolagsverketTestHost.TokenPath).ToList();

    public List<SentRequest> ApiRequests =>
        Requests.Where(request => request.Path != BolagsverketTestHost.TokenPath).ToList();

    public static HttpResponseMessage Token(string token, int expiresIn = 3600) =>
        Json($$"""
            { "access_token": "{{token}}", "token_type": "Bearer", "expires_in": {{expiresIn}} }
            """);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    /// <summary>A status with <c>Retry-After: 0</c>, so the retry does not wait.</summary>
    public static HttpResponseMessage RetryNow(HttpStatusCode status) =>
        new(status) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) } };

    public static HttpResponseMessage Zip(params (string Name, string Content)[] files) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ZipBytes(files))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/zip") },
            },
        };

    public static byte[] ZipBytes(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            body));
        Clock.Advance(TimeSpan.FromSeconds(1));

        if (_answers.Count == 0)
        {
            throw new InvalidOperationException($"Unexpected request {Requests.Count}.");
        }

        return _answers.Dequeue()(request);
    }
}

internal sealed record SentRequest(string Method, string Path, string? Authorization, string? Body);

/// <summary>A clock that only moves when a test moves it.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
