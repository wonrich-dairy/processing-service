using Xunit;
using System.Text.Json;
using Wonrich.Processing.Contracts.Events;

namespace ProcessingService.Tests.Kafka;

/// <summary>
/// Pins the wire format of the published contracts (SCRUM-130). Consumers (Traceability, Quality Lab) read
/// these exact camelCase field names, so a rename here is a breaking change that needs a new major version
/// of Wonrich.Processing.Contracts and a new .v2 topic, not a quiet edit.
/// </summary>
public class ProcessingContractsTests
{
    // The options OutboxWriter serializes with.
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly string[] BaseFields =
        ["eventId", "batchId", "dispatchNumber", "timestampUtc", "isDeviation", "schemaVersion", "correlationId"];

    private static string[] FieldsOf<T>(T evt) where T : ProcessingEventBase
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(evt, WriteOptions));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    private static string[] Expected(params string[] own) =>
        BaseFields.Concat(own).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void MilkAllocated_keeps_its_field_names() => Assert.Equal(
        Expected("sourceStoringTankId", "sourceStoringTankCode", "destinationMixingTankId", "destinationMixingTankCode",
            "quantityKg", "productType", "batchCode", "batchNumber", "batchLetter", "allocatedAtUtc", "allocatedBy", "overrideReason"),
        FieldsOf(new MilkAllocatedToMixingTankEvent()));

    [Fact]
    public void StageRecorded_keeps_its_field_names() => Assert.Equal(
        Expected("mixingTankId", "mixingTankCode", "stageType", "startTimeUtc", "endTimeUtc", "endTemperatureC",
            "durationMinutes", "cultureAdded", "cultureAddedAtUtc", "recordedBy"),
        FieldsOf(new ProcessingStageRecordedEvent()));

    [Fact]
    public void Completed_keeps_its_field_names() => Assert.Equal(
        Expected("mixingTankId", "mixingTankCode", "completedAtUtc", "totalDurationMinutes", "hasDeviation", "completedBy"),
        FieldsOf(new ProcessingCompletedEvent()));

    [Fact]
    public void HoldRaised_keeps_its_field_names() => Assert.Equal(
        Expected("holdId", "reason", "raisedAtUtc", "raisedBy", "failedParameter", "failedValue"),
        FieldsOf(new ProcessingHoldRaisedEvent()));

    [Fact]
    public void HoldResolved_keeps_its_field_names() => Assert.Equal(
        Expected("holdId", "reason", "resolution", "raisedAtUtc", "resolvedAtUtc", "resolvedBy"),
        FieldsOf(new ProcessingHoldResolvedEvent()));

    [Fact]
    public void A_consumer_reading_with_web_defaults_gets_the_event_back()
    {
        var sent = new ProcessingHoldRaisedEvent
        {
            BatchId = "DN-20260910-01", DispatchNumber = "DN-20260910-01", HoldId = "run-1", Reason = "COB positive",
            RaisedAtUtc = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), IsDeviation = true,
            FailedParameter = "COB", FailedValue = "Positive", CorrelationId = "abc123"
        };

        var payload = JsonSerializer.Serialize(sent, WriteOptions);
        var received = JsonSerializer.Deserialize<ProcessingHoldRaisedEvent>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(sent, received);
    }

    [Fact]
    public void The_event_type_header_is_the_short_type_name_without_a_namespace()
    {
        // OutboxWriter sets the eventType header from the runtime type's Name. Consumers switch on it.
        Assert.Equal("ProcessingHoldRaisedEvent", typeof(ProcessingHoldRaisedEvent).Name);
        Assert.Equal("v1", new ProcessingStageRecordedEvent().SchemaVersion);
    }
}
