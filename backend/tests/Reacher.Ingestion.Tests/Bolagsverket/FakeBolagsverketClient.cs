using System.Text;
using Reacher.Ingestion.Bolagsverket;

namespace Reacher.Ingestion.Tests.Bolagsverket;

/// <summary>
/// Stands in for Bolagsverket in the financials ingest tests (hard rule 8). Lists the
/// reports added with <see cref="Add"/> and records every download.
/// </summary>
internal sealed class FakeBolagsverketClient : IBolagsverketClient
{
    private readonly Dictionary<string, List<AnnualReportDocument>> _documents = [];

    private readonly Dictionary<string, string> _xhtml = [];

    /// <summary>Every document id passed to <see cref="Download"/>, in order.</summary>
    public List<string> Downloads { get; } = [];

    /// <summary>When set, <see cref="GetDocuments"/> throws it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>An annual report of <paramref name="orgnr"/>, with the xhtml in it.</summary>
    public void Add(
        string orgnr,
        string documentId,
        DateOnly periodEnd,
        string xhtml,
        string? registeredAt = null)
    {
        if (!_documents.TryGetValue(orgnr, out var documents))
        {
            documents = [];
            _documents[orgnr] = documents;
        }

        documents.Add(new AnnualReportDocument(
            documentId,
            periodEnd,
            registeredAt ?? $"{periodEnd.Year}-09-15T10:00:00"));
        _xhtml[documentId] = xhtml;
    }

    public Task<IReadOnlyList<AnnualReportDocument>> GetDocuments(
        string orgnr,
        CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<IReadOnlyList<AnnualReportDocument>>(
            _documents.GetValueOrDefault(orgnr) ?? []);
    }

    public Task<IReadOnlyList<Stream>> Download(
        string documentId,
        CancellationToken cancellationToken)
    {
        Downloads.Add(documentId);
        IReadOnlyList<Stream> files = _xhtml.TryGetValue(documentId, out var xhtml)
            ? [new MemoryStream(Encoding.UTF8.GetBytes(xhtml))]
            : [];
        return Task.FromResult(files);
    }
}
