namespace Reacher.Ingestion.Bolagsverket;

/// <summary>
/// A Bolagsverket call failed. The message names the route, e.g. <c>POST /dokumentlista</c>
/// or <c>GET /dokument/{documentId}</c> with the real document id. Never the orgnr, the
/// token, the client secret or the response body (hard rule 6).
/// </summary>
public class BolagsverketException : Exception
{
    public BolagsverketException()
    {
    }

    public BolagsverketException(string message)
        : base(message)
    {
    }

    public BolagsverketException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Wrong client id or secret, or the app lacks the API in Bolagsverket's portal. The next
/// company will fail the same way, so the caller should stop the run.
/// </summary>
public sealed class BolagsverketAuthException : BolagsverketException
{
    public BolagsverketAuthException()
    {
    }

    public BolagsverketAuthException(string message)
        : base(message)
    {
    }

    public BolagsverketAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
