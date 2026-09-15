using System.ComponentModel.DataAnnotations;

namespace ProcessingService.Domain.Entities;

/// <summary>
/// Storing or mixing tank. Capacity in KG per user clarification (not litres).
/// No Name stored - UI generates "Storing tank 1" etc.
/// RemainingKg stored and updated on each allocation/unload for next-day retrieval.
/// Concurrency token for concurrent-safe updates per SCRUM-57 AC.
/// Code format ST-01, MT-02 - user types st1 -> saved ST-01, dash default, uppercase.
/// </summary>
public class Tank
{
    public Guid Id { get; set; }

    /// <summary>Tank code like ST-01, MT-02 - unique, stored uppercase</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    public TankKind Kind { get; set; }

    /// <summary>Maximum capacity in KG - only editable field per user</summary>
    public decimal CapacityKg { get; set; }

    /// <summary>Remaining milk in KG - computed and stored, updated on each unload/allocation</summary>
    public decimal RemainingKg { get; set; }

    public TankStatus Status { get; set; } = TankStatus.Active;

    /// <summary>Concurrency token for concurrent-safe updates - row-level locking strategy</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;

    // Navigation
    public ICollection<ProcessingRun> ProcessingRuns { get; set; } = new List<ProcessingRun>();
    public ICollection<TankAllocation> SourceAllocations { get; set; } = new List<TankAllocation>();
    public ICollection<TankAllocation> DestinationAllocations { get; set; } = new List<TankAllocation>();
    public ICollection<ProcessingStage> Stages { get; set; } = new List<ProcessingStage>();

    public static string NormalizeCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Tank code is required", nameof(input));

        // Remove dash, trim, uppercase - user types st1, MT2, Mt03 -> ST-01, MT-02, MT-03
        var cleaned = input.Trim().Replace("-", "").ToUpperInvariant();

        // Expect format like ST1, MT02, ST03 etc - first 2 chars letters, rest numbers
        if (cleaned.Length < 3)
            throw new ArgumentException($"Tank code '{input}' too short. Expected format ST-01, MT-02", nameof(input));

        var letters = new string(cleaned.TakeWhile(char.IsLetter).ToArray());
        var numbers = new string(cleaned.SkipWhile(char.IsLetter).ToArray());

        if (string.IsNullOrWhiteSpace(letters) || string.IsNullOrWhiteSpace(numbers))
            throw new ArgumentException($"Tank code '{input}' invalid. Expected letters + numbers like ST1", nameof(input));

        // Fix: only 2 letters allowed, only ST or MT per business rule ST-01, MT-02
        if (letters.Length != 2)
            throw new ArgumentException($"Tank code '{input}' invalid. Only 2 letters allowed. Use ST-01 or MT-02 format", nameof(input));

        if (letters != "ST" && letters != "MT")
            throw new ArgumentException($"Tank code '{input}' invalid. Only ST (Storing) or MT (Mixing) allowed", nameof(input));

        if (!int.TryParse(numbers, out var num))
            throw new ArgumentException($"Tank code '{input}' number part invalid", nameof(input));

        if (num < 1 || num > 99)
            throw new ArgumentException($"Tank code '{input}' number must be between 1 and 99", nameof(input));

        // Format number as 2 digits: 1 -> 01, 2 -> 02
        return $"{letters}-{num:D2}";
    }
}
