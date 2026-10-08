using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CouchLink.Core.Input;

namespace CouchLink.Video;

/// <summary>
/// A low-level keyboard hook that swallows what <see cref="ShortcutFilter"/> says (the Windows keys,
/// Alt+Tab, Alt+Esc, Ctrl+Esc). Windows calls it for every keystroke on the PC and silently removes a
/// hook whose thread is too slow to answer, so it lives on its own thread that does nothing but pump
/// messages: never the player thread (decoding, presenting) nor the WPF UI thread. The callback only
/// checks a few keys and never throws. Install and uninstall from one thread at a time.
/// </summary>
public sealed unsafe partial class KeyboardBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, HC_ACTION = 0, VK_CONTROL = 0x11;
    private const uint LLKHF_ALTDOWN = 0x20, WM_QUIT = 0x0012;

    private Thread? _thread;

    public bool IsInstalled => _thread is not null;

    /// <summary>The thread the hook runs on while installed, else 0.</summary>
    public uint HookThreadId { get; private set; }

    public bool Install(out string? error)
    {
        error = null;
        if (_thread is not null)
            return true;

        string? failure = null;
        uint threadId = 0;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            nint hook = SetWindowsHookExW(WH_KEYBOARD_LL,
                (nint)(delegate* unmanaged[Stdcall]<int, nint, nint, nint>)&HookProc, GetModuleHandleW(null), 0);
            if (hook == 0)
            {
                failure = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
                ready.Set();
                return;
            }
            ready.Set();
            while (GetMessageW(out _, 0, 0, 0) > 0)
            {
                // The hook callback runs inside GetMessage; nothing else arrives here but WM_QUIT.
            }
            UnhookWindowsHookEx(hook);
        })
        {
            IsBackground = true,
            Name = "CouchLink key blocker",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            thread.Join();
            error = failure;
            return false;
        }
        _thread = thread;
        HookThreadId = threadId;
        return true;
    }

    public void Uninstall()
    {
        if (_thread is not { } thread)
            return;
        PostThreadMessageW(HookThreadId, WM_QUIT, 0, 0);
        thread.Join(TimeSpan.FromSeconds(1));
        _thread = null;
        HookThreadId = 0;
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
    private struct Msg { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; }

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

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
