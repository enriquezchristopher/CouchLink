using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace CouchLink.Video;

/// <summary>
/// The player's Win32 window: borderless and covering the monitor of <c>nearWindow</c> (or 1280x720
/// windowed, for testing on one PC), cursor hidden. F1 toggles the controls panel, F2 the stats,
/// Ctrl+Alt+C asks for the controls editor; Ctrl+Alt+Q, Alt+F4 and the close button ask to leave. With
/// lockInput, while it is active it blocks the Windows shortcuts (<see cref="KeyboardBlocker"/>) and
/// keeps the pointer inside it. Messages are handled on the thread that created it.
/// </summary>
public sealed unsafe partial class PlayerWindow : IDisposable
{
    private const string ClassName = "CouchLinkPlayer";
    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014,
        WM_SETCURSOR = 0x0020, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_SYSCOMMAND = 0x0112;
    private const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_F1 = 0x70, VK_F2 = 0x71, VK_Q = 0x51, VK_C = 0x43, WA_INACTIVE = 0;
    private const int HTCLIENT = 1, SC_KEYMENU = 0xF100;
    private const uint PM_REMOVE = 1, QS_ALLINPUT = 0x04FF, MWMO_INPUTAVAILABLE = 0x0004, MONITOR_DEFAULTTOPRIMARY = 1;

    private static readonly Dictionary<nint, PlayerWindow> Windows = [];
    private static bool _registered;
    private bool _destroyed;
    private ExceptionDispatchInfo? _error;
    private readonly bool _lockInput;
    private readonly Action<string>? _log;
    private readonly KeyboardBlocker _blocker = new();
    private bool _inputLocked, _clipFailedLogged;

    public PlayerWindow(nint nearWindow, bool windowed, bool lockInput = false, Action<string>? log = null)
    {
        _lockInput = lockInput && !windowed;
        _log = log;
        RegisterClassOnce();
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfoW(MonitorFromWindow(nearWindow, MONITOR_DEFAULTTOPRIMARY), ref info);
        var m = info.Monitor;
        int x = m.Left, y = m.Top, w = m.Right - m.Left, h = m.Bottom - m.Top;
        uint style = WS_POPUP | WS_VISIBLE;
        if (windowed)
        {
            var frame = new Rect { Right = 1280, Bottom = 720 };
            style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;
            AdjustWindowRectEx(ref frame, style, false, WS_EX_APPWINDOW);
            w = frame.Right - frame.Left;
            h = frame.Bottom - frame.Top;
            x = m.Left + (m.Right - m.Left - w) / 2;
            y = m.Top + (m.Bottom - m.Top - h) / 2;
        }

        Handle = CreateWindowExW(WS_EX_APPWINDOW, ClassName, "CouchLink", style, x, y, w, h, 0, 0, GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the video window failed");
        lock (Windows)
            Windows[Handle] = this;
        GetClientRect(Handle, out var client);
        Width = client.Right;
        Height = client.Bottom;
        SetForegroundWindow(Handle);

        // Creation activated the window before it was registered above, so that WM_ACTIVATE was missed.
        if (_lockInput && GetForegroundWindow() == Handle)
            LockInput();
    }

    public nint Handle { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public event Action? CloseRequested;
    public event Action? StatsToggled;
    public event Action<int, int>? Resized;
    public event Action? ControlsToggled;
    public event Action? ControlsRequested;

    /// <summary>Why the Windows shortcuts can't be blocked, or null. Set on the player thread, read anywhere.</summary>
    public string? InputLockError { get; private set; }

    /// <summary>
    /// Handles every waiting message. False once the window is gone. Rethrows an exception from an
    /// event handler (Resized, StatsToggled, CloseRequested), including one from a message sent to the
    /// window directly rather than through this loop.
    /// </summary>
    public bool PumpMessages()
    {
        while (PeekMessageW(out var message, 0, 0, 0, PM_REMOVE))
        {
            TranslateMessage(in message);
            DispatchMessageW(in message);
        }
        if (_error is { } error)
        {
            _error = null;
            error.Throw();
        }
        return !_destroyed;
    }

    /// <summary>Sleeps until <paramref name="handle"/> is set, a message arrives, or the timeout passes.</summary>
    public void WaitForInput(WaitHandle handle, TimeSpan timeout)
    {
        nint h = handle.SafeWaitHandle.DangerousGetHandle();
        MsgWaitForMultipleObjectsEx(1, &h, (uint)timeout.TotalMilliseconds, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                bool repeat = (lParam & (1 << 30)) != 0;
                if (wParam == VK_F1 && !repeat)
                {
                    ControlsToggled?.Invoke();
                    return 0;
                }
                if (wParam == VK_F2 && !repeat)
                {
                    StatsToggled?.Invoke();
                    return 0;
                }
                if (wParam == VK_Q && GetKeyState(VK_CONTROL) < 0 && GetKeyState(VK_MENU) < 0)
                {
                    CloseRequested?.Invoke();
                    return 0;
                }
                if (wParam == VK_C && GetKeyState(VK_CONTROL) < 0 && GetKeyState(VK_MENU) < 0)
                {
                    ControlsRequested?.Invoke();
                    return 0;
                }
                return null; // Alt+F4 goes on to DefWindowProc, which sends WM_CLOSE
            case WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SC_KEYMENU:
                return 0; // Alt alone must not open a window menu and steal keys
            case WM_ACTIVATE when _lockInput:
                if ((wParam & 0xFFFF) != WA_INACTIVE)
                    LockInput();
                else
                    UnlockInput();
                return null; // DefWindowProc still sets the focus
            case WM_SETCURSOR when (lParam & 0xFFFF) == HTCLIENT:
                SetCursor(0);
                return 1;
            case WM_ERASEBKGND:
                return 1;
            case WM_SIZE:
                Width = (int)(lParam & 0xFFFF);
                Height = (int)((lParam >> 16) & 0xFFFF);
                if (_inputLocked)
                    ClipToWindow();
                if (Width > 0 && Height > 0)
                    Resized?.Invoke(Width, Height);
                return 0;
            case WM_CLOSE:
                CloseRequested?.Invoke(); // the owner decides; the window stays until disposed
                return 0;
            case WM_DESTROY:
                _destroyed = true;
                return 0;
            default:
                return null;
        }
    }

    private void LockInput()
    {
        _inputLocked = true;
        ClipToWindow();
        if (!_blocker.Install(out var error) && InputLockError is null)
        {
            InputLockError = $"Key blocking unavailable ({error})";
            _log?.Invoke($"Could not block the Windows shortcuts: {error}");
        }
    }

    private void UnlockInput()
    {
        if (!_inputLocked)
            return; // never release a clip some other app set
        _inputLocked = false;
        _blocker.Uninstall();
        ClipCursor(null);
    }

    private void ClipToWindow()
    {
        GetWindowRect(Handle, out var r);
        if (!ClipCursor(&r) && !_clipFailedLogged)
        {
            _clipFailedLogged = true;
            _log?.Invoke($"Could not keep the pointer in the player (error {Marshal.GetLastPInvokeError()})");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        PlayerWindow? window;
        lock (Windows)
            Windows.TryGetValue(hwnd, out window);
        if (window is null)
            return DefWindowProcW(hwnd, message, wParam, lParam);
        try
        {
            return window.OnMessage(message, wParam, lParam) ?? DefWindowProcW(hwnd, message, wParam, lParam);
        }
        catch (Exception e)
        {
            // Never let it unwind through Windows' own frames; PumpMessages rethrows it on the player loop.
            window._error ??= ExceptionDispatchInfo.Capture(e);
            return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private static void RegisterClassOnce()
    {
        lock (Windows)
        {
            if (_registered)
                return;
            // The window shows the app's own icon (the exe's) in the taskbar and Alt+Tab.
            nint largeIcon = 0, smallIcon = 0;
            if (Environment.ProcessPath is { } exe)
                ExtractIconExW(exe, 0, &largeIcon, &smallIcon, 1);
            fixed (char* name = ClassName)
            {
                var wc = new WndClassEx
                {
                    Size = (uint)sizeof(WndClassEx),
                    WndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                    Instance = GetModuleHandleW(null),
                    Icon = largeIcon,
                    IconSmall = smallIcon,
                    ClassName = name,
                };
                if (RegisterClassExW(in wc) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Registering the video window class failed");
            }
            _registered = true;
        }
    }

    public void Dispose()
    {
        UnlockInput();
        if (!_destroyed)
            DestroyWindow(Handle);
        lock (Windows)
            Windows.Remove(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size, Style;
        public nint WndProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSmall;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string file, int index, nint* large, nint* small, uint count);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(in WndClassEx wc);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in Msg message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in Msg message);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectEx(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursor(Rect* rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
