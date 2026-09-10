using Microsoft.EntityFrameworkCore;
using ProcessingService.Domain.Runs;
using ProcessingService.Domain.Tanks;
using ProcessingService.Domain.Unloads;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Entity Framework context for the Processing Service datastore (SCRUM-56).
/// </summary>
/// <remarks>
/// The processing data model (SCRUM-57) starts at the factory's tanks and the loads unloaded into
/// them. References to records the MCC and Intake Service owns - dispatch notes, batches - are
/// carried as text rather than as foreign keys: the two services own separate databases and
/// neither may reach into the other's.
/// </remarks>
public class ProcessingDbContext : DbContext
{
    public ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : base(options)
    {
    }

    /// <summary>The factory's storing and mixing tanks (SCRUM-61).</summary>
    public DbSet<ProcessingTank> ProcessingTanks => Set<ProcessingTank>();

    /// <summary>Bowser loads unloaded into storing tanks (SCRUM-62).</summary>
    public DbSet<Unload> Unloads => Set<Unload>();

    /// <summary>Processing runs, one per unload, carrying each load through the factory (SCRUM-57).</summary>
    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProcessingDbContext).Assembly);
    }

    /// <summary>
    /// Every <see cref="DateOnly"/> in the model is stored through
    /// <see cref="DateOnlyToDateTimeConverter"/>, because the MySQL provider cannot read one back.
    /// Applying it as a convention rather than per property means a date on an entity added later
    /// is covered without anyone having to remember this.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        var dates = configurationBuilder
            .Properties<DateOnly>()
            .HaveConversion<DateOnlyToDateTimeConverter>();

        // Naming the store type keeps the columns `date` rather than the `datetime(6)` the
        // converted CLR type would otherwise infer. SQLite, which the tests run on, has no `date`
        // type and maps the converted value itself.
        if (Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            dates.HaveColumnType("date");
        }
    }
}