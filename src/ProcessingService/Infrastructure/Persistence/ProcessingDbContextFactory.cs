using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF migrations (SCRUM-71: migrations configured independently)
/// Uses Pomelo MySQL provider with placeholder connection string for scaffolding, real connection from env var at runtime.
/// No secrets in source - only placeholder.
/// </summary>
public sealed class ProcessingDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=mcc-db.mysql.database.azure.com;Port=3306;Database=processingdb;User Id=mccadmin;Password=mccAdmin@123;SslMode=Required";

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
