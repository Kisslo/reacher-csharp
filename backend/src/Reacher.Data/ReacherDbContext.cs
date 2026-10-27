using Microsoft.EntityFrameworkCore;

namespace Reacher.Data;

/// <summary>
/// The EF Core context for the whole app. Empty for now: T1-01 adds the entities
/// and the first migration from docs/contracts.md.
/// </summary>
public sealed class ReacherDbContext(DbContextOptions<ReacherDbContext> options)
    : DbContext(options)
{
}
