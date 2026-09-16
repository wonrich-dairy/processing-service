using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.Stages;
using ProcessingService.Domain.Entities;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

[ApiController]
[Route("api/processing-stages")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician}")]
public sealed class ProcessingStagesController : ControllerBase
{
    private readonly IProcessingStageService _stages;
    private readonly ProcessingService.Infrastructure.Persistence.ProcessingDbContext _db;

    public ProcessingStagesController(IProcessingStageService stages, ProcessingService.Infrastructure.Persistence.ProcessingDbContext db)
    {
        _stages = stages;
        _db = db;
    }

    [HttpPost("start")]
    public async Task<ActionResult<object>> Start([FromBody] StartStageRequest request, CancellationToken ct)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var stage = await _stages.StartAsync(request, userId, ct);
            return Ok(ToDto(stage));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id}/end")]
    public async Task<ActionResult<object>> End(Guid id, [FromBody] EndStageRequest request, CancellationToken ct)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var stage = await _stages.EndAsync(id, request, userId, ct);
            return Ok(ToDto(stage));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpGet("mixing-tank/{mixingTankId}")]
    public async Task<ActionResult<object>> ListByMixingTank(Guid mixingTankId, CancellationToken ct)
    {
        var stages = await _stages.ListByMixingTankAsync(mixingTankId, ct);
        return Ok(stages.Select(ToDto));
    }

    [HttpGet("active")]
    public async Task<ActionResult<object>> ListActive(CancellationToken ct)
    {
        var stages = await _stages.ListActiveAsync(ct);
        return Ok(stages.Select(ToDto));
    }

    [HttpGet("active-batches")]
    public async Task<ActionResult<object>> ListActiveBatches(CancellationToken ct)
    {
        var batches = await _stages.ListActiveBatchesAsync(ct);
        var dto = batches.Select(a => new
        {
            id = a.Id,
            batchCode = a.BatchCode,
            productType = a.ProductType.ToString(),
            quantityKg = a.QuantityKg,
            mixingTankId = a.DestinationMixingTankId,
            mixingTankCode = a.DestinationMixingTank?.Code,
            sourceStoringTankCode = a.SourceStoringTank?.Code,
            dispatchNumber = a.ProcessingRun?.DispatchNumber,
            allocatedAtUtc = a.AllocatedAtUtc,
            createdAtUtc = a.CreatedAtUtc
        });
        return Ok(dto);
    }

    private static object ToDto(ProcessingStage s) => new
    {
        id = s.Id,
        processingRunId = s.ProcessingRunId,
        mixingTankId = s.MixingTankId,
        mixingTankCode = s.MixingTank?.Code,
        stageType = s.StageType.ToString(),
        startTimeUtc = s.StartTimeUtc,
        endTimeUtc = s.EndTimeUtc,
        endTemperatureC = s.EndTemperatureC,
        isDeviation = s.IsDeviation,
        durationMinutes = s.DurationMinutes,
        cultureAdded = s.CultureAdded,
        createdBy = s.CreatedBy,
        createdAtUtc = s.CreatedAtUtc
    };
}
