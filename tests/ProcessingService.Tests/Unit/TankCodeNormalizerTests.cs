using ProcessingService.Domain.Entities;
using Xunit;

namespace ProcessingService.Tests.Unit;

/// <summary>
/// Tests for the tank code rules.
/// </summary>
public sealed class TankCodeNormalizerTests
{
    [Theory]
    [InlineData("st1", "ST-01")]
    [InlineData("ST-1", "ST-01")]
    [InlineData("  mt-3  ", "MT-03")]
    [InlineData("Mt03", "MT-03")]
    [InlineData("ST-99", "ST-99")]   // This is the highest allowed number.
    public void NormalizeCode_ValidInput_ReturnsUppercaseLettersDashAndTwoDigits(string typed, string expected)
    {
        Assert.Equal(expected, Tank.NormalizeCode(typed));
    }

    [Theory]
    [InlineData("")]        // empty values
    [InlineData("   ")]     // whitespace only present
    [InlineData("S1")]      // too short values
    [InlineData("AB-01")]   // letters must be ST or MT only
    [InlineData("ST-0")]    // number just below 1
    [InlineData("ST-100")]  // number just above 99
    [InlineData("ST-1A")]   // not a whole number
    public void NormalizeCode_InvalidInput_ThrowsArgumentException(string typed)
    {
        Assert.Throws<ArgumentException>(() => Tank.NormalizeCode(typed));
    }
}
