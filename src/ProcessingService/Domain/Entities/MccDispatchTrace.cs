using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Cached copy of MCC dispatch_notes in processingdb for true isolate.
/// Auto-created via polling MccDispatchSyncService every 30s reading mccdb.dispatch_notes.
/// No MCC edit, only read. When MCC creates new dispatch, we trace it here.
/// Supports partial unload: one dispatch can be split across multiple tanks via ProcessingRunStoringAllocation.
/// Dispatch Reference remains UNIQUE in this table and in ProcessingRuns.
/// </summary>
public class MccDispatchTrace
{
    public Guid Id { get; set; }

    [MaxLength(50)]
    public string Reference { get; set; } = string.Empty; // DN-20260915-01 UNIQUE

    [MaxLength(50)]
    public string BowserRegistration { get; set; } = string.Empty;

    public DateTime? DispatchDate { get; set; }

    public decimal TotalQuantityLitres { get; set; }

    [MaxLength(100)]
    public string DispatchedBy { get; set; } = string.Empty;

    public DateTime RecordedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastSyncedAtUtc { get; set; }

    public decimal TotalUnloadedKg { get; set; }

    public decimal RemainingKg => TotalQuantityLitres - TotalUnloadedKg;
}
