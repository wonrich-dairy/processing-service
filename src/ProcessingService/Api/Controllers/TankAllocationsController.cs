using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.Allocations;
using ProcessingService.Domain.Entities;
using SRC.Authorization;
using Wonrich.Auth.Tokens;

namespace ProcessingService.Api.Controllers;

[ApiController]
[Route("api/tank-allocations")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician}")]
public sealed class TankAllocationsController : ControllerBase
{
    private readonly ITankAllocationService _allocations;

    public TankAllocationsController(ITankAllocationService allocations)
    {
        _allocations = allocations;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<object>>> List(CancellationToken cancellationToken)
    {
        var result = await _allocations.ListAsync(cancellationToken);
        var dto = result.Select(ToDto).ToList();
        return Ok(dto);
    }

    [HttpGet("{batchCode}")]
    public async Task<ActionResult<object>> GetByBatchCode(string batchCode, CancellationToken cancellationToken)
    {
        var alloc = await _allocations.GetByBatchCodeAsync(batchCode, cancellationToken);
        return alloc == null ? NotFound(new { message = $"Batch '{batchCode}' not found." }) : Ok(ToDto(alloc));
    }

    [HttpPost]
    public async Task<ActionResult<object>> Allocate([FromBody] CreateAllocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userId = User.UserId() ?? User.Identity?.Name ?? "unknown";
            var alloc = await _allocations.AllocateAsync(request, userId, cancellationToken);
            return CreatedAtAction(nameof(GetByBatchCode), new { batchCode = alloc.BatchCode }, ToDto(alloc));
        }
        catch (AllocationValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
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

    [HttpGet("storing/{storingTankId}/runs")]
    public async Task<ActionResult<object>> GetRunsInStoringTank(Guid storingTankId, [FromServices] ProcessingService.Infrastructure.Persistence.ProcessingDbContext db, CancellationToken cancellationToken)
    {
        // FIXED EDGE CASE: remaining per dispatch per tank = sum(allocations in) - sum(pours out)
        // Example: ST-02 had DN-02 50KG in, poured 25KG to MT, should show 25KG remaining, not 50KG
        // Tank total 110-25=85 correct, but card showed 50KG still - now fixed to 25KG

        // Get allocations in per run per tank
        var allocationsIn = await db.ProcessingRunStoringAllocations
            .Where(a => a.StoringTankId == storingTankId)
            .GroupBy(a => a.ProcessingRunId)
            .Select(g => new { ProcessingRunId = g.Key, TotalIn = g.Sum(a => a.QuantityKg) })
            .ToListAsync(cancellationToken);

        // If no allocations (old data), fallback to processing_runs direct
        if (allocationsIn.Count == 0)
        {
            var oldRuns = await db.ProcessingRuns
                .Include(r => r.QualityPanel)
                .Where(r => r.StoringTankId == storingTankId)
                .Select(r => new { Run = r, TotalIn = r.QuantityKg })
                .ToListAsync(cancellationToken);

            // Subtract pours out for old data too
            var oldRunIds = oldRuns.Select(x => x.Run.Id).ToList();
            var poursOutOld = await db.TankAllocations
                .Where(ta => ta.SourceStoringTankId == storingTankId && oldRunIds.Contains(ta.ProcessingRunId))
                .GroupBy(ta => ta.ProcessingRunId)
                .Select(g => new { ProcessingRunId = g.Key, TotalOut = g.Sum(ta => ta.QuantityKg) })
                .ToDictionaryAsync(x => x.ProcessingRunId, x => x.TotalOut, cancellationToken);

            var oldResult = oldRuns.Select(x =>
            {
                var totalOut = poursOutOld.TryGetValue(x.Run.Id, out var o) ? o : 0m;
                var remaining = x.TotalIn - totalOut;
                return new
                {
                    id = x.Run.Id,
                    allocationId = x.Run.Id,
                    dispatchNumber = x.Run.DispatchNumber,
                    quantityKg = remaining, // remaining after pours
                    totalRunQuantityKg = x.Run.QuantityKg,
                    state = x.Run.State.ToString(),
                    qualityTestStatus = x.Run.QualityTestStatus.ToString(),
                    alcoholResult = x.Run.QualityPanel != null ? x.Run.QualityPanel.AlcoholResult : null,
                    verdict = x.Run.QualityPanel != null ? x.Run.QualityPanel.Verdict : null,
                    createdAtUtc = x.Run.CreatedAtUtc,
                    canAllocate = remaining > 0.01m && x.Run.State == ProcessingRunState.ReleasedForAllocation && x.Run.QualityTestStatus == QualityTestStatus.Passed
                };
            }).Where(r => r.quantityKg > 0.01m).OrderByDescending(r => r.createdAtUtc).ToList();

            return Ok(oldResult);
        }

        var runIds = allocationsIn.Select(a => a.ProcessingRunId).ToList();

        var poursOut = await db.TankAllocations
            .Where(ta => ta.SourceStoringTankId == storingTankId && runIds.Contains(ta.ProcessingRunId))
            .GroupBy(ta => ta.ProcessingRunId)
            .Select(g => new { ProcessingRunId = g.Key, TotalOut = g.Sum(ta => ta.QuantityKg) })
            .ToDictionaryAsync(x => x.ProcessingRunId, x => x.TotalOut, cancellationToken);

        var runs = await db.ProcessingRuns
            .Include(r => r.QualityPanel)
            .Where(r => runIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var result = allocationsIn.Select(ai =>
        {
            var run = runs[ai.ProcessingRunId];
            var totalOut = poursOut.TryGetValue(ai.ProcessingRunId, out var o) ? o : 0m;
            var remaining = ai.TotalIn - totalOut;
            return new
            {
                id = run.Id,
                allocationId = ai.ProcessingRunId,
                dispatchNumber = run.DispatchNumber,
                quantityKg = remaining, // FIXED: remaining after pours, not total in
                totalRunQuantityKg = run.QuantityKg,
                state = run.State.ToString(),
                qualityTestStatus = run.QualityTestStatus.ToString(),
                alcoholResult = run.QualityPanel != null ? run.QualityPanel.AlcoholResult : null,
                verdict = run.QualityPanel != null ? run.QualityPanel.Verdict : null,
                createdAtUtc = run.CreatedAtUtc,
                canAllocate = remaining > 0.01m && run.State == ProcessingRunState.ReleasedForAllocation && run.QualityTestStatus == QualityTestStatus.Passed
            };
        }).Where(r => r.quantityKg > 0.01m).OrderByDescending(r => r.createdAtUtc).ToList();

        return Ok(result);
    }

    private static object ToDto(TankAllocation alloc)
    {
        return new
        {
            id = alloc.Id,
            batchCode = alloc.BatchCode,
            batchNumber = alloc.BatchNumber,
            batchLetter = alloc.BatchLetter,
            productType = alloc.ProductType.ToString(),
            quantityKg = alloc.QuantityKg,
            sourceStoringTankId = alloc.SourceStoringTankId,
            sourceStoringTankCode = alloc.SourceStoringTank?.Code,
            destinationMixingTankId = alloc.DestinationMixingTankId,
            destinationMixingTankCode = alloc.DestinationMixingTank?.Code,
            processingRunId = alloc.ProcessingRunId,
            dispatchNumber = alloc.ProcessingRun?.DispatchNumber,
            alcoholResult = alloc.ProcessingRun?.QualityPanel?.AlcoholResult,
            allocatedAtUtc = alloc.AllocatedAtUtc,
            createdAtUtc = alloc.CreatedAtUtc,
            createdBy = alloc.CreatedBy,
            overrideReason = alloc.OverrideReason
        };
    }
}
