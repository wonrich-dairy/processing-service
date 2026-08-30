using Microsoft.EntityFrameworkCore;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Entity Framework context for the Processing Service datastore (SCRUM-56).
/// </summary>
/// <remarks>
/// The scaffold carries no entities yet — the processing data model is SCRUM-57. What is here is
/// the wiring the model will need: the provider, the conventions, and a context the health check
/// can ask whether the database is reachable.
/// </remarks>
public class ProcessingDbContext : DbContext
{
    public ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : base(options)
    {
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
