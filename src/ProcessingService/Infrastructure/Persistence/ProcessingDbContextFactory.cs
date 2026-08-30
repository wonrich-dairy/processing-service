using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to build a context outside the running host, so migrations can be
/// scaffolded without a MySQL server being reachable. The connection string is only a placeholder
/// unless one is supplied through the ConnectionStrings__DefaultConnection environment variable.
/// </summary>
public sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    /// <summary>
    /// Points at nothing real. Scaffolding a migration only needs a provider that can build the
    /// model, and a committed placeholder keeps a working credential out of source control.
    /// </summary>
    private const string DesignTimeConnectionString =
        "Server=localhost;Port=3308;Database=processing;User Id=design_time;Password=design_time";

    public ProcessingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new ProcessingDbContext(options);
    }
}
