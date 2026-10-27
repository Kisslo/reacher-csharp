namespace Reacher.WebsiteAnalysis.Brave;

public sealed class BraveQuotaExceededException : Exception
{
    public BraveQuotaExceededException()
        : base("The monthly Brave Search quota has been exceeded.")
    {
    }
}
