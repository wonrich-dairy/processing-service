using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Application.Unloads;
using ProcessingService.Domain.Runs;
using ProcessingService.Infrastructure.Persistence;
using ProcessingService.Tests.Support;

namespace ProcessingService.Tests.Api;

/// <summary>
/// The unload flow creating its processing run, and the arrival-temperature deviation flag. HTTP
/// in, then the database inspected through a scope: the factory holds one shared in-memory
/// connection, so what the request committed is what the scope reads back.
/// </summary>
public class UnloadRunWiringTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task WithStoringTankAsync(ProcessingApiFactory factory)
    {
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        (await manager.PostAsJsonAsync(
            "/api/processing/tanks",
            new { code = "ST1", name = "Tank ST1", kind = "Storing", capacityLitres = 10000m }))
            .EnsureSuccessStatusCode();
    }

    private static object Unload(string note, decimal celsius) =>
        new
        {
            dispatchNoteReference = note,
            storingTankCode = "ST1",
            quantityLitres = 1200m,
            temperatureCelsius = celsius
        };

    [Fact]
    public async Task Recording_an_unload_creates_one_run_awaiting_its_lab_result()
    {
        using var factory = new ProcessingApiFactory();
        await WithStoringTankAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload("DN-RUN-01", 2.4m));

        response.EnsureSuccessStatusCode();
        var unload = await response.Content.ReadFromJsonAsync<UnloadView>(JsonOptions);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        var stored = await db.Unloads.SingleAsync(one => one.Reference == unload!.Reference);
        var run = Assert.Single(await db.ProcessingRuns.ToListAsync());

        Assert.Equal(stored.Id, run.UnloadId);
        Assert.Equal(RunState.AwaitingLabResult, run.State);
        Assert.Equal(stored.RecordedAtUtc, run.StateChangedAtUtc);
    }

    [Fact]
    public async Task A_rejected_duplicate_unload_creates_no_second_run()
    {
        using var factory = new ProcessingApiFactory();
        await WithStoringTankAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload("DN-RUN-02", 2.1m)))
            .EnsureSuccessStatusCode();

        var again = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload("DN-RUN-02", 2.1m));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();

        Assert.Equal(1, await db.ProcessingRuns.CountAsync());
    }

    [Fact]
    public async Task One_unload_cannot_end_up_with_two_runs()
    {
        using var factory = new ProcessingApiFactory();
        await WithStoringTankAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload("DN-RUN-03", 2.1m)))
            .EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProcessingDbContext>();
        var unload = await db.Unloads.FirstAsync(one => one.DispatchNoteReference == "DN-RUN-03");

        db.ProcessingRuns.Add(ProcessingRun.Start(Guid.NewGuid(), unload, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    public async Task Arrival_within_1_to_3_degrees_sets_no_deviation_flag(decimal celsius)
    {
        using var factory = new ProcessingApiFactory();
        await WithStoringTankAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload("DN-TEMP-OK", celsius));

        response.EnsureSuccessStatusCode();
        var unload = await response.Content.ReadFromJsonAsync<UnloadView>(JsonOptions);

        Assert.False(unload!.IsTemperatureDeviation);

        var reread = await officer.GetFromJsonAsync<UnloadView>(
            $"/api/processing/unloads/{unload.Reference}", JsonOptions);

        Assert.False(reread!.IsTemperatureDeviation);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(4.8)]
    [InlineData(5.2)]
    public async Task Arrival_outside_1_to_3_degrees_is_saved_with_a_deviation_flag(decimal celsius)
    {
        using var factory = new ProcessingApiFactory();
        await WithStoringTankAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload("DN-TEMP-DEV", celsius));

        // Outside the expected band but inside the hard -5 to 40 °C limit: warned, not blocked.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var unload = await response.Content.ReadFromJsonAsync<UnloadView>(JsonOptions);

        Assert.True(unload!.IsTemperatureDeviation);

        var reread = await officer.GetFromJsonAsync<UnloadView>(
            $"/api/processing/unloads/{unload.Reference}", JsonOptions);

        Assert.True(reread!.IsTemperatureDeviation);
    }
}