using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Api.Infrastructure;
using ProcessingService.Application.Tanks;
using ProcessingService.Domain.Common;
using ProcessingService.Domain.Tanks;

namespace ProcessingService.Controllers;

/// <summary>The details a factory tank is added or amended with (SCRUM-61).</summary>
public sealed class SaveTankRequest
{
    /// <summary>Short code as painted on the plant. Set once, at creation.</summary>
    /// <example>ST1</example>
    [Required(ErrorMessage = "A tank code is required.")]
    [StringLength(10, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    /// <example>Storing Tank 1</example>
    [Required(ErrorMessage = "A tank name is required.")]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Storing or Mixing. Set once, at creation.</summary>
    /// <example>Storing</example>
    [Required(ErrorMessage = "A tank kind is required.")]
    public TankKind? Kind { get; set; }

    /// <summary>Working volume in litres.</summary>
    /// <example>10000</example>
    [Range(0.01, 1000000, ErrorMessage = "A tank's capacity must be greater than zero.")]
    public decimal CapacityLitres { get; set; }
}

/// <summary>
/// The factory's storing and mixing tanks (SCRUM-61). Storing tanks receive what a bowser brings;
/// mixing tanks take allocations from them and are what a processing run is worked from.
/// </summary>
/// <remarks>
/// A tank is never deleted. It is named on every unload and allocation it has carried, so removing
/// the row would leave those records pointing at nothing; taking one out of service is what
/// retiring a tank means.
/// </remarks>
[ApiController]
[Route("api/processing/tanks")]
[Authorize(Policy = ProcessingPolicies.ReadProcessing)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class TanksController : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    private readonly ITankService _tanks;

    public TanksController(ITankService tanks)
    {
        _tanks = tanks;
    }

    /// <summary>Lists the factory's tanks and what each currently holds.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="kind">Restrict to storing or mixing tanks.</param>
    /// <response code="200">The tanks.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TankView>), StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<IReadOnlyList<TankView>>> List(
        CancellationToken cancellationToken,
        [FromQuery] TankKind? kind = null) =>
        Ok(await _tanks.ListAsync(kind, cancellationToken));

    /// <summary>Reads one tank.</summary>
    /// <response code="200">The tank.</response>
    /// <response code="404">No such tank.</response>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<TankView>> Get(string code, CancellationToken cancellationToken)
    {
        var tank = await _tanks.GetAsync(code, cancellationToken);

        return tank is null ? NotFoundProblem(code) : Ok(tank);
    }

    /// <summary>Adds a tank to the factory.</summary>
    /// <response code="201">The tank was added.</response>
    /// <response code="400">The code, name, kind or capacity is missing or out of range.</response>
    /// <response code="409">A tank already carries that code.</response>
    [HttpPost]
    [Authorize(Policy = ProcessingPolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<ActionResult<TankView>> Create(
        [FromBody] SaveTankRequest request,
        CancellationToken cancellationToken)
    {
        var tank = await _tanks.CreateAsync(
            new SaveTankCommand(request.Code, request.Name, request.Kind!.Value, request.CapacityLitres),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { code = tank.Code }, tank);
    }

    /// <summary>Renames a tank and restates its working volume.</summary>
    /// <remarks>
    /// Neither the code nor the kind is amendable: the code is painted on the plant, and an unload
    /// names a storing tank while an allocation names a mixing one, so a tank that changed kind
    /// would make its own history unreadable.
    /// </remarks>
    /// <response code="200">The tank was amended.</response>
    /// <response code="400">The name or capacity is missing or out of range.</response>
    /// <response code="404">No such tank.</response>
    [HttpPut("{code}")]
    [Authorize(Policy = ProcessingPolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<ActionResult<TankView>> Update(
        string code,
        [FromBody] SaveTankRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _tanks.UpdateAsync(
                code,
                new SaveTankCommand(code, request.Name, request.Kind!.Value, request.CapacityLitres),
                cancellationToken));
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(code);
        }
    }

    /// <summary>Takes a tank out of service.</summary>
    /// <remarks>A tank still holding milk cannot be taken out: empty it first.</remarks>
    /// <response code="200">The tank is out of service.</response>
    /// <response code="400">The tank still holds milk.</response>
    /// <response code="404">No such tank.</response>
    [HttpPost("{code}/deactivate")]
    [Authorize(Policy = ProcessingPolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ActionResult<TankView>> Deactivate(string code, CancellationToken cancellationToken) =>
        ChangeStatus(code, TankStatus.UnderMaintenance, cancellationToken);

    /// <summary>Puts a tank back into service.</summary>
    /// <response code="200">The tank is in service.</response>
    /// <response code="404">No such tank.</response>
    [HttpPost("{code}/reactivate")]
    [Authorize(Policy = ProcessingPolicies.ManageTanks)]
    [ProducesResponseType(typeof(TankView), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProcessingProblemDetails), StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ActionResult<TankView>> Reactivate(string code, CancellationToken cancellationToken) =>
        ChangeStatus(code, TankStatus.Active, cancellationToken);

    private async Task<ActionResult<TankView>> ChangeStatus(
        string code,
        TankStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _tanks.ChangeStatusAsync(code, status, cancellationToken));
        }
        catch (EntityNotFoundException)
        {
            return NotFoundProblem(code);
        }
    }

    private ObjectResult NotFoundProblem(string code) => this.ProcessingProblem(
        StatusCodes.Status404NotFound,
        "entity_not_found",
        "Tank not found",
        $"No factory tank is registered under code '{code}'.");
}
