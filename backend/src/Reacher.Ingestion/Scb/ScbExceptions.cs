namespace Reacher.Ingestion.Scb;

/// <summary>
/// An SCB call failed. The message names the route template, e.g.
/// <c>/juridiskaenheter/{peOrgNr}/full</c>, never the real path, the response body
/// or the API key (hard rule 6).
/// </summary>
public class ScbException : Exception
{
    public ScbException()
    {
    }

    public ScbException(string message)
        : base(message)
    {
    }

    public ScbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>SCB loads data 04:00-04:30 Swedish time and does not answer then.</summary>
public sealed class ScbMaintenanceWindowException : ScbException
{
    public ScbMaintenanceWindowException()
        : base("SCB is closed for its nightly load 04:00-04:30 Swedish time. Try after 04:30.")
    {
    }

    public ScbMaintenanceWindowException(string message)
        : base(message)
    {
    }

    public ScbMaintenanceWindowException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
