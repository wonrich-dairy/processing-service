namespace ProcessingService.Domain.Entities;

/// <summary>
/// Normalizes tank code per user requirement:
/// - Uppercase stored, user can type lower/mixed
/// - Format ST-01, MT-02, dash default, user types only letter+number
/// - st1 -> ST-01, MT2 -> MT-02, Mt03 -> MT-03
/// - Used in both backend validation and frontend preview
/// </summary>
public static class TankCodeNormalizer
{
    public static string Normalize(string input)
    {
        return Tank.NormalizeCode(input);
    }

    public static string Preview(string input)
    {
        try
        {
            return Normalize(input);
        }
        catch
        {
            return input.ToUpperInvariant();
        }
    }

    public static bool IsValid(string input)
    {
        try
        {
            Normalize(input);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
