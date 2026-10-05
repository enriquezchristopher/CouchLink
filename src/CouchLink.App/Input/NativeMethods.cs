using System.Runtime.InteropServices;

namespace CouchLink.App.Input;

internal static partial class NativeMethods
{
    public const int WM_INPUT = 0x00FF;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const ushort RI_KEY_BREAK = 0x01;
    public const ushort MOUSE_MOVE_ABSOLUTE = 0x01;
    public const ushort RI_MOUSE_LEFT_DOWN = 0x0001, RI_MOUSE_LEFT_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_DOWN = 0x0004, RI_MOUSE_RIGHT_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_DOWN = 0x0010, RI_MOUSE_MIDDLE_UP = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static unsafe partial uint GetRawInputData(
        IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint milliseconds);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint milliseconds);
}
