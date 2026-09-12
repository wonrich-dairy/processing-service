using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF migrations (SCRUM-71: migrations configured independently)
/// Uses Pomelo MySQL provider with dummy connection string for scaffolding, real connection from config at runtime.
/// </summary>
public sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=localhost;Port=3308;Database=processing;User Id=processing_user;Password=DevPassword123!";

    public ProcessingDbContext CreateDbContext(string[] args)
    {
        // Use explicit version to avoid needing a live MySQL connection at design time (for migrations add)
        var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));

        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseMySql(DesignTimeConnectionString, serverVersion)
            .Options;

        return new ProcessingDbContext(options);
    }
}
