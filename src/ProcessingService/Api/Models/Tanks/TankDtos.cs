using System.ComponentModel.DataAnnotations;
using ProcessingService.Domain.Entities;

namespace ProcessingService.Api.Models.Tanks;

/// <summary>
/// Create tank request per SCRUM-61 AC. Code like ST-04, type Storing/Mixing, capacity KG, status Active default.
/// Code formatting: user types st1 -> saved ST-01, dash default, uppercase.
/// </summary>
public sealed class CreateTankRequest
{
    /// <example>ST-04</example>
    [Required(ErrorMessage = "Tank number is required.")]
    [StringLength(20, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type is required.")]
    public TankKind? Kind { get; set; }

    [Required(ErrorMessage = "Capacity is required.")]
    [Range(0.01, 100000, ErrorMessage = "Capacity must be positive.")]
    public decimal CapacityKg { get; set; }
}

/// <summary>
/// Update tank request - only CapacityKg editable per user clarification.
/// Code and Kind NOT amendable for history readability.
/// </summary>
public sealed class UpdateTankRequest
{
    [Required(ErrorMessage = "Capacity is required.")]
    [Range(0.01, 100000, ErrorMessage = "Capacity must be positive.")]
    public decimal CapacityKg { get; set; }

    /// <summary>Concurrency token for concurrent-safe updates</summary>
    public byte[]? RowVersion { get; set; }
}

public sealed class ChangeTankStatusRequest
{
    [Required(ErrorMessage = "Status is required.")]
    public TankStatus? Status { get; set; }

    public byte[]? RowVersion { get; set; }
}

public sealed class TankResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // UI generated: Storing tank 1, Mixing tank 1 - not stored
    public TankKind Kind { get; set; }
    public string KindName { get; set; } = string.Empty; // varchar
    public decimal CapacityKg { get; set; }
    public decimal RemainingKg { get; set; }
    public decimal AvailableKg { get; set; }
    public TankStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public byte[] RowVersion { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
