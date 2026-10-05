namespace CouchLink.Core.Input;

public static class StickMath
{
    /// <summary>Maps -1..1 to an axis byte (1..255, 0 -> 128). Values outside are clamped.</summary>
    public static byte ToAxis(double value)
    {
        double clamped = Math.Clamp(value, -1.0, 1.0);
        return (byte)Math.Round(PadState.Center + clamped * 127.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Digital directions to a stick. Diagonals are scaled to unit length; opposites cancel.</summary>
    public static (byte X, byte Y) FromDirections(bool up, bool down, bool left, bool right)
    {
        double x = (right ? 1 : 0) - (left ? 1 : 0);
        double y = (down ? 1 : 0) - (up ? 1 : 0);
        if (x != 0 && y != 0)
        {
            x *= Math.Sqrt(0.5);
            y *= Math.Sqrt(0.5);
        }
        return (ToAxis(x), ToAxis(y));
    }
}
