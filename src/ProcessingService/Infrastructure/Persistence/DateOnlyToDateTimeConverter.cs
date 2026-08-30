using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ProcessingService.Infrastructure.Persistence;

/// <summary>
/// Stores a <see cref="DateOnly"/> as a <see cref="DateTime"/> at midnight, and reads it back.
/// </summary>
/// <remarks>
/// Oracle's <c>MySql.EntityFrameworkCore</c> maps <see cref="DateOnly"/> to a <c>date</c> column and
/// then asks <c>MySqlDataReader</c> for a <see cref="DateOnly"/>, which it cannot supply: every read
/// of an entity holding one throws
/// <c>InvalidCastException: Unable to cast object of type 'System.DateTime' to type 'System.DateOnly'</c>.
/// Writes are unaffected, so the fault only surfaces when a row is loaded back — which is how it
/// reached QA in the MCC service rather than CI.
/// <para>
/// Brought in from the first commit so this service never has that defect. Pomelo materialises
/// <see cref="DateOnly"/> natively and would make this unnecessary, but its newest release targets
/// EF Core 9 and this platform is on EF Core 10.
/// </para>
/// </remarks>
public sealed class DateOnlyToDateTimeConverter : ValueConverter<DateOnly, DateTime>
{
    public DateOnlyToDateTimeConverter()
        : base(
            date => date.ToDateTime(TimeOnly.MinValue),
            value => DateOnly.FromDateTime(value))
    {
    }
}
