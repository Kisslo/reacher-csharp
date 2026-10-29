using Microsoft.Extensions.Options;
using Reacher.Data.Entities;
using Reacher.Ingestion;
using Reacher.Ingestion.Jobs;
using Reacher.Ingestion.Options;

namespace Reacher.Api.Searches;

/// <summary>
/// In-memory stand-in for <see cref="IngestCoordinator"/>, turned on with
/// <c>Ingest:UseFakeCoordinator</c>. A poll of <c>GET /api/jobs/{id}</c> moves the job
/// forward in time so the frontend can show progress without calling SCB. Nothing is
/// written to the database. The real coordinator stays the default.
/// </summary>
public sealed class FakeIngestCoordinator(IOptions<IngestOptions> options, TimeProvider time)
    : IIngestCoordinator, IIngestJobReader
{
    /// <summary>
    /// Long enough that a one-second poll shows each phase, short enough for a demo.
    /// </summary>
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly List<FakeJob> _jobs = [];
    private readonly Dictionary<(string Sni, string Municipality), DateTimeOffset> _fetched = [];

    public Task<IngestRequestResult> RequestAsync(
        string sni,
        IReadOnlyCollection<string> municipalities,
        bool refresh,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sni);

        var wanted = municipalities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (wanted.Count == 0)
        {
            throw new ArgumentException(
                "At least one municipality is required.",
                nameof(municipalities));
        }

        lock (_gate)
        {
            SettleAll();

            if (!refresh && IsFresh(sni, wanted))
            {
                return Task.FromResult(IngestRequestResult.FreshData);
            }

            // Same rule as IngestCoordinator: a second click joins the job already running.
            var existing = _jobs.FirstOrDefault(job =>
                job.Sni == sni
                && job.Status is IngestJobStatus.Queued or IngestJobStatus.Running
                && wanted.All(job.Municipalities.Contains));
            if (existing is not null)
            {
                return Task.FromResult(new IngestRequestResult(existing.Id, existing.Status));
            }

            var created = new FakeJob(Guid.NewGuid(), sni, wanted, time.GetUtcNow());
            _jobs.Add(created);
            return Task.FromResult(new IngestRequestResult(created.Id, created.Status));
        }
    }

    public Task<JobResponse?> FindAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            SettleAll();
            var job = _jobs.FirstOrDefault(candidate => candidate.Id == jobId);
            return Task.FromResult(job?.ToResponse());
        }
    }

    private bool IsFresh(string sni, List<string> wanted)
    {
        var cutoff = time.GetUtcNow().AddDays(-options.Value.FreshnessDays);
        return wanted.All(municipality =>
            _fetched.TryGetValue((sni, municipality), out var fetchedAt) && fetchedAt >= cutoff);
    }

    private void SettleAll()
    {
        foreach (var job in _jobs)
        {
            Settle(job);
        }
    }

    private void Settle(FakeJob job)
    {
        if (job.Status is IngestJobStatus.Succeeded or IngestJobStatus.Failed)
        {
            return;
        }

        var elapsed = time.GetUtcNow() - job.CreatedAt;
        if (elapsed < Step)
        {
            return;
        }

        job.StartedAt ??= job.CreatedAt + Step;
        job.Status = IngestJobStatus.Running;

        if (elapsed < 2 * Step)
        {
            job.Phase = IngestPhases.Workplaces;
            job.Done = 0;
            job.Total = null;
            return;
        }

        if (elapsed < 3 * Step)
        {
            job.Phase = IngestPhases.Companies;
            job.Done = 1;
            job.Total = job.Municipalities.Count;
            return;
        }

        if (elapsed < 4 * Step)
        {
            job.Phase = IngestPhases.Financials;
            job.Done = 1;
            job.Total = job.Municipalities.Count;
            return;
        }

        job.Status = IngestJobStatus.Succeeded;
        job.Phase = IngestPhases.Financials;
        job.Done = job.Municipalities.Count;
        job.Total = job.Municipalities.Count;
        var finishedAt = job.FinishedAt ?? job.CreatedAt + (4 * Step);
        job.FinishedAt = finishedAt;
        foreach (var municipality in job.Municipalities)
        {
            _fetched[(job.Sni, municipality)] = finishedAt;
        }
    }

    private sealed class FakeJob(
        Guid id,
        string sni,
        List<string> municipalities,
        DateTimeOffset createdAt)
    {
        public Guid Id { get; } = id;

        public string Sni { get; } = sni;

        public List<string> Municipalities { get; } = municipalities;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public string Status { get; set; } = IngestJobStatus.Queued;

        public string? Phase { get; set; }

        public int Done { get; set; }

        public int? Total { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public JobResponse ToResponse() =>
            new(Id, Status, Phase, Done, Total, Error: null, StartedAt, FinishedAt);
    }
}
