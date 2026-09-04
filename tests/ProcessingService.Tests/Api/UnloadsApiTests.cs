using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Application.Tanks;
using ProcessingService.Application.Unloads;
using ProcessingService.Tests.Support;

namespace ProcessingService.Tests.Api;

/// <summary>
/// A bowser's load going into a storing tank (SCRUM-62) - where milk enters this service.
/// </summary>
public class UnloadsApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<HttpClient> WithTanksAsync(ProcessingApiFactory factory)
    {
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        foreach (var (code, kind, capacity) in new[]
                 {
                     ("ST1", "Storing", 10000m),
                     ("ST2", "Storing", 500m),
                     ("MX1", "Mixing", 8000m)
                 })
        {
            (await manager.PostAsJsonAsync(
                "/api/processing/tanks",
                new { code, name = $"Tank {code}", kind, capacityLitres = capacity }))
                .EnsureSuccessStatusCode();
        }

        return manager;
    }

    private static object Unload(
        string note = "DN-20260904-01",
        string tank = "ST1",
        decimal litres = 1200m,
        decimal celsius = 4.2m) =>
        new
        {
            dispatchNoteReference = note,
            storingTankCode = tank,
            quantityLitres = litres,
            temperatureCelsius = celsius
        };

    [Fact]
    public async Task Recording_a_load_allocates_a_reference_and_names_who_took_it()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer, userName: "f.silva");

        var response = await officer.PostAsJsonAsync("/api/processing/unloads", Unload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var unload = await response.Content.ReadFromJsonAsync<UnloadView>(JsonOptions);

        Assert.StartsWith("UNL-", unload!.Reference);
        Assert.EndsWith("-01", unload.Reference);
        Assert.Equal("DN-20260904-01", unload.DispatchNoteReference);
        Assert.Equal("ST1", unload.StoringTankCode);
        Assert.Equal(1200m, unload.QuantityLitres);
        Assert.Equal("f.silva", unload.UnloadedBy);
    }

    [Fact]
    public async Task Each_load_of_the_day_takes_the_next_number()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var references = new List<string>();

        foreach (var note in new[] { "DN-A", "DN-B", "DN-C" })
        {
            var response = await officer.PostAsJsonAsync(
                "/api/processing/unloads",
                Unload(note, litres: 100m));

            response.EnsureSuccessStatusCode();
            references.Add((await response.Content.ReadFromJsonAsync<UnloadView>(JsonOptions))!.Reference);
        }

        Assert.Equal(references.Count, references.Distinct().Count());
        Assert.EndsWith("-03", references[2]);
    }

    [Fact]
    public async Task A_load_raises_what_the_storing_tank_holds()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload(litres: 1500m)))
            .EnsureSuccessStatusCode();

        var tank = await manager.GetFromJsonAsync<TankView>("/api/processing/tanks/ST1", JsonOptions);

        Assert.Equal(1500m, tank!.HeldLitres);
        Assert.Equal(8500m, tank.AvailableLitres);
    }

    [Fact]
    public async Task One_dispatch_note_is_unloaded_once()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload())).EnsureSuccessStatusCode();

        var again = await officer.PostAsJsonAsync("/api/processing/unloads", Unload());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dispatch_already_unloaded", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_mixing_tank_cannot_take_a_bowser_load()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload(tank: "MX1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tank_out_of_service_cannot_take_a_load()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        (await manager.PostAsync("/api/processing/tanks/ST1/deactivate", null)).EnsureSuccessStatusCode();

        var response = await officer.PostAsJsonAsync("/api/processing/unloads", Unload());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_load_that_would_overfill_the_tank_is_refused()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        // ST2 holds 500 L.
        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload("DN-A", "ST2", 400m)))
            .EnsureSuccessStatusCode();

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload("DN-B", "ST2", 200m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tank_that_does_not_exist_is_a_422_rather_than_a_404()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload(tank: "NOPE"));

        // Named in the body, not the route: the request is well formed and points at nothing.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(-40)]
    [InlineData(80)]
    public async Task A_temperature_no_instrument_would_report_is_refused(decimal celsius)
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.PostAsJsonAsync(
            "/api/processing/unloads",
            Unload(celsius: celsius));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_quality_analyst_may_read_the_unloads_but_not_record_one()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);
        var analyst = factory.CreateClientAs(WonrichRoles.QualityAnalyst);

        (await officer.PostAsJsonAsync("/api/processing/unloads", Unload())).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/processing/unloads")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await analyst.PostAsJsonAsync("/api/processing/unloads", Unload("DN-B"))).StatusCode);
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_one_factory_day()
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var today = new DateTime(2026, 9, 4, 8, 45, 0);
        var yesterday = today.AddDays(-1);

        foreach (var (note, at) in new[] { ("DN-A", today), ("DN-B", yesterday) })
        {
            (await officer.PostAsJsonAsync("/api/processing/unloads", new
            {
                dispatchNoteReference = note,
                storingTankCode = "ST1",
                quantityLitres = 100m,
                temperatureCelsius = 4.0m,
                unloadedAtLocal = at
            })).EnsureSuccessStatusCode();
        }

        var all = await officer.GetFromJsonAsync<List<UnloadView>>(
            "/api/processing/unloads", JsonOptions);
        Assert.Equal(2, all!.Count);

        var onDay = await officer.GetFromJsonAsync<List<UnloadView>>(
            "/api/processing/unloads?date=2026-09-04", JsonOptions);

        Assert.Equal(["DN-A"], onDay!.Select(unload => unload.DispatchNoteReference));
    }

    [Fact]
    public async Task An_unknown_reference_answers_404()
    {
        using var factory = new ProcessingApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.FactoryIntakeOfficer);

        var response = await officer.GetAsync("/api/processing/unloads/UNL-20260904-99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
