using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchLink.App.Presentation;
using CouchLink.App.Theme;
using CouchLink.Core.Session;

namespace CouchLink.App;

/// <summary>
/// "PC-07 wants to join. Allow / Deny", a toast in the bottom-right corner over the game, without
/// taking the keyboard from it; the main window's taskbar button flashes. Several stack upwards. The
/// close button denies. The session denies by itself after 30 s and closes it with <see cref="CloseByHost"/>.
/// </summary>
internal sealed partial class ApprovalPopup : Window
{
    private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _shown = Stopwatch.StartNew();
    private bool _answered;

    public ApprovalPopup(string name, int stackIndex, Action allow, Action deny)
    {
        InitializeComponent();
        SetResourceReference(FontFamilyProperty, "BodyFont");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontSize = 14;
        Heading.Text = $"{name} wants to join";
        AllowButton.Click += (_, _) => Answer(allow);
        DenyButton.Click += (_, _) => Answer(deny);
        CloseButton.Click += (_, _) => Answer(deny);

        UpdateRemaining(TimeSpan.Zero);
        _countdown.Tick += (_, _) => UpdateRemaining(_shown.Elapsed);
        _countdown.Start();
        Loaded += (_, _) =>
        {
            PlaceAt(stackIndex);
            Motion.Enter(Card);
        };
        SourceInitialized += (_, _) => Flash();
        Closed += (_, _) =>
        {
            _countdown.Stop();
            if (!_answered)
                deny();
        };
    }

    /// <summary>The session already answered (timed out, or the client gave up): close without denying again.</summary>
    public void CloseByHost()
    {
        _answered = true;
        Close();
    }

    internal void UpdateRemaining(TimeSpan elapsed)
    {
        var countdown = AskCountdown.For(elapsed, HostSession.AskTimeout);
        Remaining.Text = countdown.Text;
        CountdownScale.ScaleX = countdown.Remaining;
    }

    private void Answer(Action answer)
    {
        _answered = true;
        answer();
        Close();
    }

    private void PlaceAt(int stackIndex)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 4;
        Top = Math.Max(area.Top, area.Bottom - ActualHeight * (stackIndex + 1));
    }

    /// <summary>The toast has no taskbar button of its own, so the main window's flashes.</summary>
    private static void Flash()
    {
        if (Application.Current?.MainWindow is not { } main)
            return;
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(main).Handle,
            Flags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        if (info.Hwnd != 0)
            FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
