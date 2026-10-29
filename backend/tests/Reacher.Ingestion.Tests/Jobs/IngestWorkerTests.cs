using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Reacher.Data.Entities;
using Reacher.Ingestion.Bolagsverket;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Scb;
using Reacher.Ingestion.Tests.Scb;
using static Reacher.Ingestion.Tests.Scb.ScbTestData;

namespace Reacher.Ingestion.Tests.Jobs;

public sealed class IngestWorkerTests(IngestPostgresFixture postgres)
    : IClassFixture<IngestPostgresFixture>, IAsyncLifetime
{
    private readonly FakePipeline _pipeline = new();

    private readonly ChannelIngestQueue _queue = new();

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_job_goes_from_queued_to_running_to_succeeded()
    {
        var release = new TaskCompletionSource();
        _pipeline.Run = async (progress, cancellationToken) =>
        {
            progress.Report(new IngestProgress(IngestPhases.Companies, 1, 3));
            await release.Task.WaitAsync(cancellationToken);
            progress.Report(new IngestProgress(IngestPhases.Financials, 3, 3));
        };
        var jobId = await AddJobAsync(Stockholm, Solna);

        await using var worker = await StartWorkerAsync();
        await _queue.EnqueueAsync(jobId, CancellationToken.None);

        var running = await WaitForAsync(jobId, IngestJobStatus.Running);
        Assert.NotNull(running.StartedAt);
        Assert.Null(running.FinishedAt);

        release.SetResult();
        var succeeded = await WaitForAsync(jobId, IngestJobStatus.Succeeded);
        Assert.NotNull(succeeded.FinishedAt);
        Assert.Null(succeeded.Error);
        // The last report is written when the job finishes, however fast it was.
        Assert.Equal(IngestPhases.Financials, succeeded.Phase);
        Assert.Equal(3, succeeded.Done);
        Assert.Equal(3, succeeded.Total);
        Assert.Equal([$"{Sni}: {Stockholm}, {Solna}"], _pipeline.Calls);
    }

    [Fact]
    public async Task An_exception_fails_the_job_and_the_worker_runs_the_next_one()
    {
        var calls = 0;
        _pipeline.Run = (_, _) => ++calls == 1
            // A message that quotes data must never reach ingest_job.error (hard rule 6).
            ? throw new InvalidOperationException("Key (orgnr)=(5599001236) already exists.")
            : Task.CompletedTask;
        var first = await AddJobAsync(Stockholm);
        var second = await AddJobAsync(Solna);

        await using var worker = await StartWorkerAsync();
        await _queue.EnqueueAsync(first, CancellationToken.None);
        await _queue.EnqueueAsync(second, CancellationToken.None);

        var failed = await WaitForAsync(first, IngestJobStatus.Failed);
        Assert.Equal("Unexpected error (InvalidOperationException).", failed.Error);
        Assert.NotNull(failed.FinishedAt);
        await WaitForAsync(second, IngestJobStatus.Succeeded);
    }

    [Fact]
    public async Task A_failed_job_keeps_our_own_message_and_where_it_stopped()
    {
        // As in a real run with wrong credentials: SCB is done, financials fail at once.
        _pipeline.Run = (progress, _) =>
        {
            progress.Report(new IngestProgress(IngestPhases.Financials, 0, 51));
            throw new BolagsverketAuthException("POST /oauth2/token: HTTP 401.");
        };
        var jobId = await AddJobAsync(Stockholm);

        await using var worker = await StartWorkerAsync();
        await _queue.EnqueueAsync(jobId, CancellationToken.None);

        var failed = await WaitForAsync(jobId, IngestJobStatus.Failed);
        Assert.Equal("POST /oauth2/token: HTTP 401.", failed.Error);
        Assert.Equal(IngestPhases.Financials, failed.Phase);
        Assert.Equal(0, failed.Done);
        Assert.Equal(51, failed.Total);
    }

    [Fact]
    public async Task At_startup_a_running_job_is_failed_and_a_queued_job_is_run()
    {
        var interrupted = await AddJobAsync(Stockholm, status: IngestJobStatus.Running);
        var waiting = await AddJobAsync(Solna);

        // Nothing is enqueued: the worker finds both jobs in the database.
        await using var worker = await StartWorkerAsync();

        var failed = await WaitForAsync(interrupted, IngestJobStatus.Failed);
        Assert.Equal(IngestWorker.InterruptedError, failed.Error);
        await WaitForAsync(waiting, IngestJobStatus.Succeeded);
        Assert.Equal([$"{Sni}: {Solna}"], _pipeline.Calls);
    }

    [Fact]
    public async Task A_job_that_is_not_queued_is_not_run_again()
    {
        var jobId = await AddJobAsync(Stockholm);

        await using var worker = await StartWorkerAsync();
        await _queue.EnqueueAsync(jobId, CancellationToken.None);
        await WaitForAsync(jobId, IngestJobStatus.Succeeded);

        // The same id again, e.g. from the startup check and the coordinator at once.
        await _queue.EnqueueAsync(jobId, CancellationToken.None);
        var marker = await AddJobAsync(Solna);
        await _queue.EnqueueAsync(marker, CancellationToken.None);
        await WaitForAsync(marker, IngestJobStatus.Succeeded);

        Assert.Equal([$"{Sni}: {Stockholm}", $"{Sni}: {Solna}"], _pipeline.Calls);
    }

    /// <summary>A worker with a real DbContext per scope and the fake pipeline.</summary>
    private async Task<WorkerHost> StartWorkerAsync()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => postgres.CreateContext());
        services.AddSingleton<IIngestPipeline>(_pipeline);
        var provider = services.BuildServiceProvider();

        var worker = new IngestWorker(
            _queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<IngestWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        return new WorkerHost(worker, provider);
    }

    private async Task<Guid> AddJobAsync(
        string municipality,
        string? other = null,
        string status = IngestJobStatus.Queued)
    {
        await using var db = postgres.CreateContext();
        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            Sni = Sni,
            Municipalities = other is null ? [municipality] : [municipality, other],
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.IngestJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Polls the row until it has the status, for at most 10 s.</summary>
    private async Task<IngestJob> WaitForAsync(Guid jobId, string status)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            await using var db = postgres.CreateContext();
            var job = await db.IngestJobs.FindAsync(jobId);
            if (job?.Status == status)
            {
                return job;
            }

            if (waited.Elapsed > TimeSpan.FromSeconds(10))
            {
                Assert.Fail($"The job is {job?.Status ?? "gone"}, expected {status}.");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>Stops the worker before the service provider and its DbContexts go away.</summary>
    private sealed class WorkerHost(IngestWorker worker, ServiceProvider provider)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
            await provider.DisposeAsync();
        }
    }

    /// <summary>Records each call and does what <see cref="Run"/> says (nothing by default).</summary>
    private sealed class FakePipeline : IIngestPipeline
    {
        public Func<IProgress<IngestProgress>, CancellationToken, Task> Run { get; set; } =
            (_, _) => Task.CompletedTask;

        /// <summary>"SNI: municipality, municipality" per call, in order.</summary>
        public List<string> Calls { get; } = [];

        public Task RunAsync(
            string sni,
            IReadOnlyCollection<string> municipalities,
            IProgress<IngestProgress> progress,
            CancellationToken cancellationToken)
        {
            Calls.Add($"{sni}: {string.Join(", ", municipalities)}");
            return Run(progress, cancellationToken);
        }
    }
}
