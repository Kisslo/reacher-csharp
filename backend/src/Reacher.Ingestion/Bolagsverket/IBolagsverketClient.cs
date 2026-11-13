namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// Bolagsverket's API for valuable datasets (annual reports). Returns what Bolagsverket
/// sends; <see cref="FinancialsIngestService"/> decides which reports to fetch and
/// store. Its tests use a fake of this interface.
/// </summary>
public interface IBolagsverketClient
{
    /// <summary>
    /// The digitally filed annual reports of one company. Empty if Bolagsverket has none.
    /// </summary>
    /// <param name="orgnr">10 digits, no dash. Sent in the request body only, never logged.</param>
    Task<IReadOnlyList<AnnualReportDocument>> GetDocuments(
        string orgnr,
        CancellationToken cancellationToken);

    /// <summary>
    /// The xhtml files in one document, unpacked in memory. Empty if the document does not
    /// exist or holds no xhtml file. The caller disposes the streams.
    /// </summary>
    Task<IReadOnlyList<Stream>> Download(string documentId, CancellationToken cancellationToken);
}
