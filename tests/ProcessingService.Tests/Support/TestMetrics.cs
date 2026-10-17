using System.Diagnostics.Metrics;
using ProcessingService.Api.Infrastructure.Observability;

namespace ProcessingService.Tests.Support;

/// <summary>
/// ProcessingMetrics only needs an IMeterFactory.
/// </summary>
public sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = new();

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
            meter.Dispose();
        _meters.Clear();
    }
}

public static class TestMetrics
{
    public static ProcessingMetrics Create() => new(new TestMeterFactory());
}
