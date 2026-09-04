using Microsoft.Extensions.Options;

namespace ProcessingService.Application.Abstractions;

/// <summary>The factory's operating parameters.</summary>
public sealed class FactoryOptions
{
    public const string SectionName = "Factory";

    /// <summary>Zone the factory's wall clock runs on.</summary>
    public string TimeZone { get; set; } = "Asia/Colombo";
}

/// <summary>
/// The factory's clock. A record is filed under the day it happened at the plant, so the wall
/// clock is what the service reads rather than UTC.
/// </summary>
public interface IFactoryClock
{
    /// <summary>The current instant.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>The same instant as wall-clock time at the factory.</summary>
    DateTime LocalNow { get; }

    /// <summary>Converts an instant to the factory's wall clock.</summary>
    DateTime ToLocal(DateTimeOffset instant);
}

/// <inheritdoc cref="IFactoryClock" />
public sealed class FactoryClock : IFactoryClock
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public FactoryClock(TimeProvider time, IOptions<FactoryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _time = time;
        _zone = Resolve(options.Value.TimeZone);
    }

    public DateTimeOffset UtcNow => _time.GetUtcNow();

    public DateTime LocalNow => ToLocal(UtcNow);

    public DateTime ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, _zone).DateTime;

    /// <summary>
    /// Falls back to UTC rather than refusing to start. A container image without the tz database
    /// would otherwise take the whole service down over a display concern.
    /// </summary>
    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
