namespace Reacher.WebsiteAnalysis.Brave;

/// <summary>
/// Indicates that Brave Search could not provide a valid response.
/// </summary>
public sealed class BraveSearchException : Exception
{
    public BraveSearchException(string message)
        : base(message)
    {
    }

    public BraveSearchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BraveSearchException(string message, System.Net.HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode? StatusCode { get; }
}
