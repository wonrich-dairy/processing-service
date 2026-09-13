using Microsoft.EntityFrameworkCore;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// EF Core context for Processing Service (SCRUM-56 scaffold, Pomelo MySQL provider).
/// Initially empty, tables added in SCRUM-57.
/// </summary>
public class ProcessingDbContext : DbContext
{
    public ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProcessingDbContext).Assembly);
    }
}
