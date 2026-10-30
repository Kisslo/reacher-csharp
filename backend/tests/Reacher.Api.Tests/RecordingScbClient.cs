using Reacher.Ingestion.Scb;

namespace Reacher.Api.Tests;

/// <summary>Stands in for SCB (hard rule 8). Answers from <see cref="Tables"/> and counts calls.</summary>
internal sealed class RecordingScbClient : IScbClient
{
    public int TableCalls { get; private set; }

    public Dictionary<string, IReadOnlyList<CodeItem>> Tables { get; } = [];

    /// <summary>When set, every table call throws this.</summary>
    public ScbException? Failure { get; set; }

    public Task<IReadOnlyList<CodeItem>> GetCodeTable(
        string table,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TableCalls++;
        return Failure is null
            ? Task.FromResult(Tables[table])
            : Task.FromException<IReadOnlyList<CodeItem>>(Failure);
    }

    public IAsyncEnumerable<WorkplacePartial> GetWorkplacesBySni(
        string sni,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<WorkplaceFull?> GetWorkplace(
        string cfarNr,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<LegalUnitFull?> GetLegalUnit(
        string peOrgNr,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountWorkplacesBySni(string sni, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
