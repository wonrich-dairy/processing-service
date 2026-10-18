using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF migrations (SCRUM-71: migrations configured independently)
/// the real connection string comes from the ConnectionStrings__DefaultConnection env var.
/// if it is not set, a fake local one is used. ef only needs it to know the provider when it builds migrations or the migration script, it does not connect to anything.
/// so there is no user or password here on purpose (sonarqube rule S6703, secrets in code).
/// </summary>
public sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    private const string DesignTimeConnectionString =
         "Server=localhost;Port=3306;Database=processingdb";

    public ProcessingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DesignTimeConnectionString;

        var serverVersion = new MySqlServerVersion(new Version(8, 4, 0));

        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseMySql(connectionString, serverVersion)
            .Options;

        return new ProcessingDbContext(options);
    }
}
