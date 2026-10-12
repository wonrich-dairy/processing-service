using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using ProcessingService.Infrastructure.Persistence;

namespace ProcessingService.Api.Infrastructure.Observability;

/// <summary>
/// Makes /metrics report what the service really measured (SCRUM-111, SCRUM-127).
///
/// The service records its numbers on a .NET meter (<see cref="ProcessingMetrics"/>), but nothing used to read
/// that meter: /metrics printed fixed zeros, so the request, error and latency panels stayed flat and the
/// HighErrorRate alert could never fire. This copies every measurement on the meter into prometheus-net, which
/// serves it on /metrics. The metric names and labels are the ones the dashboard and alerts already query.
/// It also fills the outbox backlog gauge from the database each time Prometheus scrapes.
/// </summary>
public sealed class PrometheusMeterBridge : IHostedService, IDisposable
{
    private static readonly string[] RequestLabels = ["method", "endpoint", "status"];

    private static readonly Counter Requests = Metrics.CreateCounter("processing_http_requests_total",
        "HTTP requests handled, by endpoint and status code.", new CounterConfiguration { LabelNames = RequestLabels });
    private static readonly Counter RequestErrors = Metrics.CreateCounter("processing_http_request_errors_total",
        "HTTP requests that ended in a server error (5xx), by endpoint.", new CounterConfiguration { LabelNames = RequestLabels });
    private static readonly Histogram RequestDuration = Metrics.CreateHistogram("processing_http_request_duration_seconds",
        "HTTP request duration in seconds, by endpoint and status code.",
        new HistogramConfiguration { LabelNames = RequestLabels, Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) });

    private static readonly Counter Allocations = Metrics.CreateCounter("processing_allocations_total",
        "Milk allocations from storing to mixing tanks.", new CounterConfiguration { LabelNames = ["product", "source", "dest"] });
    private static readonly Counter Stages = Metrics.CreateCounter("processing_stages_total",
        "Processing stages recorded.", new CounterConfiguration { LabelNames = ["stage"] });
    private static readonly Counter Holds = Metrics.CreateCounter("processing_holds_total", "Held consignments.");
    private static readonly Counter Unloads = Metrics.CreateCounter("processing_unloads_total",
        "Milk unloads into storing tanks.", new CounterConfiguration { LabelNames = ["tank", "deviation"] });
    private static readonly Gauge TanksInUse = Metrics.CreateGauge("processing_tanks_in_use",
        "Tanks currently in use, by kind.", new GaugeConfiguration { LabelNames = ["kind"] });
    private static readonly Gauge OutboxBacklog = Metrics.CreateGauge("processing_outbox_backlog",
        "Outbox rows not yet published to Kafka: Pending is waiting for the relay, Poisoned has given up and needs a person.",
        new GaugeConfiguration { LabelNames = ["status"] });

    // The scrape callback is registered once for the process; it reads whichever scope factory started last.
    private static readonly object CallbackLock = new();
    private static bool _callbackRegistered;
    private static IServiceScopeFactory? _scopeFactory;

    private MeterListener? _listener;

    public PrometheusMeterBridge(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The two outbox states always exist, at zero, so the panel shows 0 rather than "No data".
        OutboxBacklog.WithLabels("Pending");
        OutboxBacklog.WithLabels("Poisoned");

        lock (CallbackLock)
        {
            if (!_callbackRegistered)
            {
                Metrics.DefaultRegistry.AddBeforeCollectCallback(RefreshOutboxBacklogAsync);
                _callbackRegistered = true;
            }
        }

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ProcessingMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnMeasurement);
        _listener.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }

    private static void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        var value = Convert.ToDouble(measurement);
        switch (instrument.Name)
        {
            case "processing_http_requests_total":
                Requests.WithLabels(Tag(tags, "method"), Tag(tags, "endpoint"), Tag(tags, "status")).Inc(value);
                break;
            case "processing_http_request_errors_total":
                RequestErrors.WithLabels(Tag(tags, "method"), Tag(tags, "endpoint"), Tag(tags, "status")).Inc(value);
                break;
            case "processing_http_request_duration_seconds":
                RequestDuration.WithLabels(Tag(tags, "method"), Tag(tags, "endpoint"), Tag(tags, "status")).Observe(value);
                break;
            case "processing_allocations_total":
                Allocations.WithLabels(Tag(tags, "product"), Tag(tags, "source"), Tag(tags, "dest")).Inc(value);
                break;
            case "processing_stages_total":
                Stages.WithLabels(Tag(tags, "stage")).Inc(value);
                break;
            case "processing_holds_total":
                Holds.Inc(value);
                break;
            case "processing_unloads_total":
                Unloads.WithLabels(Tag(tags, "tank"), Tag(tags, "deviation")).Inc(value);
                break;
            case "processing_tanks_in_use":
                // Tanks are counted up and down with different extra tags; only the kind is a label, so both
                // directions land on the same series.
                TanksInUse.WithLabels(Tag(tags, "kind")).Inc(value);
                break;
        }
    }

    private static string Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
                return tag.Value?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    // Plain COUNT under InnoDB's consistent read does not block on the relay's FOR UPDATE SKIP LOCKED claim locks.
    private static async Task RefreshOutboxBacklogAsync(CancellationToken cancellationToken)
    {
        var scopeFactory = _scopeFactory;
        if (scopeFactory is null)
            return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
            var counts = await db.OutboxMessages
                .GroupBy(m => m.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            OutboxBacklog.WithLabels("Pending").Set(counts.FirstOrDefault(c => c.Status == "Pending")?.Count ?? 0);
            OutboxBacklog.WithLabels("Poisoned").Set(counts.FirstOrDefault(c => c.Status == "Poisoned")?.Count ?? 0);
        }
        catch
        {
            // The database is unreachable or a migration is pending: never fail the whole scrape. The gauge
            // keeps its last value and Prometheus still gets every other metric.
        }
    }
}
