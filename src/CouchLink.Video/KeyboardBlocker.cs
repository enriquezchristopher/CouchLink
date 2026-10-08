using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CouchLink.Core.Input;

namespace CouchLink.Video;

/// <summary>
/// A low-level keyboard hook that swallows what <see cref="ShortcutFilter"/> says (the Windows keys,
/// Alt+Tab, Alt+Esc, Ctrl+Esc). Install and uninstall on the player thread: it pumps messages, so the
/// callback runs there and never waits on the WPF UI thread. The callback only checks a few keys, well
/// inside Windows' limit for slow hooks, and never throws.
/// </summary>
public sealed unsafe partial class KeyboardBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, HC_ACTION = 0, VK_CONTROL = 0x11;
    private const uint LLKHF_ALTDOWN = 0x20;

    private nint _hook;

    public bool IsInstalled => _hook != 0;

    public bool Install(out string? error)
    {
        error = null;
        if (_hook != 0)
            return true;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL,
            (nint)(delegate* unmanaged[Stdcall]<int, nint, nint, nint>)&HookProc, GetModuleHandleW(null), 0);
        if (_hook != 0)
            return true;
        error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
        return false;
    }

    public void Uninstall()
    {
        if (_hook == 0)
            return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == HC_ACTION)
            {
                var info = (KbdLlHookStruct*)lParam;
                bool alt = (info->Flags & LLKHF_ALTDOWN) != 0;
                bool ctrl = GetAsyncKeyState(VK_CONTROL) < 0;
                if (ShortcutFilter.ShouldBlock((ushort)info->VkCode, alt, ctrl))
                    return 1;
            }
        }
        catch (Exception)
        {
            // A hook must never throw into Windows; let the key through.
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, nint proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
