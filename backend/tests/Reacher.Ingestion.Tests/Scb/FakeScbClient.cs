using System.Runtime.CompilerServices;
using Reacher.Ingestion.Scb;

namespace Reacher.Ingestion.Tests.Scb;

/// <summary>
/// Stands in for SCB in the ingest tests (hard rule 8). Lists <see cref="Workplaces"/>,
/// answers from <see cref="LegalUnits"/> and records every legal unit asked for.
/// </summary>
internal sealed class FakeScbClient : IScbClient
{
    public List<WorkplacePartial> Workplaces { get; } = [];

    public Dictionary<string, LegalUnitFull> LegalUnits { get; } = [];

    /// <summary>Every peOrgNr passed to <see cref="GetLegalUnit"/>, in order.</summary>
    public List<string> LegalUnitRequests { get; } = [];

    public async IAsyncEnumerable<WorkplacePartial> GetWorkplacesBySni(
        string sni,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var workplace in Workplaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return workplace;
        }
    }

    public Task<LegalUnitFull?> GetLegalUnit(string peOrgNr, CancellationToken cancellationToken)
    {
        LegalUnitRequests.Add(peOrgNr);

        // The same guard as the real client, so a personnummer that got through fails loudly.
        if (!peOrgNr.StartsWith("16", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only a peOrgNr that starts with 16 may be fetched.");
        }

        return Task.FromResult(LegalUnits.GetValueOrDefault(peOrgNr));
    }

    public Task<WorkplaceFull?> GetWorkplace(string cfarNr, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The ingest doesn't use it.");

    public Task<int> CountWorkplacesBySni(string sni, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The ingest doesn't use it.");

    public Task<IReadOnlyList<CodeItem>> GetCodeTable(
        string table,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The ingest doesn't use it.");
}
