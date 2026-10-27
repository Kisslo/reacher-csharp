using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reacher.Ingestion.Options;
using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Tests.Scb;

/// <summary>
/// Builds the client exactly as the API does (<see cref="ScbServiceCollectionExtensions"/>,
/// so with the real retry pipeline), but on top of a <see cref="FakeHttpHandler"/>.
/// </summary>
internal sealed class ScbTestHost : IDisposable
{
    /// <summary>Not a real key.</summary>
    public const string ApiKey = "test-only-scb-key";

    /// <summary>12:00 Swedish time, outside SCB's nightly window.</summary>
    public static readonly DateTimeOffset Noon = new(2026, 11, 3, 11, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    public ScbTestHost(
        FakeHttpHandler handler,
        DateTimeOffset? now = null,
        ILoggerProvider? logs = null)
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
        services.AddOptions<ScbOptions>().Configure(options =>
        {
            options.BaseUrl = "https://scb.test/v1";
            options.ApiKey = ApiKey;
            options.PageSize = 2;
        });
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now ?? Noon));
        services.AddScbClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        _provider = services.BuildServiceProvider();
    }

    public IScbClient Client => _provider.GetRequiredService<IScbClient>();

    public void Dispose() => _provider.Dispose();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>Keeps every log line, so a test can prove what never reaches the log.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Lines { get; } = [];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
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
            lock (owner.Lines)
            {
                owner.Lines.Add($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
