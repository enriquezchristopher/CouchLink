using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class StickMathTests
{
    [Theory]
    [InlineData(0.0, 128)]
    [InlineData(1.0, 255)]
    [InlineData(-1.0, 1)]
    [InlineData(0.5, 192)]
    [InlineData(5.0, 255)]
    [InlineData(-5.0, 1)]
    public void ToAxis_maps_and_clamps(double value, int expected)
    {
        Assert.Equal((byte)expected, StickMath.ToAxis(value));
    }

    [Theory]
    [InlineData(false, false, false, false, 128, 128)]
    [InlineData(true, false, false, false, 128, 1)]     // W = up = Y 0-ish
    [InlineData(false, true, false, false, 128, 255)]   // S = down
    [InlineData(false, false, true, false, 1, 128)]     // A = left
    [InlineData(false, false, false, true, 255, 128)]   // D = right
    [InlineData(true, false, false, true, 218, 38)]     // up + right, normalized
    [InlineData(true, true, false, false, 128, 128)]    // opposite cancel
    [InlineData(true, true, true, true, 128, 128)]      // all four held
    [InlineData(true, true, false, true, 255, 128)]     // up+down cancel, right stays full
    public void FromDirections_combines_keys(bool up, bool down, bool left, bool right, int x, int y)
    {
        Assert.Equal(((byte)x, (byte)y), StickMath.FromDirections(up, down, left, right));
    }
}
