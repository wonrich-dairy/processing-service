using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProcessingService.Application.MccDispatch;
using ProcessingService.Infrastructure.Persistence;
using SRC.Authorization;

namespace ProcessingService.Api.Controllers;

/// <summary>
/// MCC dispatch endpoints for live preview + PARTIAL UNLOAD support.
/// Validates dispatch, shows remaining, filters fully unloaded from dropdown.
/// If DN-02 100KG total 50KG already unloaded, remaining 50KG shows.
/// If fully unloaded (10KG of 10KG), shows fully unloaded message, NOT partial.
/// </summary>
[ApiController]
[Route("api/mcc-dispatches")]
[Authorize(Roles = $"{WonrichRoles.SystemAdministrator},{WonrichRoles.ProductionManager},{WonrichRoles.ProcessingTechnician},{WonrichRoles.FactoryIntakeOfficer}")]
public sealed class MccDispatchesController : ControllerBase
{
    private readonly IMccDispatchClient _mcc;
    private readonly ProcessingDbContext _db;

    public MccDispatchesController(IMccDispatchClient mcc, ProcessingDbContext db)
    {
        _mcc = mcc;
        _db = db;
    }

    [HttpGet("validate/{dispatchNumber}")]
    public async Task<ActionResult<object>> Validate(string dispatchNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dispatchNumber))
            return BadRequest(new { message = "Dispatch number is required" });

        var normalized = dispatchNumber.Trim().ToUpperInvariant();

        if (!normalized.StartsWith("DN-"))
            return Ok(new { dispatchNumber = normalized, exists = false, message = $"Invalid format. Expected DN-YYYYMMDD-XX like DN-20260910-01", validFormat = false });

        var exists = await _mcc.ExistsAsync(normalized, cancellationToken);
        var dto = await _mcc.GetAsync(normalized, cancellationToken);

        if (!exists)
            return Ok(new { dispatchNumber = normalized, exists = false, message = $"Dispatch '{normalized}' not found in MCC", validFormat = true });

        // Calculate already unloaded from ProcessingRuns + Allocations + Trace (fixes old fully unloaded showing as partial)
        var trace = await _db.MccDispatchTraces.FirstOrDefaultAsync(t => t.Reference == normalized, cancellationToken);

        var alreadyFromRuns = await _db.ProcessingRuns
            .Where(r => r.DispatchNumber == normalized)
            .SumAsync(r => (decimal?)r.QuantityKg, cancellationToken) ?? 0m;

        var alreadyFromAlloc = await _db.ProcessingRunStoringAllocations
            .Where(a => a.ProcessingRun.DispatchNumber == normalized)
            .SumAsync(a => (decimal?)a.QuantityKg, cancellationToken) ?? 0m;

        var alreadyUnloaded = alreadyFromRuns;
        if (alreadyFromAlloc > alreadyUnloaded)
            alreadyUnloaded = alreadyFromAlloc;
        if (trace != null && trace.TotalUnloadedKg > alreadyUnloaded)
            alreadyUnloaded = trace.TotalUnloadedKg;

        var total = trace?.TotalQuantityLitres ?? dto?.TotalQuantityLitres ?? 0m;
        var remaining = total > 0 ? total - alreadyUnloaded : decimal.MaxValue;
        var isFullyUnloaded = total > 0 && remaining <= 0.01m;

        var message = isFullyUnloaded
            ? $"Dispatch {normalized} fully unloaded ({alreadyUnloaded:F0} KG of {total:F0} KG) - cannot unload again"
            : alreadyUnloaded > 0.01m && remaining > 0.01m
                ? $"Dispatch {normalized} found - {dto?.BowserRegistration} - Total {total:F0} KG, already unloaded {alreadyUnloaded:F0} KG, remaining {remaining:F0} KG - can split to another tank"
                : alreadyUnloaded > 0.01m
                    ? $"Dispatch {normalized} partially unloaded - {alreadyUnloaded:F0} KG done, {remaining:F0} KG remaining"
                    : $"Dispatch {normalized} found - {dto?.BowserRegistration} - {dto?.TotalQuantityLitres}L - not yet unloaded";

        return Ok(new
        {
            dispatchNumber = normalized,
            exists = true,
            validFormat = true,
            message,
            bowser = dto?.BowserRegistration,
            quantityLitres = dto?.TotalQuantityLitres,
            alreadyUnloadedKg = alreadyUnloaded,
            remainingKg = total > 0 ? remaining : (decimal?)null,
            isFullyUnloaded,
            dispatchDate = dto?.DispatchDate
        });
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<MccDispatchDto>>> ListRecent([FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        var result = await _mcc.ListRecentAsync(take, cancellationToken);
        return Ok(result);
    }
}
