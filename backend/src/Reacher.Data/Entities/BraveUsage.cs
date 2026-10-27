namespace Reacher.Data.Entities;

/// <summary>Brave requests per month. The only Brave data we store (D7).</summary>
public sealed class BraveUsage
{
    /// <summary><c>YYYY-MM</c>.</summary>
    public required string Month { get; set; }

    public int RequestCount { get; set; }
}
