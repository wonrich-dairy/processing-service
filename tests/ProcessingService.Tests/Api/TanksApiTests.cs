using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Application.Tanks;
using ProcessingService.Domain.Tanks;
using ProcessingService.Tests.Support;

namespace ProcessingService.Tests.Api;

/// <summary>
/// Configuring the factory's storing and mixing tanks (SCRUM-61).
/// </summary>
public class TanksApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static object NewTank(
        string code = "ST1",
        string kind = "Storing",
        decimal capacity = 10000m) =>
        new { code, name = $"Tank {code}", kind, capacityLitres = capacity };

    private static async Task<HttpClient> WithTanksAsync(ProcessingApiFactory factory)
    {
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        (await manager.PostAsJsonAsync("/api/processing/tanks", NewTank("ST1"))).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync("/api/processing/tanks", NewTank("MX1", "Mixing", 8000m)))
            .EnsureSuccessStatusCode();

        return manager;
    }

    [Fact]
    public async Task A_production_manager_adds_a_tank_and_it_starts_empty_and_in_service()
    {
        using var factory = new ProcessingApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        var response = await manager.PostAsJsonAsync("/api/processing/tanks", NewTank());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var tank = await response.Content.ReadFromJsonAsync<TankView>(JsonOptions);

        Assert.Equal("ST1", tank!.Code);
        Assert.Equal(TankKind.Storing, tank.Kind);
        Assert.Equal(TankStatus.Active, tank.Status);
        Assert.Equal(0m, tank.HeldLitres);
        Assert.Equal(10000m, tank.AvailableLitres);
    }

    [Fact]
    public async Task The_list_can_be_narrowed_to_one_kind()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);

        var all = await manager.GetFromJsonAsync<List<TankView>>("/api/processing/tanks", JsonOptions);
        Assert.Equal(2, all!.Count);

        var storing = await manager.GetFromJsonAsync<List<TankView>>(
            "/api/processing/tanks?kind=Storing", JsonOptions);

        Assert.Equal(["ST1"], storing!.Select(tank => tank.Code));

        var mixing = await manager.GetFromJsonAsync<List<TankView>>(
            "/api/processing/tanks?kind=Mixing", JsonOptions);

        Assert.Equal(["MX1"], mixing!.Select(tank => tank.Code));
    }

    [Fact]
    public async Task A_code_already_in_use_is_refused_with_409()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);

        var response = await manager.PostAsJsonAsync("/api/processing/tanks", NewTank("ST1"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_code", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(WonrichRoles.FactoryIntakeOfficer)]
    [InlineData(WonrichRoles.QualityAnalyst)]
    public async Task A_role_without_ManageTanks_may_read_but_not_configure(string role)
    {
        using var factory = new ProcessingApiFactory();
        await WithTanksAsync(factory);
        var client = factory.CreateClientAs(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/processing/tanks")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/processing/tanks", NewTank("ST9"))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync("/api/processing/tanks/ST1", NewTank("ST1"))).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/processing/tanks/ST1/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task An_mcc_role_has_no_business_here_at_all()
    {
        using var factory = new ProcessingApiFactory();
        var officer = factory.CreateClientAs(WonrichRoles.IntakeOfficer);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await officer.GetAsync("/api/processing/tanks")).StatusCode);
    }

    [Fact]
    public async Task Amending_a_tank_leaves_the_code_and_the_kind_alone()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);

        var response = await manager.PutAsJsonAsync(
            "/api/processing/tanks/ST1",
            new { code = "RENAMED", name = "Silo One", kind = "Mixing", capacityLitres = 12000m });

        response.EnsureSuccessStatusCode();
        var tank = await response.Content.ReadFromJsonAsync<TankView>(JsonOptions);

        Assert.Equal("ST1", tank!.Code);
        Assert.Equal(TankKind.Storing, tank.Kind);
        Assert.Equal("Silo One", tank.Name);
        Assert.Equal(12000m, tank.CapacityLitres);
    }

    [Fact]
    public async Task A_tank_goes_out_of_service_and_comes_back()
    {
        using var factory = new ProcessingApiFactory();
        var manager = await WithTanksAsync(factory);

        var out_ = await manager.PostAsync("/api/processing/tanks/ST1/deactivate", null);
        out_.EnsureSuccessStatusCode();
        Assert.Equal(
            TankStatus.UnderMaintenance,
            (await out_.Content.ReadFromJsonAsync<TankView>(JsonOptions))!.Status);

        var back = await manager.PostAsync("/api/processing/tanks/ST1/reactivate", null);
        back.EnsureSuccessStatusCode();
        Assert.Equal(
            TankStatus.Active,
            (await back.Content.ReadFromJsonAsync<TankView>(JsonOptions))!.Status);
    }

    [Fact]
    public async Task An_unknown_tank_answers_404()
    {
        using var factory = new ProcessingApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await manager.GetAsync("/api/processing/tanks/NOPE")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await manager.PostAsync("/api/processing/tanks/NOPE/deactivate", null)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_capacity_that_is_not_a_volume_is_refused(decimal capacity)
    {
        using var factory = new ProcessingApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        var response = await manager.PostAsJsonAsync(
            "/api/processing/tanks",
            NewTank("ST7", "Storing", capacity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tank_without_a_kind_is_refused()
    {
        using var factory = new ProcessingApiFactory();
        var manager = factory.CreateClientAs(WonrichRoles.ProductionManager);

        var response = await manager.PostAsJsonAsync(
            "/api/processing/tanks",
            new { code = "ST7", name = "Tank ST7", capacityLitres = 5000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
