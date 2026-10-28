namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// The document cannot be read as iXBRL. The message names the document id only,
/// never the orgnr or any content from the document (hard rule 6).
/// </summary>
public sealed class IxbrlParseException : Exception
{
    public IxbrlParseException()
    {
    }

    public IxbrlParseException(string message)
        : base(message)
    {
    }

    public IxbrlParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
