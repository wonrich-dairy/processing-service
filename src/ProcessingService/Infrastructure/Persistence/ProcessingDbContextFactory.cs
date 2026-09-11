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
    /// Matches docker-compose.yml, so `dotnet ef database update` works against the local stack
    /// without the caller exporting a connection string first. Scaffolding a migration needs only a
    /// provider that can build the model, so an unreachable server is fine for that; applying one
    /// needs a real database, and this is where it is.
    ///
    /// These are local development credentials, identical to the compose defaults. Staging and
    /// production supply their own through ConnectionStrings__DefaultConnection.
    /// </summary>
    private const string DesignTimeConnectionString =
        "Server=localhost;Port=3307;Database=wonrich_processing;User Id=processing_user;Password=ProcessingDevPassword123!";

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
