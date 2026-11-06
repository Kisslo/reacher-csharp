using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Reacher.Api.Tests;

/// <summary>Tests that need no database, so they run without Docker.</summary>
public sealed class AccessGateTests
{
    /// <summary>Made up for these tests. Not a real credential.</summary>
    private const string Password = "test-only-password-that-is-long-enough";

    [Theory]
    [InlineData("/api/companies")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/health/extra")]
    [InlineData("/")]
    public async Task Request_without_password_gets_401_problem(string path)
    {
        using var factory = CreateGatedFactory(Password);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Basic", challenge.Scheme);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthenticated", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("Basic", "anyone:wrong-password")]
    [InlineData("Basic", "anyone:")]
    [InlineData("Basic", "no-colon-at-all")]
    [InlineData("Bearer", "anyone:" + Password)]
    public async Task Wrong_credentials_get_401(string scheme, string credentials)
    {
        using var factory = CreateGatedFactory(Password);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(scheme, Encode(credentials));

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_base64_gets_401()
    {
        using var factory = CreateGatedFactory(Password);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", "not-base64!");

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("anyone", Password)]
    [InlineData("", Password)]
    [InlineData("anyone", "test:only:password:that:has:colons:in:it")]
    public async Task Right_password_passes_with_any_username(string username, string password)
    {
        using var factory = CreateGatedFactory(password);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Encode($"{username}:{password}"));

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_open_without_password()
    {
        using var factory = CreateGatedFactory(Password);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        // 503, not 401: the request got past the gate to the unreachable database.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Missing setting 'AccessGate:Password'.")]
    [InlineData("   ", "Missing setting 'AccessGate:Password'.")]
    [InlineData(ReacherApiFactory.Sentinel, "must be at least 32 characters")]
    public void Startup_fails_when_gate_is_on_and_password_is_invalid(
        string password,
        string expectedMessage)
    {
        using var factory = CreateGatedFactory(password);

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        var text = exception.ToString();
        Assert.Contains(expectedMessage, text);
        Assert.DoesNotContain(ReacherApiFactory.Sentinel, text);
    }

    [Fact]
    public void Startup_fails_with_a_password_one_character_too_short()
    {
        using var factory = CreateGatedFactory(new string('x', 31));

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        Assert.Contains("must be at least 32 characters", exception.ToString());
    }

    [Fact]
    public async Task Startup_succeeds_with_a_32_character_password()
    {
        using var factory = CreateGatedFactory(new string('x', 32));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Gate_is_on_by_default_outside_development()
    {
        using var baseFactory = ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase);
        using var factory = baseFactory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));

        var exception = Record.Exception(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.NotNull(exception);
        Assert.Contains("Missing setting 'AccessGate:Password'.", exception.ToString());
    }

    [Fact]
    public async Task Password_is_never_logged()
    {
        var logs = new RecordingLoggerProvider();
        using var baseFactory = CreateGatedFactory(Password);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            // The most verbose level, so anything the framework could log is captured.
            builder.UseSetting("Logging:LogLevel:Default", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        using var client = factory.CreateClient();

        using var withoutPassword = await client.GetAsync("/openapi/v1.json");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Encode($"anyone:{Password}"));
        using var withPassword = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, withPassword.StatusCode);
        Assert.NotEmpty(logs.Messages);
        Assert.All(logs.Messages, message =>
        {
            Assert.DoesNotContain(Password, message);
            Assert.DoesNotContain(Encode($"anyone:{Password}"), message);
        });
    }

    private static WebApplicationFactory<Program> CreateGatedFactory(string password) =>
        ReacherApiFactory.Create(ReacherApiFactory.UnreachableDatabase).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AccessGate:Enabled", "true");
            builder.UseSetting("AccessGate:Password", password);
        });

    private static string Encode(string credentials) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> messages = new();

        public IReadOnlyCollection<string> Messages => messages;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue($"{formatter(state, exception)} {exception}");
        }

        public void Dispose()
        {
        }
    }
}
