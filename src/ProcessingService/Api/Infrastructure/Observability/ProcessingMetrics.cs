using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Custom Prometheus metrics for Processing Service (SCRUM-90).
/// - Request count/duration/error per endpoint (via RequestMetricsMiddleware)
/// - Custom: allocations, stages, holds
/// Exposed via /metrics in Prometheus exposition format.
/// </summary>
public sealed class ProcessingMetrics
{
    public const string MeterName = "Wonrich.ProcessingService";
    private readonly Meter _meter;

    // Request metrics
    public readonly Counter<long> RequestsTotal;
    public readonly Histogram<double> RequestDurationSeconds;
    public readonly Counter<long> RequestErrorsTotal;

    // Custom business metrics
    public readonly Counter<long> AllocationsTotal;
    public readonly Counter<long> StagesTotal;
    public readonly Counter<long> HoldsTotal;
    public readonly Counter<long> UnloadsTotal;
    public readonly UpDownCounter<long> TanksInUse;

    public ProcessingMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        RequestsTotal = _meter.CreateCounter<long>("processing_http_requests_total", "requests", "Total HTTP requests per endpoint");
        RequestDurationSeconds = _meter.CreateHistogram<double>("processing_http_request_duration_seconds", "s", "HTTP request duration per endpoint");
        RequestErrorsTotal = _meter.CreateCounter<long>("processing_http_request_errors_total", "errors", "Total HTTP errors per endpoint");

        AllocationsTotal = _meter.CreateCounter<long>("processing_allocations_total", "allocations", "Total milk allocations from storing to mixing");
        StagesTotal = _meter.CreateCounter<long>("processing_stages_total", "stages", "Total processing stages recorded (heating, homogeniser, pasteuriser, cooling)");
        HoldsTotal = _meter.CreateCounter<long>("processing_holds_total", "holds", "Total held consignments");
        UnloadsTotal = _meter.CreateCounter<long>("processing_unloads_total", "unloads", "Total milk unloads into storing tanks");
        TanksInUse = _meter.CreateUpDownCounter<long>("processing_tanks_in_use", "tanks", "Current tanks in use");
    }

    public void RecordAllocation(string productCode, string facility)
    {
        var tags = new TagList { { "product", productCode }, { "facility", facility } };
        AllocationsTotal.Add(1, tags);
    }

    public void RecordStage(string stageName)
    {
        var tags = new TagList { { "stage", stageName } };
        StagesTotal.Add(1, tags);
    }

    public void RecordHold() => HoldsTotal.Add(1);
    public void RecordUnload() => UnloadsTotal.Add(1);
}
