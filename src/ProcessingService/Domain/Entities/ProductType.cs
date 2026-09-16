namespace ProcessingService.Domain.Entities;

/// <summary>
/// Product type for batch code. Stored as varchar per SCRUM-57 AC.
/// SY=Set Yogurt, SK=Set Kiri, FM=Fresh Milk, FLM=Flavored Milk, DY=Drinking Yogurt (was DK, fixed per user)
/// Batch code format: [dayNumber]-[productCode]-[batchLetter] e.g. 1-SY-A, 258-DY-A
/// </summary>
public enum ProductType
{
    SY = 0, // Set Yogurt
    SK = 1, // Set Kiri
    FM = 2, // Fresh Milk
    FLM = 3, // Flavored Milk
    DY = 4, // Drinking Yogurt - fixed from DK to DY per factory

    [Obsolete("Use DY - kept for backward compat parsing old DK rows")]
    DK = DY // Drinking Yogurt old code, alias to DY so old DB rows still parse
}
