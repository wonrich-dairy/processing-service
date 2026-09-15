using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Api.Models.Tanks;
using ProcessingService.Application.Tanks;
using ProcessingService.Domain.Entities;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// Tank configuration - storing and mixing tanks per SCRUM-61.
/// Full CRUD, no hardcoded counts, audit trail, code normalizer ST-01.
/// </summary>
[ApiController]
[Route("api/tanks")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician},{WonrichRoles.FactoryIntakeOfficer}")]
public sealed class TanksController : ControllerBase
{
    private readonly ITankService _tanks;

    public TanksController(ITankService tanks)
    {
        _tanks = tanks;
    }

    /// <summary>Lists tanks, filterable by kind and status. Only active tanks are selectable for processing screens.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TankResponse>>> List(
        [FromQuery] TankKind? kind,
        [FromQuery] TankStatus? status,
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _tanks.ListAsync(kind, status, activeOnly, cancellationToken);
        return Ok(result);
    }

    /// <summary>Gets a single tank by id.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TankResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tank = await _tanks.GetAsync(id, cancellationToken);
        return tank == null ? NotFound() : Ok(tank);
    }

    /// <summary>Adds a new tank. Code like ST-04, type Storing/Mixing, capacity KG, status Active default. Code formatting st1->ST-01.</summary>
    [HttpPost]
    public async Task<ActionResult<TankResponse>> Create([FromBody] CreateTankRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var tank = await _tanks.CreateAsync(request, userId, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = tank.Id }, tank);
        }
        catch (DuplicateTankCodeException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "", ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>Updates tank - only CapacityKg editable per user clarification. Code and Kind not amendable.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TankResponse>> Update(Guid id, [FromBody] UpdateTankRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var tank = await _tanks.UpdateAsync(id, request, userId, cancellationToken);
            return Ok(tank);
        }
        catch (TankNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Changes tank status Active/Inactive/UnderMaintenance. Default Active on creation.</summary>
    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<TankResponse>> ChangeStatus(Guid id, [FromBody] ChangeTankStatusRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var tank = await _tanks.ChangeStatusAsync(id, request, userId, cancellationToken);
            return Ok(tank);
        }
        catch (TankNotFoundException)
        {
            return NotFound();
        }
        catch (TankHasMilkException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(ex.ParamName ?? "", ex.Message);
            return ValidationProblem(ModelState);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Deletes a tank. Only if never used in processing history. Requires confirmation dialog type DELETE ST-01 on frontend.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _tanks.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (TankNotFoundException)
        {
            return NotFound();
        }
        catch (TankHasHistoryException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (TankHasMilkException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
}
