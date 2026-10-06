using System.Runtime.InteropServices;

namespace CouchLink.Video;

/// <summary>Facts about the host's display, readable before capture starts.</summary>
public static partial class DisplayInfo
{
    private const int EnumCurrentSettings = -1;

    /// <summary>The primary display's refresh rate in Hz, or 60 if Windows can't tell.</summary>
    public static unsafe int PrimaryRefreshRate()
    {
        var mode = new DevMode { Size = (ushort)sizeof(DevMode) };
        return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) && mode.DisplayFrequency > 1
            ? (int)mode.DisplayFrequency
            : 60;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode mode);

    /// <summary>DEVMODEW (220 bytes), display fields only.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DevMode
    {
        public fixed char DeviceName[32];
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        public fixed char FormName[32];
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
}
