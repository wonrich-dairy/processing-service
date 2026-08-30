using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Tests.Infrastructure;

/// <summary>
/// Guards the two configuration promises the scaffold makes: no working credential is committed,
/// and dates are mapped in a way the MySQL provider can actually read back.
/// </summary>
public class ConfigurationTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProcessingService.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void No_connection_string_is_committed()
    {
        var committed = Path.Combine(RepositoryRoot(), "src", "ProcessingService", "appsettings.json");
        var settings = File.ReadAllText(committed);

        // The shipped file names the setting and leaves it empty; the value is supplied per
        // environment. A password reaching source control is the failure this catches.
        Assert.Contains("\"DefaultConnection\": \"\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_development_settings_file_is_ignored_by_git()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));

        Assert.Contains("appsettings.Development.json", gitignore, StringComparison.Ordinal);
    }

    /// <summary>
    /// The MySQL provider maps a DateOnly to a date column and then asks its reader for a
    /// DateOnly, which it cannot supply — every read of an entity holding one throws. The tests
    /// run on SQLite, which materialises DateOnly happily, so this builds the model against the
    /// MySQL provider instead. It needs no server.
    /// </summary>
    [Fact]
    public void Dates_are_stored_as_a_DateTime_because_MySQL_cannot_read_a_DateOnly()
    {
        var options = new DbContextOptionsBuilder<ProcessingDbContext>()
            .UseMySQL("Server=localhost;Database=processing;User Id=unused;Password=unused")
            .Options;

        using var context = new ProcessingDbContext(options);

        foreach (var date in DateProperties(context))
        {
            var converter = date.GetValueConverter();

            Assert.True(
                converter?.ProviderClrType == typeof(DateTime),
                $"{date.DeclaringType.DisplayName()}.{date.Name} would be read back as a DateOnly, "
                + "which MySqlDataReader cannot supply.");

            Assert.Equal("date", date.GetColumnType());
        }
    }

    private static IEnumerable<IProperty> DateProperties(ProcessingDbContext context) =>
        context.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property =>
                property.ClrType == typeof(DateOnly) || property.ClrType == typeof(DateOnly?));
}
