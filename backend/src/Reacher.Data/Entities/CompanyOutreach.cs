namespace Reacher.Data.Entities;

/// <summary>User-entered outreach state shared by everyone using the internal tool.</summary>
public sealed class CompanyOutreach
{
    public required string Orgnr { get; set; }

    public DateTimeOffset? CalledAt { get; set; }

    public bool? Willing { get; set; }

    public string? Note { get; set; }

    public required DateTimeOffset UpdatedAt { get; set; }

    public Company Company { get; set; } = null!;
}
