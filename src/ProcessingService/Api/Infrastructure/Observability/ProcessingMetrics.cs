using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Custom business metrics worth tracking for Processing (SCRUM-90).
/// Exposed via the /metrics Prometheus endpoint alongside the standard ASP.NET Core metrics
/// (request count, duration, error count per endpoint) that come from AddAspNetCoreInstrumentation().
/// </summary>
public sealed class ProcessingMetrics
{
    public const string MeterName = "Wonrich.Processing";

    private readonly Counter<long> _allocationsTotal;
    private readonly Counter<long> _stagesRecordedTotal;
    private readonly Counter<long> _holdsRaisedTotal;
    private readonly Counter<long> _unloadsRecordedTotal;
    private readonly Counter<long> _runsCreatedTotal;
    private readonly Counter<long> _coolingRecordedTotal;
    private readonly Counter<long> _productSplitsTotal;

    public ProcessingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName, "1.0.0");

        _allocationsTotal = meter.CreateCounter<long>(
            "processing_allocations_total",
            description: "Total number of allocations from storing tanks to mixing tanks");

        _stagesRecordedTotal = meter.CreateCounter<long>(
            "processing_stages_recorded_total",
            description: "Total number of heating/homogeniser/pasteuriser stages recorded");

        _holdsRaisedTotal = meter.CreateCounter<long>(
            "processing_holds_raised_total",
            description: "Total number of held consignments raised");

        _unloadsRecordedTotal = meter.CreateCounter<long>(
            "processing_unloads_recorded_total",
            description: "Total number of milk unloads recorded into storing tanks");

        _runsCreatedTotal = meter.CreateCounter<long>(
            "processing_runs_created_total",
            description: "Total number of processing runs created");

        _coolingRecordedTotal = meter.CreateCounter<long>(
            "processing_cooling_recorded_total",
            description: "Total number of post-pasteurisation cooling records");

        _productSplitsTotal = meter.CreateCounter<long>(
            "processing_product_splits_total",
            description: "Total number of product splits after cooling");
    }

    public void RecordAllocation(string storingTankCode, string mixingTankCode, decimal litres)
    {
        _allocationsTotal.Add(1,
            new TagList
            {
                { "storing_tank", storingTankCode },
                { "mixing_tank", mixingTankCode }
            });
    }

    public void RecordStage(string stageType)
    {
        _stagesRecordedTotal.Add(1, new TagList { { "stage_type", stageType } });
    }

    public void RecordHold(string reason)
    {
        _holdsRaisedTotal.Add(1, new TagList { { "reason", reason } });
    }

    public void RecordUnload(string storingTankCode, bool isDeviation)
    {
        _unloadsRecordedTotal.Add(1, new TagList
        {
            { "storing_tank", storingTankCode },
            { "is_deviation", isDeviation.ToString().ToLowerInvariant() }
        });
    }

    public void RecordRunCreated()
    {
        _runsCreatedTotal.Add(1);
    }

    public void RecordCooling()
    {
        _coolingRecordedTotal.Add(1);
    }

    public void RecordProductSplit(string productType)
    {
        _productSplitsTotal.Add(1, new TagList { { "product_type", productType } });
    }
}
