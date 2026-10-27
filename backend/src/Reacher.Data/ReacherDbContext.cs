using Microsoft.EntityFrameworkCore;
using Reacher.Data.Entities;

namespace Reacher.Data;

/// <summary>
/// The EF Core context for the whole app. The model mirrors the schema in
/// docs/contracts.md; changing it is a joint change.
/// </summary>
public sealed class ReacherDbContext(DbContextOptions<ReacherDbContext> options)
    : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Workplace> Workplaces => Set<Workplace>();

    public DbSet<FinancialFact> FinancialFacts => Set<FinancialFact>();

    public DbSet<Coverage> Coverage => Set<Coverage>();

    public DbSet<IngestJob> IngestJobs => Set<IngestJob>();

    public DbSet<BraveUsage> BraveUsage => Set<BraveUsage>();

    // Here rather than at each UseNpgsql call, so the API, dotnet ef and the tests
    // can never disagree on column names.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(company =>
        {
            // Ten digits, third digit 2-9: the shape of an organisationsnummer. A
            // personnummer has a month there (0 or 1), so it can never be stored (D2).
            company.ToTable("company", table => table.HasCheckConstraint(
                "ck_company_orgnr", "orgnr ~ '^[0-9]{2}[2-9][0-9]{7}$'"));
            company.HasKey(c => c.Orgnr);
        });

        modelBuilder.Entity<Workplace>(workplace =>
        {
            workplace.ToTable("workplace");
            workplace.HasKey(w => w.Cfar);
            workplace.HasOne(w => w.Company)
                .WithMany(c => c.Workplaces)
                .HasForeignKey(w => w.Orgnr)
                .OnDelete(DeleteBehavior.Cascade);
            workplace.HasIndex(w => new { w.Municipality, w.Sni });
            workplace.HasIndex(w => w.Orgnr);
        });

        modelBuilder.Entity<FinancialFact>(fact =>
        {
            fact.ToTable("financial_fact");
            // The primary key starts with (orgnr, period_end), so its index also serves
            // lookups by company and year. No separate index needed.
            fact.HasKey(f => new { f.Orgnr, f.PeriodEnd, f.Key });
            fact.HasOne(f => f.Company)
                .WithMany(c => c.FinancialFacts)
                .HasForeignKey(f => f.Orgnr)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Coverage>(coverage =>
        {
            coverage.ToTable("coverage");
            coverage.HasKey(c => new { c.Sni, c.Municipality });
        });

        modelBuilder.Entity<IngestJob>(job =>
        {
            job.ToTable("ingest_job");
            job.HasKey(j => j.Id);
        });

        modelBuilder.Entity<BraveUsage>(usage =>
        {
            usage.ToTable("brave_usage");
            usage.HasKey(u => u.Month);
        });
    }
}
