using System.Runtime.InteropServices;
using System.Windows.Interop;
using CouchLink.Core.Input;
using static CouchLink.App.Input.NativeMethods;

namespace CouchLink.App.Input;

/// <summary>
/// Reads keyboard and relative mouse input for one window via Win32 Raw Input.
/// Only delivers input while that window is in the foreground.
/// </summary>
internal sealed class RawInputSource : IDisposable
{
    private readonly HwndSource _source;

    public event Action<ushort>? KeyDown;
    public event Action<ushort>? KeyUp;
    public event Action<int, int>? MouseMove;

    public RawInputSource(HwndSource source)
    {
        _source = source;
        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = 0, Target = source.Handle }, // keyboard
            new() { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = source.Handle }, // mouse
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
        _source.AddHook(WndProc);
    }

    private unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT)
            return IntPtr.Zero;

        uint headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, headerSize);
        if (size == 0 || size > 1024)
            return IntPtr.Zero;

        byte* buffer = stackalloc byte[(int)size];
        if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
            return IntPtr.Zero;

        var header = *(RAWINPUTHEADER*)buffer;
        byte* data = buffer + headerSize;
        if (header.Type == RIM_TYPEKEYBOARD)
            OnKeyboard(*(RAWKEYBOARD*)data);
        else if (header.Type == RIM_TYPEMOUSE)
            OnMouse(*(RAWMOUSE*)data);
        return IntPtr.Zero;
    }

    private void OnKeyboard(RAWKEYBOARD kb)
    {
        if (kb.VKey is 0 or 0xFF)
            return; // fake/overrun keys
        var vk = VirtualKeys.FromRawKeyboard(kb.VKey, kb.MakeCode, kb.Flags);
        if ((kb.Flags & RI_KEY_BREAK) != 0)
            KeyUp?.Invoke(vk);
        else
            KeyDown?.Invoke(vk);
    }

    private void OnMouse(RAWMOUSE m)
    {
        if ((m.Flags & MOUSE_MOVE_ABSOLUTE) == 0 && (m.LastX != 0 || m.LastY != 0))
            MouseMove?.Invoke(m.LastX, m.LastY);

        Button(m.ButtonFlags, RI_MOUSE_LEFT_DOWN, RI_MOUSE_LEFT_UP, VirtualKeys.LButton);
        Button(m.ButtonFlags, RI_MOUSE_RIGHT_DOWN, RI_MOUSE_RIGHT_UP, VirtualKeys.RButton);
        Button(m.ButtonFlags, RI_MOUSE_MIDDLE_DOWN, RI_MOUSE_MIDDLE_UP, VirtualKeys.MButton);
    }

    private void Button(ushort flags, ushort down, ushort up, ushort vk)
    {
        if ((flags & down) != 0) KeyDown?.Invoke(vk);
        if ((flags & up) != 0) KeyUp?.Invoke(vk);
    }

    public void Dispose() => _source.RemoveHook(WndProc);
}
