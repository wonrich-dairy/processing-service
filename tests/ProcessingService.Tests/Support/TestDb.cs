using Microsoft.EntityFrameworkCore;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Tests.Support;

public static class TestDb
{
    /// <summary>
    /// A real DbContext on the EF Core InMemory provider.
    /// </summary>
    public static ProcessingDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ProcessingDbContext(options);
    }

    /// <summary>The "Arrange" helper: add entities and save them.</summary>
    public static async Task SeedAsync(this ProcessingDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }
}
