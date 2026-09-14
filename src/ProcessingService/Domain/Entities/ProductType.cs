namespace ProcessingService.Domain.Entities;

/// <summary>
/// Product type for batch code. Stored as varchar per SCRUM-57 AC.
/// SY=Set Yogurt, SK=Set Kiri, FM=Fresh Milk, FLM=Flavored Milk, DK=Drinking Yogurt
/// Batch code format: [dayNumber]-[productCode]-[batchLetter] e.g. 1-SY-A
/// </summary>
public enum ProductType
{
    SY = 0, // Set Yogurt
    SK = 1, // Set Kiri
    FM = 2, // Fresh Milk
    FLM = 3, // Flavored Milk
    DK = 4 // Drinking Yogurt
}
