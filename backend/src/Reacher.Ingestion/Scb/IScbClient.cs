namespace Reacher.Ingestion.Scb;

/// <summary>
/// SCB's AFR API. Returns what SCB sends; filtering and storage are in T1-05.
/// T1-05 tests use a fake of this interface.
/// </summary>
public interface IScbClient
{
    /// <summary>Every workplace whose primary SNI code is <paramref name="sni"/>, page by page.</summary>
    IAsyncEnumerable<WorkplacePartial> GetWorkplacesBySni(
        string sni,
        CancellationToken cancellationToken);

    /// <summary>One workplace, or null if SCB does not have it.</summary>
    Task<WorkplaceFull?> GetWorkplace(string cfarNr, CancellationToken cancellationToken);

    /// <summary>One legal unit, or null if SCB does not have it. Legal persons only.</summary>
    Task<LegalUnitFull?> GetLegalUnit(string peOrgNr, CancellationToken cancellationToken);

    Task<int> CountWorkplacesBySni(string sni, CancellationToken cancellationToken);

    /// <summary>A code table, e.g. <c>kommunkoder</c> or <c>anstklkoder</c>.</summary>
    Task<IReadOnlyList<CodeItem>> GetCodeTable(string table, CancellationToken cancellationToken);
}
