namespace Reacher.WebsiteAnalysis.Analysis;

/// <summary>
/// A website analysis failed. The message is fixed and never includes the URL,
/// an API key, or a response body (D16).
/// </summary>
public sealed class AnalysisException : Exception
{
    public const string InvalidUrlCode = "invalid_url";

    public const string TimeoutCode = "analysis_timeout";

    public const string FetchFailedCode = "analysis_fetch_failed";

    private AnalysisException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public static AnalysisException InvalidUrl() =>
        new(InvalidUrlCode, "The URL is not allowed.");

    public static AnalysisException Timeout() =>
        new(TimeoutCode, "The page fetch timed out.");

    public static AnalysisException FetchFailed() =>
        new(FetchFailedCode, "The page could not be fetched.");
}
