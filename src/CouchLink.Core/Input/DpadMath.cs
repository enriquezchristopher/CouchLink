namespace CouchLink.Core.Input;

public enum Dpad8 { None, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }

public static class DpadMath
{
    /// <summary>Combines D-pad flags into one of 8 directions; opposite flags cancel.</summary>
    public static Dpad8 FromButtons(PadButtons buttons)
    {
        int x = (buttons.HasFlag(PadButtons.DpadRight) ? 1 : 0) - (buttons.HasFlag(PadButtons.DpadLeft) ? 1 : 0);
        int y = (buttons.HasFlag(PadButtons.DpadDown) ? 1 : 0) - (buttons.HasFlag(PadButtons.DpadUp) ? 1 : 0);
        return (x, y) switch
        {
            (0, -1) => Dpad8.North,
            (1, -1) => Dpad8.NorthEast,
            (1, 0) => Dpad8.East,
            (1, 1) => Dpad8.SouthEast,
            (0, 1) => Dpad8.South,
            (-1, 1) => Dpad8.SouthWest,
            (-1, 0) => Dpad8.West,
            (-1, -1) => Dpad8.NorthWest,
            _ => Dpad8.None,
        };
    }
}
