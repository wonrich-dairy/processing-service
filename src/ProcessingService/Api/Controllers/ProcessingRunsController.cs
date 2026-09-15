using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProcessingService.Application.ProcessingRuns;
using ProcessingService.Domain.Entities;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// Unload bowser - creates ProcessingRun with dispatch validation, sensory before unload, capacity check.
/// Part of SCRUM-62. Returns DTOs to avoid JSON cycle (ProcessingRun -> StoringTank -> ProcessingRuns).
/// </summary>
[ApiController]
[Route("api/processing-runs")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician},{WonrichRoles.FactoryIntakeOfficer}")]
public sealed class ProcessingRunsController : ControllerBase
{
    private readonly IProcessingRunService _runs;

    public ProcessingRunsController(IProcessingRunService runs)
    {
        _runs = runs;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(CancellationToken cancellationToken)
    {
        var result = await _runs.ListAsync(cancellationToken);
        var dto = result.Select(ToDto).ToList();
        return Ok(dto);
    }

    [HttpGet("{dispatchNumber}")]
    public async Task<ActionResult<object>> GetByDispatch(string dispatchNumber, CancellationToken cancellationToken)
    {
        var run = await _runs.GetByDispatchAsync(dispatchNumber, cancellationToken);
        return run == null ? NotFound(new { message = $"Dispatch '{dispatchNumber}' not found." }) : Ok(ToDto(run));
    }

    [HttpPost("unload")]
    public async Task<ActionResult<object>> Unload([FromBody] CreateUnloadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var run = await _runs.CreateUnloadAsync(request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetByDispatch), new { dispatchNumber = run.DispatchNumber }, ToDto(run));
        }
        catch (DuplicateDispatchException ex)
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

    private static object ToDto(ProcessingRun run)
    {
        return new
        {
            id = run.Id,
            dispatchNumber = run.DispatchNumber,
            storingTankId = run.StoringTankId,
            storingTankCode = run.StoringTank?.Code,
            storingTankName = run.StoringTank != null ? $"{(run.StoringTank.Kind == TankKind.Storing ? "Storing" : "Mixing")} tank {run.StoringTank.Code.Split('-').LastOrDefault()}" : null,
            quantityKg = run.QuantityKg,
            temperatureC = run.TemperatureC,
            isTemperatureDeviation = run.IsTemperatureDeviation,
            state = run.State.ToString(),
            qualityTestStatus = run.QualityTestStatus.ToString(),
            holdReason = run.HoldReason,
            batchCode = run.BatchCode,
            createdAtUtc = run.CreatedAtUtc,
            updatedAtUtc = run.UpdatedAtUtc,
            createdBy = run.CreatedBy,
            hasQualityPanel = run.QualityPanel != null,
            qualityVerdict = run.QualityPanel?.Verdict
        };
    }
}
