using System.Runtime.InteropServices;
using System.Windows.Interop;
using CouchLink.Core.Input;
using static CouchLink.App.Input.NativeMethods;

namespace CouchLink.App.Input;

/// <summary>
/// Reads keyboard and relative mouse input for one window via Win32 Raw Input.
/// Registered as an input sink, so it also reads input while the player window is in front;
/// <c>inputAllowed</c> decides whether a CouchLink window is in front at all.
/// </summary>
internal sealed class RawInputSource : IDisposable
{
    private const int VK_NUMLOCK = 0x90;

    private readonly HwndSource _source;
    private readonly Func<bool> _inputAllowed;
    private bool _suspended;

    /// <summary>Raised once when input arrives while no CouchLink window is in front; release every key.</summary>
    public event Action? InputSuspended;

    public event Action<ushort>? KeyDown;
    public event Action<ushort>? KeyUp;
    public event Action<int, int>? MouseMove;

    public RawInputSource(HwndSource source, Func<bool> inputAllowed)
    {
        _source = source;
        _inputAllowed = inputAllowed;
        RAWINPUTDEVICE[] devices =
        [
            // Input sink: the main window gets input while the player window is in front too.
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = source.Handle }, // keyboard
            new() { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = source.Handle }, // mouse
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
        _source.AddHook(WndProc);
    }

    private unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT)
            return IntPtr.Zero;
        if (!_inputAllowed())
        {
            if (!_suspended)
            {
                _suspended = true;
                InputSuspended?.Invoke();
            }
            return IntPtr.Zero; // another app is in front: its keys are not ours
        }
        _suspended = false;

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
        if (VirtualKeys.IsFakeShift(kb.VKey, kb.MakeCode, kb.Flags))
            return; // Windows' Shift release/press around a numpad key; the real Shift is still down
        bool numLockOn = (GetKeyState(VK_NUMLOCK) & 1) != 0;
        var vk = VirtualKeys.FromRawKeyboard(kb.VKey, kb.MakeCode, kb.Flags, numLockOn);
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
        Button(m.ButtonFlags, RI_MOUSE_BUTTON_4_DOWN, RI_MOUSE_BUTTON_4_UP, VirtualKeys.XButton1);
        Button(m.ButtonFlags, RI_MOUSE_BUTTON_5_DOWN, RI_MOUSE_BUTTON_5_UP, VirtualKeys.XButton2);
    }

    private void Button(ushort flags, ushort down, ushort up, ushort vk)
    {
        if ((flags & down) != 0) KeyDown?.Invoke(vk);
        if ((flags & up) != 0) KeyUp?.Invoke(vk);
    }

    public void Dispose() => _source.RemoveHook(WndProc);
}
